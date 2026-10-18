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
    public static class QaLifecycleCases
    {
        private static QaLedger ledger = null!;
        private static readonly List<object> cases = new();
        private static string source = "", cassette = "";
        private static long owner, peer;
        private static string evidenceName = "cases.json";
        public static async Task Run(QaLedger root, List<QaSupervised> running, bool teleportOnly = false)
        {
            ledger = root;
            cases.Clear();
            evidenceName = teleportOnly ? "teleport-repeat.json" : "cases.json";
            if (teleportOnly)
            {
                await TeleportRepeat();
                return;
            }
            string evidence = Path.Combine(ledger.Root, "evidence/R07/cases.json");
            if (File.Exists(evidence))
                QaFiles.Copy(ledger, evidence, Path.Combine(ledger.Root, "evidence/R07/previous-cases.json"));
            try
            {
                await Command("server", "host");
                foreach (string role in new[] { "client-a", "client-b" })
                {
                    await Command(role, "prepare");
                    await Command(role, "combat-mute");
                }
                var initial = await Command("client-a", "snapshot");
                var position = initial["position"]!.DeepClone();
                await Task.Delay(3000);
                var nearby = position.DeepClone();
                nearby["x"] = nearby["x"]!.GetValue<float>() + 4f;
                await Command(
                    "client-b",
                    "teleport",
                    new
                    {
                        position = nearby
                    }
                );
                await Until("client-b", s => s["teleporting"]?.GetValue<bool>() == false, 20);
                var created = await Command("client-a", "life-create");
                source = created["lifecycle"]!["source"]!.GetValue<string>();
                cassette = created["lifecycle"]!["cassette"]!.GetValue<string>();
                owner = created["playerId"]!.GetValue<long>();
                var server = await NativeStatus("server", 10);
                peer = server["peers"]!.AsArray().Single(p => p!["playerId"]!.GetValue<long>() == owner)!["uid"]!.GetValue<long>();
                foreach (string role in new[] { "server", "client-b" })
                    await Command(
                        role,
                        "life-track",
                        new
                        {
                            source,
                            cassette,
                            owner,
                            peer
                        }
                    );
                var ready = await Until("client-b", Active, 15);
                Add(
                    "LIFE-baseline",
                    true,
                    new
                    {
                        created,
                        ready
                    }
                );
                foreach (var operation in new[] {
                    ("life-pause", "life-resume", "LIFE-01-pause"),
                    ("life-unequip", "life-equip", "LIFE-01-ordinary-unequip")
                })
                {
                    await Watch("client-b", operation.Item3);
                    var baseline = await Command("client-b", "life-snapshot");
                    if (!Active(baseline))
                        throw new IOException("Pre-action lifecycle baseline was not active");
                    var before = await Command("client-a", "life-snapshot");
                    var stopped = await Command("client-a", operation.Item1);
                    var observed = await Until("client-b", Inactive, 5);
                    double elapsed = Removal(stopped, observed, "actionUtc");
                    var own = await Command("client-a", "life-snapshot");
                    bool reactionStopped = own["lifecycle"]!["state"]!["localWaves"]!.GetValue<int>()
                        == stopped["lifecycle"]!["state"]!["localWaves"]!.GetValue<int>()
                        && own["lifecycle"]!["state"]!["localMotion"]!.GetValue<float>() == 0f
                        && observed["lifecycle"]!["state"]!["remotePlaying"]!.GetValue<int>() == 0;
                    Add(
                        operation.Item3,
                        elapsed >= 0 && elapsed <= 2 && reactionStopped,
                        new
                        {
                            elapsed,
                            baseline,
                            before,
                            stopped,
                            observed,
                            own
                        }
                    );
                    var resumed = await Command("client-a", operation.Item2);
                    ready = await Until("client-b", Active, 15);
                    Add(
                        operation.Item3 + "-return-one-source",
                        Items(resumed, "inventory", 1)
                            && ready["lifecycle"]!["state"]!["activeSources"]!.GetValue<int>() == 1,
                        new
                        {
                            resumed,
                            ready
                        }
                    );
                }
                await Watch("client-b", "death");
                var deathBaseline = await Command("client-b", "life-snapshot");
                if (!Active(deathBaseline))
                    throw new IOException("Death baseline was not active");
                var dead = await Command("client-a", "life-death");
                dead = await Until("client-a", s => s["lifecycle"]?["deathAt"] != null, 5);
                var removed = await Until("client-b", Inactive, 5);
                double deathRemoval = Removal(dead, removed, "deathAt");
                Add(
                    "LIFE-02-native-death",
                    deathRemoval >= -.1 && deathRemoval <= 2 && dead["lifecycle"]!["state"]!["dead"]!.GetValue<bool>()
                        && Items(dead, "grave", 1),
                    new
                    {
                        deathRemoval,
                        nativeDeathStampSamplingSeconds = .1,
                        deathBaseline,
                        dead,
                        removed
                    }
                );
                var returned = await Until(
                    "client-a",
                    s => s["lifecycle"]?["returnAt"] != null
                        && s["lifecycle"]!["state"]!["dead"]!.GetValue<bool>() == false,
                    90
                );
                Add(
                    "LIFE-02-native-respawn",
                    Items(returned, "grave", 1) && Items(returned, "inventory", 0)
                    && returned["lifecycle"]!["destroyedEquipmentOwners"]!.GetValue<int>() == 0,
                    returned
                );
                await Task.Delay(3000);
                await Command(
                    "client-a",
                    "life-teleport",
                    new
                    {
                        position
                    }
                );
                await Until("client-a", s => s["teleporting"]?.GetValue<bool>() == false, 25);
                await Command("client-a", "life-loot");
                var looted = await Until("client-a", s => Items(s, "inventory", 1) && Items(s, "grave", 0), 20);
                var equipped = await Command("client-a", "life-equip");
                ready = await Until("client-b", Active, 15);
                Add(
                    "LIFE-02-return-item-cassette-single",
                    Items(looted, "inventory", 1) && Items(looted, "grave", 0)
                        && equipped["lifecycle"]!["equipmentMappings"]!.GetValue<int>() == 1
                        && equipped["lifecycle"]!["destroyedEquipmentOwners"]!.GetValue<int>() == 0,
                    new
                    {
                        looted,
                        equipped,
                        ready
                    }
                );
                await Task.Delay(3000);
                await Watch("client-b", "teleport");
                var teleportBaseline = await Command("client-b", "life-snapshot");
                if (!Active(teleportBaseline))
                    throw new IOException("Teleport baseline was not active");
                var target = position.DeepClone();
                target["x"] = target["x"]!.GetValue<float>() + 2f;
                var started = await Command(
                    "client-a",
                    "life-teleport",
                    new
                    {
                        position = target
                    }
                );
                removed = await Until("client-b", Inactive, 5);
                double teleportRemoval = Removal(started, removed, "actionUtc");
                Add(
                    "LIFE-03-native-teleport-begin",
                    started["teleporting"]!.GetValue<bool>() && teleportRemoval >= 0 && teleportRemoval <= 2,
                    new
                    {
                        teleportRemoval,
                        teleportBaseline,
                        started,
                        removed
                    }
                );
                var ended = await Until("client-a", s => s["teleporting"]?.GetValue<bool>() == false, 25);
                ready = await Until("client-b", Active, 15);
                Add(
                    "LIFE-03-native-teleport-end",
                    Items(ended, "inventory", 1),
                    new
                    {
                        ended,
                        ready
                    }
                );
                var placedPosition = target.DeepClone();
                placedPosition["x"] = placedPosition["x"]!.GetValue<float>() + 1f;
                var placed = await Command(
                    "client-a",
                    "life-place",
                    new
                    {
                        position = placedPosition
                    }
                );
                ready = await Until("client-b", Active, 15);
                Add(
                    "LIFE-04-placed-before-disconnect",
                    Items(placed, "world", 1),
                    new
                    {
                        placed,
                        ready
                    }
                );
                await Watch("server", "disconnect");
                await Watch("client-b", "disconnect");
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/qa-command.txt"),
                    Encoding.UTF8.GetBytes("shutdown")
                );
                var registered = await Until("server", s => s["lifecycle"]?["disconnectAt"] != null, 30);
                removed = await Until("client-b", Inactive, 5);
                double disconnectRemoval = (DateTime.Parse(removed["lifecycle"]!["removedAt"]!.GetValue<string>())
                    .ToUniversalTime() - DateTime.Parse(registered["lifecycle"]!["disconnectAt"]!.GetValue<string>())
                    .ToUniversalTime()).TotalSeconds;
                var physical = await Command("client-b", "life-snapshot");
                await running[1].WaitAsync(TimeSpan.FromSeconds(30));
                Add(
                    "LIFE-04-owner-disconnect-no-infinite-aura",
                    disconnectRemoval <= 2 && Items(physical, "world", 1),
                    new
                    {
                        disconnectRemoval,
                        registered,
                        removed,
                        physical
                    }
                );
                await Command("client-b", "life-clean-placed");
                await Command("client-b", "clear");
                Save();
                Console.WriteLine("QA_NATIVE_LIFECYCLE_PASS LIFE01-04 focused actual native transitions");
            }
            catch (Exception error)
            {
                cases.Add(new
                {
                    id = "LIFE-cycle",
                    passed = false,
                    error = error.ToString(),
                    a = Read("client-a"),
                    b = Read("client-b")
                });
                Save();
                throw;
            }
        }
        private static async Task TeleportRepeat()
        {
            try
            {
                string original = Path.Combine(ledger.Root, "evidence/R07/cases.json"),
                    preserved = Path.Combine(ledger.Root, "evidence/R07/pre-fix.json");
                if (!File.Exists(preserved))
                    QaFiles.Copy(ledger, original, preserved);
                await Command("server", "host");
                foreach (string role in new[] { "client-a", "client-b" })
                {
                    await Command(role, "prepare");
                    await Command(role, "combat-mute");
                }
                var a = await Command("client-a", "snapshot");
                var position = a["position"]!.DeepClone();
                var nearby = position.DeepClone();
                nearby["x"] = nearby["x"]!.GetValue<float>() + 4f;
                await Task.Delay(3000);
                await Command(
                    "client-b",
                    "teleport",
                    new
                    {
                        position = nearby
                    }
                );
                await Until("client-b", s => s["teleporting"]?.GetValue<bool>() == false, 25);
                var created = await Command("client-a", "life-create");
                source = created["lifecycle"]!["source"]!.GetValue<string>();
                cassette = created["lifecycle"]!["cassette"]!.GetValue<string>();
                owner = created["playerId"]!.GetValue<long>();
                var native = await NativeStatus("server", 10);
                peer = native["peers"]!.AsArray().Single(p => p!["playerId"]!.GetValue<long>() == owner)!["uid"]!.GetValue<long>();
                foreach (string role in new[] { "server", "client-b" })
                    await Command(
                        role,
                        "life-track",
                        new
                        {
                            source,
                            cassette,
                            owner,
                            peer
                        }
                    );
                await Command("client-a", "life-real-audio");
                await Until(
                    "client-a",
                    s => s["lifecycle"]!["state"]!["audioPlaying"]!.GetValue<bool>()
                    && s["lifecycle"]!["state"]!["audioFrame"]!.GetValue<int>() > 2400,
                    20
                );
                var baseline = await Until("client-b", Active, 15);
                await Watch("client-b", "teleport");
                await Watch("client-a", "teleport-owner");
                var target = position.DeepClone();
                target["x"] = target["x"]!.GetValue<float>() + 2f;
                var started = await Command(
                    "client-a",
                    "life-teleport",
                    new
                    {
                        position = target
                    }
                );
                var removed = await Until("client-b", Inactive, 5);
                var ended = await Until("client-a", s => s["teleporting"]?.GetValue<bool>() == false, 25);
                var samples = ended["lifecycle"]!["samples"]!.AsArray()
                    .Where(s => s!["teleporting"]!.GetValue<bool>())
                    .ToArray();
                int waves = started["lifecycle"]!["teleportWaves"]!.GetValue<int>();
                var priorImpulses = started["lifecycle"]!["teleportImpulseIds"]!.AsArray()
                    .Select(i => i!.GetValue<int>())
                    .ToHashSet();
                bool noEnhancements = samples.Length >= 10
                && samples.All(
                    s => s!["localWaves"]!.GetValue<int>() == waves && s["localMotion"]!.GetValue<float>() == 0f
                        && s["worldImpulseIds"]!.AsArray().All(i => priorImpulses.Contains(i!.GetValue<int>()))
                );
                bool audibleRetained = samples.All(s => s!["audioPlaying"]!.GetValue<bool>())
                && samples.Last()!["audioFrame"]!.GetValue<int>() > samples.First()!["audioFrame"]!.GetValue<int>();
                double elapsed = Removal(started, removed, "actionUtc");
                Add(
                    "LIFE-03-owner-enhancements-suspended-real-audio-keeps-playing",
                    noEnhancements && audibleRetained && elapsed >= 0 && elapsed <= 2,
                    new
                    {
                        elapsed,
                        sampleCount = samples.Length,
                        waves,
                        baseline,
                        started,
                        removed,
                        ended,
                        noEnhancements,
                        audibleRetained
                    }
                );
                var ready = await Until("client-b", Active, 15);
                Add(
                    "LIFE-03-return-exactly-one-source",
                    Items(ended, "inventory", 1)
                        && ready["lifecycle"]!["state"]!["activeSources"]!.GetValue<int>() == 1,
                    new
                    {
                        ended,
                        ready
                    }
                );
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                Save();
                Console.WriteLine("QA_NATIVE_LIFECYCLE_TELEPORT_REPEAT_PASS actual IsTeleporting samples");
            }
            catch (Exception error)
            {
                cases.Add(new
                {
                    id = "LIFE-03-repeat",
                    passed = false,
                    error = error.ToString(),
                    a = Read("client-a"),
                    b = Read("client-b")
                });
                Save();
                throw;
            }
        }
        private static bool Items(JsonNode status, string location, int count)
        {
            var items = status["lifecycle"]?["items"]?.AsArray();
            if (items == null)
                return false;
            var matching = items.Where(i => i!["location"]!.GetValue<string>() == location).ToArray();
            return matching.Length == count
            && matching.All(i => i!["id"]!.GetValue<string>() == source && i["cassette"]!.GetValue<string>() == cassette
                && i["ranks"]!.AsArray().All(r => r!.GetValue<int>() == 1));
        }
        private static bool Active(JsonNode status)
        {
            var state = status["lifecycle"]?["state"];
            if (state == null || state["activeSources"]!.GetValue<int>() != 1 || state["effects"]!.GetValue<int>() != 5
            || state["hudIcons"]!.GetValue<int>() != 5)
                return false;
            double[] expected = { .05, 1, 1, 1, .05 };
            return state["values"]!.AsArray().Select((v, i) => Math.Abs(v!.GetValue<double>() - expected[i]) < 1e-5).All(
                v => v
            );
        }
        private static bool Inactive(JsonNode status)
        {
            var state = status["lifecycle"]?["state"];
            return state != null && state["activeSources"]!.GetValue<int>() == 0
            && state["values"]!.AsArray().All(v => v!.GetValue<double>() == 0)
            && state["effects"]!.GetValue<int>() == 0
            && state["hudIcons"]!.GetValue<int>() == 0
            && status["lifecycle"]!["removedAt"] != null;
        }
        private static double Removal(
            JsonNode action,
            JsonNode observed,
            string stamp
        ) => (DateTime.Parse(observed["lifecycle"]!["removedAt"]!.GetValue<string>())
            .ToUniversalTime() - DateTime.Parse(action["lifecycle"]![stamp]!.GetValue<string>())
            .ToUniversalTime()).TotalSeconds;
        private static async Task Watch(
            string role,
            string phase
        ) => await Command(role, "life-watch", new
        {
            source,
            cassette,
            owner,
            peer,
            phase
        });
        private static async Task<JsonNode> NativeStatus(string role, int seconds)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var status = JsonNode.Parse(
                        QaFiles.ReadSharedText(
                            Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json")
                        )
                    );
                    if (status?["peers"] != null
                        && status["utc"] != null
                        && DateTime.UtcNow - DateTime.Parse(status["utc"]!.GetValue<string>()).ToUniversalTime()
                        < TimeSpan.FromSeconds(3))
                        return status;
                }
                catch (IOException) { }
                catch (JsonException) { }
                await Task.Delay(100);
            }
            throw new TimeoutException("Fresh native status deadline: " + role);
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
        private static async Task<JsonNode> Until(string role, Func<JsonNode, bool> condition, int seconds)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                var status = Read(role);
                if (status != null && condition(status))
                    return status;
                if (status?["commandError"] != null || status?["error"] != null)
                    throw new IOException("Native lifecycle snapshot failed: "
                        + (status["commandError"] ?? status["error"]));
                await Task.Delay(100);
            }
            throw new TimeoutException("Native lifecycle state deadline: " + role);
        }
        private static async Task<JsonNode> Command(string role, string operation, object? args = null)
        {
            string id = Guid.NewGuid().ToString("N");
            var request = args == null ? new JsonObject() : JsonSerializer.SerializeToNode(args)!.AsObject();
            request["id"] = id;
            request["action"] = operation;
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-gameplay-command.json"),
                JsonSerializer.SerializeToUtf8Bytes(request)
            );
            var status = await Until(
                role,
                s => s["id"]?.GetValue<string>() == id
                    && s["busy"]?.GetValue<bool>() == false,
                30
            );
            if (status["commandError"] != null || status["error"] != null)
                throw new IOException(
                    "Lifecycle operation failed: " + operation + " " + (status["commandError"] ?? status["error"])
                );
            return status;
        }
        private static void Add(string id, bool passed, object observation)
        {
            cases.Add(new
            {
                id,
                passed,
                observation
            });
            Save();
            Console.WriteLine("QA_LIFE_CASE " + id + " " + passed);
            if (!passed)
                throw new IOException("Native lifecycle criterion failed: " + id);
        }
        private static void Save() => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "evidence/R07", evidenceName),
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                    fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                    scope = "один настоящий Valheim dedicated server и два настоящих клиента A/B из общей клиентской установки; focused native lifecycle numeric source/HUD/item observations; UI/LIFE05/06/Steam/12h not verified",
                    cases,
                    utc = DateTime.UtcNow
                }
            )
        );
    }
}
