using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static partial class QaDamageCases
    {
        private static QaLedger currentLedger = null!;
        private static readonly List<object> currentCases = new();
        private static DateTime currentStarted;
        private static DateTime currentCoreStarted;
        private static string carriedProofSha = "";
        private static string carriedTailProofSha = "";
        public static void PrepareCurrent(QaLedger owner)
        {
            currentLedger = owner;
            currentCases.Clear();
            owner.BeginConfigRollback();
            string restore = Path.Combine(owner.Root, "evidence/R03-R04/point4-restore.json");
            if (!File.Exists(restore))
                QaFiles.Copy(owner, Path.Combine(owner.Root, "evidence/R09/restore.json"), restore);
            string prior = Path.Combine(owner.Root, "evidence/R06/current-cases.json"),
                saved = Path.Combine(owner.Root, "evidence/R06/current-attempt1.json");
            if (File.Exists(prior) && !File.Exists(saved))
                QaFiles.Copy(owner, prior, saved);
            if (File.Exists(prior) && File.Exists(saved) && QaFiles.Hash(prior) != QaFiles.Hash(saved))
            {
                string attempt = Path.Combine(
                    owner.Root,
                    "evidence/R06/failure-" + QaFiles.Hash(prior).Substring(0, 8)
                );
                if (!File.Exists(attempt + ".json"))
                {
                    QaFiles.Copy(owner, prior, attempt + ".json");
                    QaFiles.Copy(
                        owner,
                        Path.Combine(owner.Root, "evidence/R09/restore.json"),
                        attempt + "-restore.json"
                    );
                    foreach (string role in new[] { "server", "client-a", "client-b" })
                        foreach (string name in new[] { "stdout.log", "stderr.log" })
                            QaFiles.Copy(
                                owner,
                                Path.Combine(owner.Root, "current", role, name),
                                attempt + "-" + role + "-" + name
                            );
                }
            }
            foreach (string role in new[] { "server", "client-a", "client-b" })
                foreach (var file in new[] {
                    ("profiles/" + role + "/BepInEx/config/qa-gameplay-status.json", role + "-gameplay.json"),
                    ("profiles/" + role + "/BepInEx/config/qa-status.json", role + "-native.json"),
                    ("current/" + role + "/stdout.log", role + "-stdout.log"),
                    ("current/" + role + "/stderr.log", role + "-stderr.log")
                })
                {
                    string source = Path.Combine(owner.Root, file.Item1), target = Path.Combine(
                        owner.Root,
                        "evidence/R06/current-attempt1-" + file.Item2
                    );
                    if (File.Exists(source) && !File.Exists(target))
                        QaFiles.Copy(owner, source, target);
                }
            string restoreSaved = Path.Combine(owner.Root, "evidence/R06/current-attempt1-restore.json");
            if (!File.Exists(restoreSaved))
                QaFiles.Copy(owner, Path.Combine(owner.Root, "evidence/R09/restore.json"), restoreSaved);
        }
        public static void RestoreCurrent() => currentLedger.RestoreConfigRollback();
        public static void PreserveBootFailure(QaLedger owner)
        {
            string folder = Path.Combine(owner.Root, "evidence/R06/current-boot-attempt2");
            foreach (string role in new[] { "server", "client-a", "client-b" })
                foreach (string name in new[] { "stdout.log", "stderr.log" })
                {
                    string from = Path.Combine(owner.Root, "current", role, name), to = Path.Combine(
                        folder,
                        role + "-" + name
                    );
                    if (File.Exists(from) && !File.Exists(to))
                        QaFiles.Copy(owner, from, to);
                }
            string target = Path.Combine(folder, "proof.json");
            if (!File.Exists(target))
                QaFiles.Write(
                owner,
                target,
                JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    passed = false,
                    scope = "Before client Chainloader/native connection proof; no P06 tail cases ran",
                    roleStatuses = new[] { "server", "client-a", "client-b" }.ToDictionary(
                role => role,
                role =>
                                {
                                    string status = Path.Combine(owner.Root, "profiles", role,
                        "BepInEx/config/qa-status.json");
                                    return File.Exists(status) ? QaFiles.ReadSharedText(status) : null;
                                }),
                    exitCodes = "Unknown; inspect captured role logs",
                    typedPreferencesSha = QaRegistryData.Hash(QaRegistryData.Capture()),
                    nativeProcesses = System.Diagnostics.Process.GetProcessesByName("valheim").Length
                                + System.Diagnostics.Process.GetProcessesByName("valheim_server").Length,
                    registryMarkerExists = File.Exists(
                    Path.Combine(owner.Root, "baseline/registry-active.json")
                ),
                    serverLoaderExists = File.Exists(Path.Combine(QaEnvironment.ServerRoot, "winhttp.dll")),
                    phase = owner.State.Phase,
                    reservationCount = owner.State.Reservations.Count,
                    nativeReservationCount = owner.State.NativeReservations.Count,
                    productSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                    utc = DateTime.UtcNow
                }
                )
                );
        }
        public static async Task RunCurrent(QaLedger owner)
        {
            currentLedger = owner;
            currentStarted = DateTime.UtcNow;
            currentCases.Clear();
            carriedProofSha = carriedTailProofSha = "";
            try
            {
                string candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
                string verifierSha = QaProofIdentity.Current;
                string carriedPath = Path.Combine(owner.Root, "evidence/R06/current-attempt1.json");
                var carried = QaProofIdentity.ReadCompatible(carriedPath, candidateSha, verifierSha);
                string[] expectedPrefix = {
                    "DAMAGE-current-owner-server",
                    "DAMAGE-current-owner-client-a",
                    "DAMAGE-current-owner-client-b",
                    "DAMAGE-current-valid-replay-observer",
                    "DAMAGE-stale-inactive-waits-real-active-snapshot",
                    "DAMAGE-fresh-pause-cancels-pending-no-hit-after-resume"
                };
                var prefix = QaProofIdentity.PassedPrefix(carried, expectedPrefix);
                foreach (var item in prefix)
                    currentCases.Add(item.DeepClone());
                if (prefix.Length > 0)
                    carriedProofSha = QaFiles.Hash(carriedPath);
                string latestPath = Path.Combine(owner.Root, "evidence/R06/current-cases.json");
                var latest = prefix.Length == expectedPrefix.Length
                    ? QaProofIdentity.ReadCompatible(latestPath, candidateSha, verifierSha)
                    : null;
                string[] tailIds = {
                    "DAMAGE-new-placed-registration-and-C-sum",
                    "DAMAGE-unknown-source-native-queue-release",
                    "DAMAGE-current-one-Pulse-opening",
                    "DAMAGE-native-ownership-change-reroutes-not-applied",
                    "DAMAGE-neutral-native-Deer-no-HP-RPC-push"
                };
                var passedTail = QaProofIdentity.PassedPrefix(latest, tailIds, true);
                bool completedTail = passedTail.Length >= 3;
                if (completedTail)
                {
                    foreach (var item in passedTail)
                        currentCases.Add(item.DeepClone());
                    carriedTailProofSha = QaFiles.Hash(latestPath);
                }
                SaveCurrent();
                bool physicsOnly = passedTail.Length == 5;
                await QaAuraCases.Prepare(owner);
                await DC("server", "combat-host");
                foreach (string role in new[] { "client-a", "client-b" })
                    await DW(
                        role,
                        "snapshot",
                        s => s["vitals"]?["health"]?.GetValue<double>() > 0 && s["dead"]?.GetValue<bool>() == false,
                        null,
                        20
                    );
                foreach (string role in new[] { "client-a", "client-b" })
                    await DC(role, "combat-mute");
                foreach (string role in new[] { "server", "client-a", "client-b" })
                    await DC(role, "combat-watch");
                var a = await DC("client-a", "snapshot");
                var position = a["position"]!.DeepClone();
                position["y"] = position["y"]!.GetValue<float>() + 100;
                foreach (string role in new[] { "client-a", "client-b" })
                {
                    await DC(
                        role,
                        "floor",
                        new
                        {
                            position
                        }
                    );
                    await DC(
                        role,
                        "aura-position",
                        new
                        {
                            position = new
                            {
                                x = position["x"]!.GetValue<float>() + (role == "client-b" ? 4 : 0),
                                y = position["y"]!.GetValue<float>(),
                                z = position["z"]!.GetValue<float>()
                            }
                        }
                    );
                    await DC(role, "freeze");
                }
                if (!physicsOnly)
                {
                    await DC(
                        "client-a",
                        "create",
                        new
                        {
                            key = "A",
                            ranks = new[] { 2, 2, 2, 2, 1 },
                            decorations = 1,
                            placed = true,
                            position
                        }
                    );
                    await DC(
                        "client-b",
                        "create",
                        new
                        {
                            key = "B",
                            ranks = new[] { 1, 3, 3, 3, 2 },
                            decorations = 2,
                            placed = true,
                            position = new
                            {
                                x = position["x"]!.GetValue<float>() + 4,
                                y = position["y"]!.GetValue<float>(),
                                z = position["z"]!.GetValue<float>()
                            }
                        }
                    );
                }
                await DC(
                    "server",
                    "combat-area",
                    new
                    {
                        position
                    }
                );
                await DW(
                    "server",
                    "combat-snapshot",
                    s => s["combat"]!["areaReady"]!.GetValue<bool>()
                        && s["combat"]!["sourcePlayers"]!.AsArray().Count >= 2,
                    null,
                    45
                );
                if (physicsOnly)
                {
                    var cleanup = await DC("server", "combat-clean-physics");
                    foreach (string role in new[] { "client-a", "client-b" })
                    {
                        await DC(role, "clear");
                        await DW(role, "combat-waves", s => s["sample"]!["count"]!.GetValue<int>() == 0);
                    }
                    currentCoreStarted = DateTime.UtcNow;
                    var onlyPhysics = await DC("client-a", "combat-physics");
                    VerifyPhysics(onlyPhysics);
                    DA(
                        "P06-current-complete",
                        true,
                        new
                        {
                            seconds = (DateTime.UtcNow - currentStarted).TotalSeconds,
                            coreSeconds = (DateTime.UtcNow - currentCoreStarted).TotalSeconds,
                            carriedProofSha,
                            carriedTailProofSha,
                            cleanup,
                            inherited = "Only old9E tamed/PvP policy unchanged; 11 native D24 cases retained without rerun",
                            oldProof = JsonNode.Parse(
                            File.ReadAllText(Path.Combine(owner.Root, "evidence/R06/cases.json"))
                        )
                        }
                    );
                    return;
                }
                await DC("server", "combat-clean-old");
                var npc = await DC(
                    "server",
                    "combat-spawn",
                    new
                    {
                        position = new
                        {
                            x = position["x"]!.GetValue<float>() + 3,
                            y = position["y"]!.GetValue<float>() + 1,
                            z = position["z"]!.GetValue<float>()
                        }
                    }
                );
                string[] id = npc["combat"]!["npc"]!["id"]!.GetValue<string>().Split(':');
                long uid = long.Parse(id[0]);
                uint obj = uint.Parse(id[1]);
                foreach (string role in new[] { "client-a", "client-b" })
                    await DW(
                        role,
                        "combat-snapshot",
                        s => s["combat"]?["npc"]?["id"]?.GetValue<string>() == id[0] + ":" + id[1],
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                foreach (string role in new[] { "client-a", "client-b" })
                    await DC(
                        role,
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                var identity = await CurrentNativeIdentity(owner);
                long peerA = identity["peers"]!.AsArray().Single(
                    p => p!["playerId"]!.GetValue<long>() == a["playerId"]!.GetValue<long>()
                )!["uid"]!.GetValue<long>();
                long peerB = identity["peers"]!.AsArray().Single(p => p!["uid"]!.GetValue<long>() != peerA)!["uid"]!.GetValue<long>();
                long peerD = npc["combat"]!["npc"]!["owner"]!.GetValue<long>();
                if (currentCases.Count == 0)
                {
                    foreach (var actor in new[] { ("server", peerD), ("client-a", peerA), ("client-b", peerB) })
                    {
                        await DC(
                            "server",
                            "combat-owner",
                            new
                            {
                                uid,
                                @object = obj,
                                peer = actor.Item2
                            }
                        );
                        await DW(
                            actor.Item1,
                            "combat-snapshot",
                            s => s["combat"]?["npc"]?["isOwner"]?.GetValue<bool>() == true,
                            new
                            {
                                uid,
                                @object = obj
                            }
                        );
                        var before = await DC(
                            actor.Item1,
                            "combat-control",
                            new
                            {
                                uid,
                                @object = obj
                            }
                        );
                        var sent = await DC(
                            "client-a",
                            "combat-strike",
                            new
                            {
                                key = "A",
                                uid,
                                @object = obj
                            }
                        );
                        var after = await DW(actor.Item1, "combat-snapshot", s => Health(s) < 999);
                        DA(
                            "DAMAGE-current-owner-" + actor.Item1,
                            Math.Abs(Health(before) - Health(after) - 11.575) <= .01,
                            new
                            {
                                before,
                                after,
                                sent,
                                native = identity
                            }
                        );
                    }
                    var replayBefore = await DC("client-b", "combat-snapshot");
                    var replay = await DC(
                        "client-a",
                        "combat-replay",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    var observer = await DC(
                        "client-b",
                        "combat-replay",
                        new
                        {
                            uid,
                            @object = obj,
                            @event = replay["combat"]!["replay"]!["event"]!.DeepClone()
                        }
                    );
                    await DW(
                        "server",
                        "combat-observe",
                        s => s["sample"]!["pendingBegins"]!.GetValue<int>() == 0
                            && s["sample"]!["pendingRequests"]!.GetValue<int>() == 0
                            && s["sample"]!["pendingDeliveries"]!.GetValue<int>() == 0
                    );
                    var replayAfter = await DC("client-b", "combat-snapshot");
                    DA(
                        "DAMAGE-current-valid-replay-observer",
                        replay["combat"]!["replay"]!["valid"]!.GetValue<bool>()
                            && observer["combat"]!["replay"]!["valid"]!.GetValue<bool>()
                            && Health(replayBefore) == Health(replayAfter),
                        new
                        {
                            replayBefore,
                            replay,
                            observer,
                            replayAfter
                        }
                    );
                    string aid = (await DC("client-a", "snapshot"))["owned"]!.AsArray().Single(
                        n => n!["key"]!.GetValue<string>() == "A"
                    )!["id"]!.GetValue<string>();
                    await DC(
                        "client-a",
                        "toggle",
                        new
                        {
                            key = "A"
                        }
                    );
                    await DW(
                        "client-b",
                        "combat-observe",
                        s => s["sample"]!["sources"]!.AsArray().Any(
                            n => n!["id"]!.GetValue<string>() == aid && !n["active"]!.GetValue<bool>()
                        )
                    );
                    await DC(
                        "client-b",
                        "combat-delay-source",
                        new
                        {
                            source = aid
                        }
                    );
                    await DC(
                        "client-a",
                        "toggle",
                        new
                        {
                            key = "A"
                        }
                    );
                    await DW(
                        "server",
                        "combat-observe",
                        s => s["sample"]!["sources"]!.AsArray().Any(
                            n => n!["id"]!.GetValue<string>() == aid && n["active"]!.GetValue<bool>()
                        )
                    );
                    await DC(
                        "client-b",
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    await DC(
                        "client-a",
                        "combat-strike",
                        new
                        {
                            key = "A",
                            uid,
                            @object = obj
                        }
                    );
                    var stale = await DW(
                        "client-b",
                        "combat-observe",
                        s => s["sample"]!["waiting"]!.GetValue<int>() > 0
                    );
                    await DC("client-b", "combat-release-source");
                    var resumed = await DW("client-b", "combat-snapshot", s => Health(s) < 999);
                    DA(
                        "DAMAGE-stale-inactive-waits-real-active-snapshot",
                        Health(stale) == 1000 && Math.Abs(1000 - Health(resumed) - 11.575) <= .01,
                        new
                        {
                            stale,
                            resumed
                        }
                    );
                    await DC(
                        "client-a",
                        "toggle",
                        new
                        {
                            key = "A"
                        }
                    );
                    await DW(
                        "client-b",
                        "combat-observe",
                        s => s["sample"]!["sources"]!.AsArray().Any(
                            n => n!["id"]!.GetValue<string>() == aid && !n["active"]!.GetValue<bool>()
                        )
                    );
                    await DC(
                        "client-b",
                        "combat-delay-source",
                        new
                        {
                            source = aid
                        }
                    );
                    await DC(
                        "client-a",
                        "toggle",
                        new
                        {
                            key = "A"
                        }
                    );
                    await DW(
                        "server",
                        "combat-observe",
                        s => s["sample"]!["sources"]!.AsArray().Any(
                            n => n!["id"]!.GetValue<string>() == aid && n["active"]!.GetValue<bool>()
                        )
                    );
                    await DC(
                        "client-b",
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    await DC(
                        "client-a",
                        "combat-strike",
                        new
                        {
                            key = "A",
                            uid,
                            @object = obj
                        }
                    );
                    var cancelling = await DW(
                        "client-b",
                        "combat-observe",
                        s => s["sample"]!["waiting"]!.GetValue<int>() > 0
                    );
                    await DC(
                        "client-a",
                        "toggle",
                        new
                        {
                            key = "A"
                        }
                    );
                    var cancelled = await DW(
                        "client-b",
                        "combat-observe",
                        s => s["sample"]!["waiting"]!.GetValue<int>() == 0
                    );
                    await DC(
                        "client-a",
                        "toggle",
                        new
                        {
                            key = "A"
                        }
                    );
                    await DC("client-b", "combat-release-source");
                    await DW(
                        "client-b",
                        "combat-observe",
                        s => s["sample"]!["waiting"]!.GetValue<int>() == 0
                        && s["sample"]!["sources"]!.AsArray().Any(
                            n => n!["id"]!.GetValue<string>() == aid && n["active"]!.GetValue<bool>()
                        )
                    );
                    await DW("server", "combat-observe", s => s["sample"]!["pendingDeliveries"]!.GetValue<int>() == 0);
                    var afterResume = await DC("client-b", "combat-snapshot");
                    DA(
                        "DAMAGE-fresh-pause-cancels-pending-no-hit-after-resume",
                        Health(cancelling) == 1000 && Health(cancelled) == 1000 && Health(afterResume) == 1000,
                        new
                        {
                            cancelling,
                            cancelled,
                            afterResume
                        }
                    );
                }
                await DC(
                    "server",
                    "combat-owner",
                    new
                    {
                        uid,
                        @object = obj,
                        peer = peerB
                    }
                );
                await DW(
                    "client-b",
                    "combat-snapshot",
                    s => s["combat"]?["npc"]?["isOwner"]?.GetValue<bool>() == true,
                    new
                    {
                        uid,
                        @object = obj
                    }
                );
                await DC(
                    "client-b",
                    "combat-control",
                    new
                    {
                        uid,
                        @object = obj
                    }
                );
                currentCoreStarted = DateTime.UtcNow;
                if (!completedTail)
                {
                    await DC(
                        "server",
                        "combat-delay-registration",
                        new
                        {
                            peer = peerA,
                            @short = true
                        }
                    );
                    var createdC = await DC(
                        "client-a",
                        "combat-create-strike",
                        new
                        {
                            key = "C",
                            ranks = new[] { 1, 1, 1, 1, 1 },
                            uid,
                            @object = obj,
                            position = new
                            {
                                x = position["x"]!.GetValue<float>() + 2,
                                y = position["y"]!.GetValue<float>(),
                                z = position["z"]!.GetValue<float>()
                            }
                        }
                    );
                    string cid = createdC["owned"]!.AsArray().Single(n => n!["key"]!.GetValue<string>() == "C")!["id"]!.GetValue<string>();
                    var registration = await DW(
                        "server",
                        "combat-observe",
                        s => s["sample"]?["registrationProof"] != null
                    );
                    var pending = registration["sample"]!["registrationProof"]!["beforeRelease"]!;
                    var registered = await DW("client-b", "combat-snapshot", s => Health(s) < 999);
                    await Settled();
                    registered = await DC("client-b", "combat-snapshot");
                    DA(
                        "DAMAGE-new-placed-registration-and-C-sum",
                        pending["pendingBegins"]!.GetValue<int>() > 0
                            && pending["pendingRequests"]!.GetValue<int>() > 0
                            && pending["registrationAuraSha"] != null
                            && pending["registrationVisualSha"] != null
                            && pending["npc"]!["health"]!.GetValue<double>() == 1000
                            && registration["sample"]!["registrationProof"]!["pendingHoldSeconds"]!.GetValue<double>()
                            <= .3
                            && Math.Abs(1000 - Health(registered) - 12.075) <= .01,
                        new
                        {
                            createdC,
                            registration,
                            registered,
                            cid
                        }
                    );
                    await DC(
                        "client-b",
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    await DC(
                        "client-b",
                        "combat-delay-source",
                        new
                        {
                            unknown = true,
                            @short = true
                        }
                    );
                    var createdUnknown = await DC(
                        "client-a",
                        "combat-create-strike",
                        new
                        {
                            key = "UNKNOWN",
                            ranks = new[] { 0, 0, 0, 0, 0 },
                            uid,
                            @object = obj,
                            position = new
                            {
                                x = position["x"]!.GetValue<float>() + 2,
                                y = position["y"]!.GetValue<float>(),
                                z = position["z"]!.GetValue<float>() + 2
                            }
                        }
                    );
                    var unknown = await DW("client-b", "combat-observe", s => s["sample"]?["sourceProof"] != null);
                    var unknownProof = unknown["sample"]!["sourceProof"]!;
                    var unknownPending = unknownProof["beforeRelease"]!;
                    string unknownId = createdUnknown["owned"]!.AsArray().Single(n => n!["key"]!.GetValue<string>()
                        == "UNKNOWN")!["id"]!.GetValue<string>();
                    var appliedUnknown = await DW("client-b", "combat-snapshot", s => Health(s) < 999);
                    await Settled();
                    appliedUnknown = await DC("client-b", "combat-snapshot");
                    DA(
                        "DAMAGE-unknown-source-native-queue-release",
                        unknownProof["source"]!.GetValue<string>() == unknownId
                            && unknownPending["waiting"]!.GetValue<int>() > 0
                            && unknownPending["auraSha"] != null
                            && unknownPending["npc"]!["health"]!.GetValue<double>() == 1000
                            && !unknownPending["sources"]!.AsArray().Any(n => n!["id"]!.GetValue<string>() == unknownId)
                            && unknownProof["pendingHoldSeconds"]!.GetValue<double>() <= .3
                            && Math.Abs(1000 - Health(appliedUnknown) - 12.075) <= .01,
                        new
                        {
                            createdUnknown,
                            unknown,
                            appliedUnknown
                        }
                    );
                    await DC(
                        "client-b",
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    await DC(
                        "client-a",
                        "combat-pulse",
                        new
                        {
                            key = "A"
                        }
                    );
                    var pulse = await DW("client-b", "combat-snapshot", s => Health(s) < 999);
                    await Settled();
                    pulse = await DC("client-b", "combat-snapshot");
                    DA("DAMAGE-current-one-Pulse-opening", Math.Abs(1000 - Health(pulse) - 12.075) <= .01, pulse);
                }
                else
                {
                    var preparedC = await DC(
                        "client-a",
                        "create",
                        new
                        {
                            key = "C",
                            ranks = new[] { 1, 1, 1, 1, 1 },
                            decorations = 0,
                            placed = true,
                            position = new
                            {
                                x = position["x"]!.GetValue<float>() + 2,
                                y = position["y"]!.GetValue<float>(),
                                z = position["z"]!.GetValue<float>()
                            }
                        }
                    );
                    string preparedId = preparedC["owned"]!.AsArray().Single(n => n!["key"]!.GetValue<string>() == "C")!["id"]!.GetValue<string>();
                    foreach (string role in new[] { "server", "client-b" })
                        await DW(
                            role,
                            "combat-observe",
                            s => s["sample"]!["sources"]!.AsArray().Any(
                                n => n!["id"]!.GetValue<string>() == preparedId && n["active"]!.GetValue<bool>()
                            )
                        );
                }
                if (passedTail.Length < 4)
                {
                    await DC(
                        "client-b",
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    await DC(
                        "server",
                        "combat-arm-owner-change",
                        new
                        {
                            owner = peerB,
                            peer = peerA
                        }
                    );
                    await DC(
                        "client-b",
                        "combat-delay-apply",
                        new
                        {
                            @short = true,
                            peer = peerA
                        }
                    );
                    await DC(
                        "client-a",
                        "combat-strike",
                        new
                        {
                            key = "A",
                            uid,
                            @object = obj
                        }
                    );
                    var held = await DW("client-b", "combat-observe", s => s["sample"]?["applyProof"] != null);
                    var redirected = await DW(
                        "client-a",
                        "combat-snapshot",
                        s => s["combat"]?["npc"]?["isOwner"]?.GetValue<bool>() == true && Health(s) < 999,
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    await Settled();
                    redirected = await DC("client-a", "combat-snapshot");
                    var applyProof = held["sample"]!["applyProof"]!;
                    if (applyProof["passed"]?.GetValue<bool>() != true)
                        throw new IOException("Apply hold fixture failed: " + applyProof["error"]);
                    DA(
                        "DAMAGE-native-ownership-change-reroutes-not-applied",
                        applyProof["before"]!["npc"]!["health"]!.GetValue<double>() == 1000
                            && applyProof["before"]!["applySha"] != null
                            && applyProof["before"]!["npc"]!["owner"]!.GetValue<long>() == peerB
                            && applyProof["ownerChanged"]!["npc"]!["owner"]!.GetValue<long>() == peerA
                            && applyProof["pendingHoldSeconds"]!.GetValue<double>() <= .3
                            && Math.Abs(1000 - Health(redirected) - 12.075) <= .01,
                        new
                        {
                            held,
                            redirected,
                            server = await DC("server", "combat-observe")
                        }
                    );
                }
                await DC(
                    "server",
                    "combat-owner",
                    new
                    {
                        uid,
                        @object = obj,
                        peer = peerD
                    }
                );
                await DW("server", "combat-snapshot", s => s["combat"]!["npc"]!["isOwner"]!.GetValue<bool>());
                await DC("server", "combat-clear");
                var deer = await DC(
                    "server",
                    "combat-spawn",
                    new
                    {
                        prefab = "Deer",
                        dynamic = true,
                        position = new
                        {
                            x = position["x"]!.GetValue<float>() + 6,
                            y = position["y"]!.GetValue<float>() + 1,
                            z = position["z"]!.GetValue<float>()
                        }
                    }
                );
                string[] deerId = deer["combat"]!["npc"]!["id"]!.GetValue<string>().Split(':');
                uid = long.Parse(deerId[0]);
                obj = uint.Parse(deerId[1]);
                await DW(
                    "client-a",
                    "combat-snapshot",
                    s => s["combat"]?["npc"]?["id"]?.GetValue<string>() == deerId[0] + ":" + deerId[1],
                    new
                    {
                        uid,
                        @object = obj
                    }
                );
                var neutralBefore = await DC("server", "combat-observe");
                await DC("server", "safety-push");
                await DC(
                    "client-a",
                    "combat-strike",
                    new
                    {
                        key = "A",
                        uid,
                        @object = obj
                    }
                );
                await Task.Delay(100);
                var neutralAfter = await DW(
                    "server",
                    "combat-observe",
                    s => s["sample"]!["pendingBegins"]!.GetValue<int>() == 0
                        && s["sample"]!["pendingRequests"]!.GetValue<int>() == 0
                        && s["sample"]!["pendingDeliveries"]!.GetValue<int>() == 0
                );
                DA(
                    "DAMAGE-neutral-native-Deer-no-HP-RPC-push",
                    Health(neutralBefore) == Health(neutralAfter)
                        && neutralAfter["sample"]!["npcBody"]!["waveCalls"]!.GetValue<int>()
                        == neutralBefore["sample"]!["npcBody"]!["waveCalls"]!.GetValue<int>()
                        && Planar(neutralAfter["sample"]!["npcBody"]!["velocity"]!) <= .05,
                    new
                    {
                        neutralBefore,
                        neutralAfter
                    }
                );
                await DC("server", "combat-clear");
                foreach (string role in new[] { "client-a", "client-b" })
                    await DC(role, "clear");
                foreach (string role in new[] { "client-a", "client-b" })
                    await DW(role, "combat-waves", s => s["sample"]!["count"]!.GetValue<int>() == 0);
                var physics = await DC("client-a", "combat-physics");
                VerifyPhysics(physics);
                DA(
                    "P06-current-complete",
                    true,
                    new
                    {
                        seconds = (DateTime.UtcNow - currentStarted).TotalSeconds,
                        coreSeconds = (DateTime.UtcNow - currentCoreStarted).TotalSeconds,
                        carriedProofSha,
                        carriedTailProofSha,
                        inherited = "Only old9E tamed/PvP policy: unchanged IsHostile/CombatDamage/player exclusions, now earlier Apply checks; not claiming whole old suite on current SHA",
                        oldProof = JsonNode.Parse(File.ReadAllText(Path.Combine(owner.Root, "evidence/R06/cases.json")))
                    }
                );
            }
            catch (Exception failure)
            {
                var roles = new JsonObject();
                foreach (string role in new[] { "server", "client-a", "client-b" })
                {
                    try
                    {
                        roles[role] = JsonNode.Parse(
                            QaFiles.ReadSharedText(
                                Path.Combine(owner.Root, "profiles", role, "BepInEx/config/qa-gameplay-status.json")
                            )
                        );
                    }
                    catch (Exception capture) when (capture is IOException || capture is JsonException)
                    {
                        roles[role] = capture.Message;
                    }
                }
                currentCases.Add(new
                {
                    id = "P06-first-failure",
                    passed = false,
                    error = failure.ToString(),
                    roles
                });
                SaveCurrent();
                throw;
            }
        }
        private static Task<JsonNode> DC(
            string role,
            string action,
            object? args = null
        ) => QaAuraCases.Command(role, action, args);
        private static async Task<JsonNode> CurrentNativeIdentity(QaLedger owner)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                try
                {
                    return JsonNode.Parse(
                        QaFiles.ReadSharedText(
                            Path.Combine(owner.Root, "profiles/server/BepInEx/config/qa-status.json")
                        )
                    )!;
                }
                catch (Exception failure) when (failure is IOException || failure is JsonException)
                {
                    await Task.Delay(100);
                }
            } while (DateTime.UtcNow < deadline);
            throw new IOException("Live native identity remained unreadable for three seconds");
        }
        private static Task<JsonNode> Settled() => DW(
            "server",
            "combat-observe",
            s => s["sample"]!["pendingBegins"]!.GetValue<int>() == 0
                && s["sample"]!["pendingRequests"]!.GetValue<int>() == 0
                && s["sample"]!["pendingDeliveries"]!.GetValue<int>() == 0
        );
        private static async Task<JsonNode> DW(
            string role,
            string action,
            Func<JsonNode, bool> predicate,
            object? args = null,
            int timeout = 5
        )
        {
            DateTime until = DateTime.UtcNow.AddSeconds(timeout);
            do
            {
                var s = await DC(role, action, args);
                if (predicate(s))
                    return s;
                await Task.Delay(100);
            } while (DateTime.UtcNow < until);
            throw new IOException("Current native damage condition missing: " + role + "/" + action);
        }
        private static double Health(JsonNode s) => s["combat"]!["npc"]!["health"]!.GetValue<double>();
        private static double Planar(
            JsonNode v
        ) => Math.Sqrt(Math.Pow(v["x"]!.GetValue<double>(), 2) + Math.Pow(v["z"]!.GetValue<double>(), 2));
        private static double Delta(
            JsonNode a,
            JsonNode b
        ) => Math.Sqrt(
            Math.Pow(
                a["x"]!.GetValue<double>() - b["x"]!.GetValue<double>(),
                2
            )
                + Math.Pow(
                a["z"]!.GetValue<double>() - b["z"]!.GetValue<double>(),
                2
            )
        );
        private static void VerifyPhysics(JsonNode result)
        {
            var s = result["sample"]!;
            var before = s["baseline"]!.AsArray();
            var first = s["first"]!.AsArray();
            var secondBefore = s["beforeSecond"]!.AsArray();
            var second = s["second"]!.AsArray();
            var thirdBefore = s["beforeThird"]!.AsArray();
            var third = s["third"]!.AsArray();
            double positive = Delta(first[0]!["velocity"]!, before[0]!["velocity"]!),
                cooldown = Planar(second[0]!["velocity"]!) - Planar(secondBefore[0]!["velocity"]!),
                again = Delta(third[0]!["velocity"]!, thirdBefore[0]!["velocity"]!);
            bool dynamic = before.Take(4).All(
                b => b!["body"]!.GetValue<bool>()
                    && !b["kinematic"]!.GetValue<bool>()
                    && (b["constraints"]!.GetValue<int>() & 14) == 0
            );
            bool excluded = Enumerable.Range(1, 4).All(
                i => Delta(before[i]!["position"]!, third[i]!["position"]!) <= .01
                    && (before[i]!["velocity"] == null || Delta(before[i]!["velocity"]!, third[i]!["velocity"]!) <= .01)
            );
            bool inside = before.All(b => b!["closestDistance"]!.GetValue<double>()
                <= s["maximumRadius"]!.GetValue<double>());
            DA(
                "DAMAGE05-native-owner-A-drop-cap-cooldown-exclusions",
                dynamic
                    && excluded
                    && inside
                    && first[0]!["owner"]!.GetValue<long>() == s["nativeUID"]!.GetValue<long>()
                    && positive > 0
                    && positive <= 2.0001
                    && cooldown <= .0001
                    && again > 0
                    && again <= 2.0001
                    && s["hitCounts"]!.AsArray().Select(n => n!.GetValue<int>()).SequenceEqual(new[] { 1, 0, 1 }),
                new
                {
                    positive,
                    cooldown,
                    again,
                    physics = result
                }
            );
        }
        private static void DA(string id, bool passed, object observed)
        {
            currentCases.Add(new
            {
                id,
                passed,
                observed,
                utc = DateTime.UtcNow
            });
            SaveCurrent();
            Console.WriteLine("QA_DAMAGE " + id + " " + passed);
            if (!passed)
                throw new IOException(id);
        }
        private static void SaveCurrent() => QaFiles.Write(
            currentLedger,
            Path.Combine(currentLedger.Root, "evidence/R06/current-cases.json"),
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                verifierSha = QaProofIdentity.Current,
                carriedProofSha,
                carriedTailProofSha,
                currentStarted,
                currentCases,
                utc = DateTime.UtcNow,
                scope = "Only P06 current damage/races/neutral/physics; no P3/P4/P5/boathelp/UI/video repeat"
            })
        );
    }
}
