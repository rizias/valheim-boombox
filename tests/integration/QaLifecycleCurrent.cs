using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaLifecycleCurrent
    {
        private static QaLedger ledger = null!;
        private static readonly List<object> cases = new();
        private static DateTime started;
        private static DateTime coreStarted;
        private static double restartSeconds;
        public static bool KeepTransferredB
        {
            get;
            private set;
        }
        private static JsonNode? passedTransfer;
        private static readonly List<JsonNode> passedOwn = new();
        private static JsonNode? resumedCoast;
        private static string carrySha = "";
        public static void Prepare(QaLedger root)
        {
            ledger = root;
            cases.Clear();
            root.BeginConfigRollback();
            passedTransfer = null;
            KeepTransferredB = false;
            passedOwn.Clear();
            resumedCoast = null;
            carrySha = "";
            string p06Restore = Path.Combine(root.Root, "evidence/R06/current-restore.json");
            if (!File.Exists(p06Restore))
                QaFiles.Copy(root, Path.Combine(root.Root, "evidence/R09/restore.json"), p06Restore);
            string current = Path.Combine(root.Root, "evidence/R07/current-cases.json");
            if (File.Exists(current))
            {
                var previous = JsonNode.Parse(File.ReadAllText(current))!;
                string priorSha = previous["candidateSha"]!.GetValue<string>(),
                    nowSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
                if (priorSha != nowSha)
                    throw new IOException("P07 carried cases belong to another candidate DLL; no passes reused");
                carrySha = priorSha;
                passedTransfer = previous["cases"]!.AsArray().FirstOrDefault(
                    c => c!["id"]!.GetValue<string>() == "LIFE05-native-physical-A-to-B-one-id-local-music"
                        && c["passed"]!.GetValue<bool>()
                )?.DeepClone();
                KeepTransferredB = passedTransfer != null;
                foreach (var entry in previous["cases"]!.AsArray().Where(
                    c => c!["passed"]!.GetValue<bool>()
                        && c["id"]!.GetValue<string>().StartsWith("LIFE06-", StringComparison.Ordinal)
                ))
                    passedOwn.Add(entry!.DeepClone());
                if (previous["cases"]!.AsArray().LastOrDefault()?["d"]?["combat"]?["reference"] != null
                    && passedOwn.Count == 2)
                    resumedCoast = previous["cases"]!.AsArray().Last()!["d"]!["combat"]!["reference"]!.DeepClone();
                string saved = Path.Combine(root.Root, "evidence/R07/failure-" + QaFiles.Hash(current).Substring(0, 8));
                if (!File.Exists(saved + ".json"))
                {
                    QaFiles.Copy(root, current, saved + ".json");
                    QaFiles.Copy(root, Path.Combine(root.Root, "evidence/R09/restore.json"), saved + "-restore.json");
                    foreach (string role in new[] { "server", "client-a", "client-b" })
                        foreach (string log in new[] { "stdout.log", "stderr.log" })
                            QaFiles.Copy(
                                root,
                                Path.Combine(root.Root, "current", role, log),
                                saved + "-" + role + "-" + log
                            );
                }
            }
        }
        public static void Restore() => ledger.RestoreConfigRollback();
        public static async Task Run(QaLedger root, List<QaSupervised> running, string pipe)
        {
            ledger = root;
            started = DateTime.UtcNow;
            try
            {
                var oldLife = JsonNode.Parse(File.ReadAllText(Path.Combine(root.Root, "evidence/R07/pre-fix.json")))!;
                var oldTeleport = JsonNode.Parse(
                    File.ReadAllText(Path.Combine(root.Root, "evidence/R07/teleport-repeat.json"))
                )!;
                if (oldLife["cases"]!.AsArray().Count != 12
                    || oldLife["cases"]!.AsArray().Any(n => !n!["passed"]!.GetValue<bool>())
                    || oldTeleport["cases"]!.AsArray().Any(n => !n!["passed"]!.GetValue<bool>()))
                    throw new IOException("Existing accepted LIFE/teleport proof missing");
                await QaAuraCases.Prepare(root, KeepTransferredB);
                foreach (string role in new[] { "client-a", "client-b" })
                {
                    await C(role, "combat-mute");
                    await W(
                        role,
                        "snapshot",
                        s => s["dead"]?.GetValue<bool>() == false && s["vitals"]?["health"]?.GetValue<double>() > 0
                    );
                }
                JsonNode coast, sea;
                if (resumedCoast != null)
                {
                    coast = resumedCoast.DeepClone();
                    sea = JsonSerializer.SerializeToNode(new
                    {
                        x = 0,
                        y = 0,
                        z = 1
                    })!;
                    await C(
                        "client-a",
                        "aura-position",
                        new
                        {
                            position = coast
                        }
                    );
                    await C("client-a", "freeze");
                }
                else
                {
                    var shore = await C("client-a", "remaining-scene", null, 90);
                    coast = shore["sample"]!["coast"]!.DeepClone();
                    sea = shore["sample"]!["sea"]!.DeepClone();
                }
                await C(
                    "client-b",
                    "aura-position",
                    new
                    {
                        position = new
                        {
                            x = coast["x"]!.GetValue<float>() + 4,
                            y = coast["y"]!.GetValue<float>(),
                            z = coast["z"]!.GetValue<float>()
                        }
                    }
                );
                await C("client-b", "freeze");
                foreach (string role in new[] { "client-a", "client-b" })
                    await C(role, "remaining-local-ref");
                coreStarted = DateTime.UtcNow;
                JsonNode b;
                string source;
                if (KeepTransferredB)
                {
                    cases.Add(passedTransfer!);
                    Save();
                    source = passedTransfer!["observed"]!["b"]!["remaining"]!["source"]!.GetValue<string>();
                    long bId = Native("client-b")["localPlayer"]!.GetValue<long>();
                    foreach (string role in new[] { "client-a", "client-b" })
                        await C(
                            role,
                            "remaining-track",
                            new
                            {
                                source,
                                owner = bId,
                                coast,
                                sea
                            }
                        );
                    b = await C("client-b", "remaining-snapshot");
                    if (Count(b, "inventory") == 0)
                    {
                        await C(
                            "server",
                            "combat-area",
                            new
                            {
                                position = coast
                            }
                        );
                        await W("server", "combat-snapshot", s => s["combat"]!["areaReady"]!.GetValue<bool>(), 45);
                        await W("client-b", "remaining-snapshot", s => Count(s, "chest") == 1, 30);
                        var chest = await C("client-b", "remaining-chest");
                        var cid = chest["remaining"]!["chestId"]!.GetValue<string>().Split(':');
                        long uid = long.Parse(cid[0]);
                        uint obj = uint.Parse(cid[1]);
                        long peer = Native("server")["peers"]!.AsArray().Single(p => p!["playerId"]!.GetValue<long>()
                            == bId)!["uid"]!.GetValue<long>();
                        await C(
                            "server",
                            "remaining-chest-owner",
                            new
                            {
                                uid,
                                @object = obj,
                                source,
                                peer
                            }
                        );
                        await W(
                            "client-b",
                            "remaining-snapshot",
                            s => s["remaining"]!["chestOwner"]!.GetValue<long>() == peer
                        );
                        await C("client-b", "remaining-take");
                        b = await W("client-b", "remaining-snapshot", s => Count(s, "inventory") == 1);
                    }
                    if (Count(b, "inventory") != 1)
                        throw new IOException("Previously transferred native B item missing on resume");
                    await C("client-b", "remaining-equip");
                }
                else
                {
                    var made = await C("client-a", "remaining-create");
                    source = made["remaining"]!["source"]!.GetValue<string>();
                    long aId = made["playerId"]!.GetValue<long>();
                    foreach (string role in new[] { "client-a", "client-b" })
                        await C(
                            role,
                            "remaining-track",
                            new
                            {
                                source,
                                owner = aId,
                                coast,
                                sea
                            }
                        );
                    var before = await C("client-a", "remaining-snapshot");
                    var original = Item(before, "inventory").DeepClone();
                    var dropped = await C("client-a", "remaining-place");
                    await W("client-b", "remaining-snapshot", s => Count(s, "world") == 1);
                    var pickup = await C("client-b", "remaining-pickup");
                    await W("client-b", "remaining-snapshot", s => Count(s, "inventory") == 1);
                    await C("client-b", "remaining-equip");
                    b = await W(
                        "client-b",
                        "remaining-snapshot",
                        s => Count(s, "inventory") == 1
                            && Count(s, "world") == 0
                            && s["sources"]!.AsArray().Count(
                            n => n!["id"]!.GetValue<string>() == source
                            && n["active"]!.GetValue<bool>()
                        )
                            == 1
                            && s["sources"]!.AsArray().Any(
                            n => n!["id"]!.GetValue<string>() == source
                            && n["owner"]!.GetValue<long>() == s["playerId"]!.GetValue<long>()
                        )
                    );
                    var a = await W(
                        "client-a",
                        "remaining-snapshot",
                        s => Count(s, "inventory") == 0
                            && Count(s, "world") == 0
                            && s["remaining"]!["decks"]!.GetValue<int>() == 0
                    );
                    await C(
                        "client-b",
                        "remaining-local-ref",
                        new
                        {
                            play = true
                        }
                    );
                    var localB = await W(
                        "client-b",
                        "remaining-snapshot",
                        s => s["remaining"]!["playing"]!.GetValue<bool>()
                    );
                    Add(
                        "LIFE05-native-physical-A-to-B-one-id-local-music",
                        SamePhysical(original, Item(b, "inventory"))
                            && Item(b, "inventory")["owner"]!.GetValue<long>() == b["playerId"]!.GetValue<long>()
                            && localB["remaining"]!["ownTrackKnown"]!.GetValue<bool>()
                            && !localB["remaining"]!["otherTrackKnown"]!.GetValue<bool>()
                            && localB["remaining"]!["queue"]![0]!.GetValue<string>() == "qa-point3-local-client-b"
                            && localB["remaining"]!["foreignAudio"]!.GetValue<int>() == 0,
                        new
                        {
                            original,
                            dropped,
                            pickup,
                            a,
                            b,
                            localB
                        }
                    );
                }
                await C("client-b", "remaining-synthetic");
                if (passedOwn.Count == 2)
                {
                    foreach (var entry in passedOwn)
                        cases.Add(entry);
                    Save();
                }
                else
                {
                    foreach (string role in new[] { "client-a", "client-b" })
                        await C(role, "remaining-own-prepare");
                    var charged = await W(
                        "client-b",
                        "remaining-snapshot",
                        s => s["remaining"]!["own"]!["charge"]!.GetValue<float>() >= .99,
                        15
                    );
                    var foreignA = await C("client-a", "remaining-snapshot");
                    var attack = await C("client-b", "remaining-attack");
                    var melee = attack["remaining"]!["own"]!["attack"]!;
                    var hits = melee["hits"]!.AsArray();
                    Add(
                        "LIFE06-native-enchant-only-carrier-not-foreign-aura",
                        foreignA["effects"]!.AsArray().Count == 5
                            && foreignA["remaining"]!["own"]!["charge"]!.GetValue<double>() == 0
                            && melee["healthAfter"]!.GetValue<double>() < melee["healthBefore"]!.GetValue<double>()
                            && hits.Any(
                            h => h!["swing"]!.GetValue<double>() > 0
                            && Math.Abs(
                                h["after"]!.GetValue<double>() / h["before"]!.GetValue<double>()
                            - (1 + .2 * h["swing"]!.GetValue<double>())
                            )
                            < .0001
                        ),
                        new
                        {
                            charged,
                            foreignA,
                            attack
                        }
                    );
                    var boatB = await C(
                        "client-b",
                        "remaining-boat",
                        new
                        {
                            lane = 0
                        },
                        30
                    );
                    var boatA = await C(
                        "client-a",
                        "remaining-boat",
                        new
                        {
                            lane = 8
                        },
                        30
                    );
                    var ownBoat = await C("client-b", "remaining-snapshot");
                    var foreignBoat = await C("client-a", "remaining-snapshot");
                    bool positive = ownBoat["remaining"]!["own"]!["pushes"]!.AsArray().Any(BoatForceMatches);
                    Add(
                        "LIFE06-native-boat-own-helmsman-force-not-shared-aura",
                        positive && foreignBoat["remaining"]!["own"]!["ship"]!["controlled"]!.GetValue<bool>()
                            && foreignBoat["remaining"]!["own"]!["ship"]!["isOwner"]!.GetValue<bool>()
                            && foreignBoat["remaining"]!["own"]!["pushes"]!.AsArray().Count == 0
                            && foreignBoat["effects"]!.AsArray().Count == 5,
                        new
                        {
                            boatB,
                            boatA,
                            ownBoat,
                            foreignBoat
                        }
                    );
                    foreach (string role in new[] { "client-a", "client-b" })
                        await C(role, "remaining-boat-stop");
                }
                var validBefore = await C("client-b", "remaining-snapshot");
                Add(
                    "P07-native-equipment-prune-preserves-valid-carrier",
                    validBefore["lifecycle"]!["equipmentMappings"]!.GetValue<int>() == 1
                        && validBefore["lifecycle"]!["destroyedEquipmentOwners"]!.GetValue<int>() == 0
                        && Item(validBefore, "inventory")["id"]!.GetValue<string>() == source
                        && Item(validBefore, "inventory")["equipped"]!.GetValue<bool>(),
                    new
                    {
                        validBefore,
                        priorNativeBaseline = "Saved D24 actualrespawn had equipmentMappings2/destroyedOwners1; new Prune removes only invalid owners/items",
                        carrySha
                    }
                );
                await C(
                    "client-b",
                    "remaining-local-ref",
                    new
                    {
                        play = true
                    }
                );
                var readySave = await W(
                    "client-b",
                    "remaining-snapshot",
                    s => s["remaining"]!["playing"]!.GetValue<bool>()
                );
                var savedPayload = Item(readySave, "inventory").DeepClone();
                var stored = await C("client-b", "remaining-store");
                stored = await W(
                    "client-b",
                    "remaining-snapshot",
                    s => Count(s, "chest") == 1
                        && Count(s, "inventory") == 0
                        && s["remaining"]!["decks"]!.GetValue<int>() == 0
                );
                string chestId = stored["remaining"]!["chestId"]!.GetValue<string>();
                string[] nativeChest = chestId.Split(':');
                long chestUid = long.Parse(nativeChest[0]);
                uint chestObject = uint.Parse(nativeChest[1]);
                var nativeBefore = new[] { Native("server"), Native("client-a"), Native("client-b") };
                DateTime restart = DateTime.UtcNow;
                for (int i = 2; i >= 0; i--)
                {
                    Write(
                        i == 0
                            ? "server"
                            : i == 1
                                ? "client-a"
                                : "client-b",
                        "shutdown"
                    );
                    await running[i].WaitAsync(TimeSpan.FromSeconds(45));
                }
                var previous = running[0];
                running[0] = Launch("server", pipe);
                previous.Dispose();
                await Ready(
                    "server",
                    s => s["worldUID"]!.GetValue<long>() != 0
                        && s["hostPort"]!.GetValue<int>() == 24870
                );
                previous = running[1];
                running[1] = Launch("client-a", pipe);
                previous.Dispose();
                previous = running[2];
                running[2] = Launch("client-b", pipe);
                previous.Dispose();
                foreach (string role in new[] { "client-a", "client-b" })
                {
                    await Ready(
                        role,
                        s => s["localPlayer"]!.GetValue<long>() != 0
                            && s["connection"]!.GetValue<string>() == "Connected"
                    );
                    await C(role, "prepare");
                    await C(
                        role,
                        "remaining-track",
                        new
                        {
                            source,
                            owner = b["playerId"]!.GetValue<long>(),
                            coast,
                            sea
                        }
                    );
                    await C(
                        role,
                        "aura-position",
                        new
                        {
                            position = coast
                        }
                    );
                    await C(role, "freeze");
                }
                await C(
                    "server",
                    "combat-area",
                    new
                    {
                        position = coast
                    }
                );
                await W("server", "combat-snapshot", s => s["combat"]!["areaReady"]!.GetValue<bool>(), 45);
                await W("client-b", "remaining-snapshot", s => Count(s, "chest") == 1, 30);
                var currentChest = await C("client-b", "remaining-chest");
                string[] loadedChest = currentChest["remaining"]!["chestId"]!.GetValue<string>().Split(':');
                chestUid = long.Parse(loadedChest[0]);
                chestObject = uint.Parse(loadedChest[1]);
                var restored = await C("client-b", "remaining-snapshot");
                var nativeB = Native("client-b");
                long peerB = Native("server")["peers"]!.AsArray().Single(
                    p => p!["playerId"]!.GetValue<long>() == nativeB["localPlayer"]!.GetValue<long>()
                )!["uid"]!.GetValue<long>();
                await C(
                    "server",
                    "remaining-chest-owner",
                    new
                    {
                        uid = chestUid,
                        @object = chestObject,
                        source,
                        peer = peerB
                    }
                );
                await W(
                    "client-b",
                    "remaining-snapshot",
                    s => s["remaining"]!["chestOwner"]!.GetValue<long>() == peerB
                );
                await C("client-b", "remaining-take");
                await C("client-b", "remaining-equip");
                var recovered = await W(
                    "client-b",
                    "remaining-snapshot",
                    s => Count(s, "inventory") == 1
                        && Count(s, "chest") == 0
                        && s["remaining"]!["playing"]!.GetValue<bool>()
                );
                restartSeconds = (DateTime.UtcNow - restart).TotalSeconds;
                Add(
                    "LIFE05-native-chest-quit-restart-one-item-complete-payload",
                    SamePhysical(savedPayload, Item(restored, "chest"))
                        && SamePhysical(savedPayload, Item(recovered, "inventory"))
                        && JsonNode.DeepEquals(savedPayload["tracks"], Item(recovered, "inventory")["tracks"])
                        && savedPayload["playlist"]?.GetValue<string>()
                        == Item(recovered, "inventory")["playlist"]?.GetValue<string>()
                        && nativeBefore[0]["pid"]!.GetValue<int>() != Native("server")["pid"]!.GetValue<int>()
                        && nativeBefore[2]["pid"]!.GetValue<int>() != Native("client-b")["pid"]!.GetValue<int>(),
                    new
                    {
                        savedPayload,
                        stored,
                        restored,
                        recovered,
                        nativeBefore,
                        serverAfter = Native("server"),
                        bAfter = Native("client-b"),
                        restartSeconds = (DateTime.UtcNow - restart).TotalSeconds
                    }
                );
                foreach (string role in new[] { "client-a", "client-b" })
                    await C(role, "remaining-clean");
                Add(
                    "P07-focused-current-cases-complete",
                    true,
                    new
                    {
                        seconds = (DateTime.UtcNow - started).TotalSeconds,
                        coreSeconds = (DateTime.UtcNow - coreStarted).TotalSeconds - restartSeconds,
                        restartSeconds,
                        oldLifeSha = QaFiles.Hash(Path.Combine(root.Root, "evidence/R07/pre-fix.json")),
                        oldTeleportSha = QaFiles.Hash(Path.Combine(root.Root, "evidence/R07/teleport-repeat.json")),
                        historicalOnly = "Old LIFE and teleport files are references, not current-candidate acceptance. Only the focused cases above were checked."
                    }
                );
            }
            catch (Exception failure)
            {
                cases.Add(new
                {
                    id = "P07-first-failure",
                    passed = false,
                    error = failure.ToString(),
                    a = Read("client-a"),
                    b = Read("client-b"),
                    d = Read("server")
                });
                Save();
                throw;
            }
        }
        private static bool SamePhysical(
            JsonNode a,
            JsonNode b
        ) => new[] {
            "id",
            "cassette",
            "tier",
            "ranks",
            "slots",
            "placements"
        }.All(
            k => JsonNode.DeepEquals(a[k], b[k])
        );
        private static JsonNode Item(
            JsonNode s,
            string location
        ) => s["remaining"]!["items"]!.AsArray().Single(n => n!["location"]!.GetValue<string>() == location)!;
        private static int Count(
            JsonNode s,
            string location
        ) => s["remaining"]?["items"]?.AsArray().Count(n => n!["location"]!.GetValue<string>() == location) ?? 0;
        private static bool BoatForceMatches(JsonNode? n)
        {
            if (n == null || !n["pushed"]!.GetValue<bool>() || n["lossSum"]!.GetValue<double>() <= 0)
                return false;
            double step = n["step"]!.GetValue<double>(),
                mass = n["mass"]!.GetValue<double>(),
                loss = n["lossSum"]!.GetValue<double>();
            double x = (n["forceAfter"]!["x"]!.GetValue<double>() - n["forceBefore"]!["x"]!.GetValue<double>()) * step
                / mass, z = (n["forceAfter"]!["z"]!.GetValue<double>() - n["forceBefore"]!["z"]!.GetValue<double>())
                * step
                / mass;
            return Math.Sqrt(x * x + z * z) > 0 && Math.Abs(Math.Sqrt(x * x + z * z) - .203 * loss) <= .0001;
        }
        private static Task<JsonNode> C(
            string role,
            string operation,
            object? args = null,
            int seconds = 30
        ) => QaAuraCases.Command(role, operation, args, seconds);
        private static async Task<JsonNode> W(
            string role,
            string operation,
            Func<JsonNode, bool> predicate,
            int seconds = 8
        )
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            do
            {
                var s = await QaAuraCases.Command(role, operation, until: deadline);
                if (predicate(s))
                    return s;
                await QaAuraCases.DelayWithin(deadline, 100);
            } while (DateTime.UtcNow < deadline);
            throw new IOException("P07 native condition missing: " + role + "/" + operation);
        }
        private static JsonNode? Read(string role)
        {
            try
            {
                return JsonNode.Parse(
                    QaFiles.ReadSharedText(
                        Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-gameplay-status.json")
                    )
                );
            }
            catch (IOException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        private static JsonNode Native(string role)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                try
                {
                    return JsonNode.Parse(
                        QaFiles.ReadSharedText(
                            Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json")
                        )
                    )!;
                }
                catch (IOException) { }
                catch (JsonException) { }
                System.Threading.Thread.Sleep(100);
            } while (DateTime.UtcNow < deadline);
            throw new IOException("P07 fresh native JSON unreadable: " + role);
        }
        private static void Write(
            string role,
            string command
        ) => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-command.txt"),
            Encoding.UTF8.GetBytes(command)
        );
        private static async Task Ready(string role, Func<JsonNode, bool> predicate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(150);
            do
            {
                try
                {
                    if (predicate(Native(role)))
                        return;
                }
                catch (IOException) { }
                catch (JsonException) { }
                await Task.Delay(250);
            } while (DateTime.UtcNow < deadline);
            throw new IOException("P07 native restart not ready: " + role);
        }
        private static QaSupervised Launch(string role, string pipe)
        {
            foreach (string stale in new[] {
                "qa-command.txt",
                "qa-status.json",
                "qa-gameplay-command.json",
                "qa-gameplay-status.json"
            })
            {
                string p = Path.Combine(ledger.Root, "profiles", role, "BepInEx/config", stale);
                if (File.Exists(p))
                    ledger.RemoveOwned(p);
            }
            string game = QaEnvironment.RuntimeRoot(role), profile = Path.Combine(ledger.Root, "profiles", role);
            var args = new List<string> {
                "-batchmode",
                "-nographics",
                "-logFile",
                "-",
                "-savedir",
                Path.Combine(profile, "saves"),
                "--doorstop-enabled",
                "true",
                "--doorstop-target-assembly",
                Path.Combine(profile, "BepInEx/core/BepInEx.Preloader.dll")
            };
            if (role == "server")
                args.AddRange(new[] {
                    "-name",
                    "Boombox QA",
                    "-world",
                    "BoomboxQA",
                    "-seed",
                    "boombox-qa-20261004",
                    "-password",
                    "qa-local-password",
                    "-public",
                    "0",
                    "-port",
                    "24870"
                    });
            return new QaSupervised(
                ledger,
                role,
                Path.Combine(
                    game,
                    role == "server"
                        ? "valheim_server.exe"
                        : "valheim.exe"
                ),
                game,
                args,
                pipe: pipe
            );
        }
        private static void Add(string id, bool passed, object observed)
        {
            cases.Add(new
            {
                id,
                passed,
                observed,
                utc = DateTime.UtcNow
            });
            Save();
            Console.WriteLine("QA_P07 " + id + " " + passed);
            if (!passed)
                throw new IOException(id);
        }
        private static void Save() => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "evidence/R07/current-cases.json"),
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                    fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                    started,
                    cases,
                    utc = DateTime.UtcNow,
                    scope = "Only MP-P07 four missing native transfer/persistence/ownEnchant/ownHelmsman; no old12life/P3-6/UI/video/12h repeat"
                }
            )
        );
    }
}
