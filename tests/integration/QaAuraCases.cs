using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BoomboxQa
{
    public static class QaAuraCases
    {
        private static readonly List<object> cases = new();
        private static QaLedger ledger = null!;
        internal static DateTime? CommandDeadline;
        internal static void UseLedger(QaLedger owner) => ledger = owner;

        public static async Task Prepare(QaLedger owner, bool preserveB = false)
        {
            ledger = owner;
            await Command("server", "host");
            await Command("client-a", "prepare");
            await Command("client-b", preserveB ? "remaining-prepare" : "prepare");
        }

        public static async Task PrepareLocal(QaLedger owner)
        {
            ledger = owner;
            await Command("client-a", "host");
            await Command("client-a", "prepare");
        }

        public static async Task Run(QaLedger owner)
        {
            ledger = owner;
            cases.Clear();
            var a = await Command("client-a", "snapshot");
            var origin = Point(a["position"]!);
            var aPoint = new
            {
                origin.x,
                origin.y,
                origin.z
            };
            await Teleport("client-b", origin.x + 4, origin.y, origin.z);
            await Command(
                "client-a",
                "create",
                new
                {
                    key = "A",
                    ranks = new[] { 2, 2, 2, 2, 1 },
                    decorations = 1,
                    placed = true,
                    position = aPoint
                }
            );
            await Command(
                "client-b",
                "create",
                new
                {
                    key = "B",
                    ranks = new[] { 1, 3, 3, 3, 2 },
                    decorations = 2,
                    placed = false,
                    position = aPoint
                }
            );
            await Task.Delay(2000);
            var sample = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 50
                }
            );
            VerifyRecovery("SUM-01-A+B", sample, 5.3);
            var cPoint = new
            {
                x = origin.x + 2,
                y = origin.y,
                z = origin.z
            };
            await Command(
                "client-a",
                "create",
                new
                {
                    key = "C",
                    ranks = new[] { 1, 1, 1, 1, 1 },
                    decorations = 0,
                    placed = true,
                    position = cPoint
                }
            );
            await Task.Delay(2000);
            sample = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 50
                }
            );
            VerifyRecovery("SUM-01-A+B+C", sample, 6.3);
            await Command(
                "client-a",
                "toggle",
                new
                {
                    key = "C"
                }
            );
            await Task.Delay(2000);
            sample = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 50
                }
            );
            VerifyRecovery("SUM-03-stop-C", sample, 5.3);
            await Command(
                "client-a",
                "toggle",
                new
                {
                    key = "C"
                }
            );
            await Task.Delay(2000);
            var aState = await Command("client-a", "snapshot");
            var c = aState["owned"]!.AsArray().First(n => n!["key"]!.GetValue<string>() == "C")!;
            string[] worldId = c["worldItem"]!.GetValue<string>().Split(':');
            var serverState = JsonNode.Parse(
                QaFiles.ReadSharedText(Path.Combine(ledger.Root, "profiles/server/BepInEx/config/qa-status.json"))
            )!;
            long bPlayer = sample["playerId"]!.GetValue<long>();
            long bPeer = serverState["peers"]!.AsArray().First(p => p!["playerId"]!.GetValue<long>() == bPlayer)!["uid"]!.GetValue<long>();
            await Command(
                "server",
                "worldowner",
                new
                {
                    uid = long.Parse(worldId[0]),
                    @object = uint.Parse(worldId[1]),
                    peer = bPeer
                }
            );
            await Task.Delay(2500);
            sample = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 50
                }
            );
            VerifyRecovery("SUM-04-C-native-owner-B", sample, 6.3);
            var cAfter = await Command("client-a", "snapshot");
            var actualC = cAfter["owned"]!.AsArray().First(n => n!["id"]!.GetValue<string>()
                == c["id"]!.GetValue<string>())!;
            if (actualC["nativeOwner"]!.GetValue<long>() != bPeer)
                throw new IOException("Native ownership migration not observed on A");
            cases.Add(new
            {
                id = "SUM-04-ownership-observation",
                passed = true,
                before = c,
                after = actualC,
                expectedNativePeer = bPeer
            });
            Save();
            await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = .1
                }
            );
            var capped = await Command("client-b", "snapshot");
            foreach (string resource in new[] { "Health", "Stamina", "Eitr" })
                if (capped["vitals"]![char.ToLowerInvariant(resource[0]) + resource.Substring(1)]!.GetValue<double>()
                    > capped["vitals"]!["max" + resource]!.GetValue<double>() + .001)
                    throw new IOException("Native resource exceeded maximum");
            cases.Add(new
            {
                id = "AURA-04-clamp",
                passed = true,
                observed = capped
            });
            Save();
            await Command("client-b", "zeroeitr");
            sample = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 10
                }
            );
            if (sample["vitals"]!["eitr"]!.GetValue<double>() != 0
                || sample["vitals"]!["maxEitr"]!.GetValue<double>() != 0)
                throw new IOException("Native zero-maximum eitr became nonzero");
            cases.Add(new
            {
                id = "AURA-04-eitr-zero",
                passed = true,
                observed = sample
            });
            Save();
            await Command("client-b", "food");
            await Command(
                "client-a",
                "toggle",
                new
                {
                    key = "C"
                }
            );
            var ground = await Command("client-b", "snapshot");
            var level = Point(ground["position"]!);
            var platform = new
            {
                x = level.x,
                y = level.y + 30f,
                z = level.z
            };
            await Command(
                "client-b",
                "floor",
                new
                {
                    position = platform
                }
            );
            await Teleport("client-b", platform.x, platform.y, platform.z);
            await Command("client-b", "freeze");
            foreach (float radius in new[] { 19.9f, 20f, 20.1f })
                await Boundary(radius, false);
            await Boundary(19.9f, true);
            await Command("client-a", "clear");
            await Command("client-b", "clear");
            Save();
        }

        internal static async Task<JsonNode> Command(
            string role,
            string operation,
            object? arguments = null,
            int seconds = 30,
            DateTime? until = null
        )
        {
            if (until.HasValue && DateTime.UtcNow >= until.Value)
                throw new TimeoutException("QA operation deadline exhausted before " + role + "/" + operation);
            if (CommandDeadline is DateTime cap && DateTime.UtcNow >= cap)
                throw new TimeoutException("Bounded aura cycle exhausted its action budget before " + role + "/"
                    + operation);
            string id = Guid.NewGuid().ToString("N");
            var request = arguments == null ? new JsonObject() : JsonSerializer.SerializeToNode(arguments)!.AsObject();
            request["id"] = id;
            request["action"] = operation;
            string profile = Path.Combine(ledger.Root, "profiles", role, "BepInEx/config");
            QaFiles.Write(
                ledger,
                Path.Combine(profile, "qa-gameplay-command.json"),
                JsonSerializer.SerializeToUtf8Bytes(request)
            );
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            if (CommandDeadline is DateTime limit && limit < deadline)
                deadline = limit;
            if (until.HasValue && until.Value < deadline)
                deadline = until.Value;
            while (DateTime.UtcNow < deadline)
            {
                if ((QaLocalFinish.SelectedAction == "local-upgrade-check" || QaLocalFinish.SelectedAction == "migration-mp")
                && QaLocalFinish.UpgradeClock?.Elapsed.TotalSeconds >= 105)
                    throw new TimeoutException("QA command budget exhausted before cleanup");
                try
                {
                    string path = Path.Combine(profile, "qa-gameplay-status.json");
                    if (!File.Exists(path))
                    {
                        await DelayWithin(deadline);
                        continue;
                    }
                    var status = JsonNode.Parse(QaFiles.ReadSharedText(path))!;
                    if (status["id"]?.GetValue<string>() != id || status["busy"]?.GetValue<bool>() != false)
                    {
                        await DelayWithin(deadline);
                        continue;
                    }
                    QaFiles.Write(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R01-R02", role + "-" + operation + ".json"),
                        JsonSerializer.SerializeToUtf8Bytes(status)
                    );
                    if (status["commandError"] != null || status["error"] != null)
                        throw new IOException(
                            "Native " + role + " " + operation + " failed: "
                                + (status["commandError"] ?? status["error"])
                        );
                    return status;
                }
                catch (JsonException) { }
                await DelayWithin(deadline);
            }
            throw new TimeoutException("Native gameplay command timed out: " + role + "/" + operation);
        }

        internal static Task DelayWithin(DateTime deadline, int milliseconds = 250)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return Task.CompletedTask;
            return Task.Delay(remaining < TimeSpan.FromMilliseconds(milliseconds)
                ? remaining : TimeSpan.FromMilliseconds(milliseconds));
        }

        internal static async Task Teleport(string role, float x, float y, float z)
        {
            await Task.Delay(3000);
            await Command(
                role,
                "teleport",
                new
                {
                    position = new
                    {
                        x,
                        y,
                        z
                    }
                }
            );
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
                var status = await Command(role, "snapshot");
                if (status["position"] == null || status["teleporting"] == null)
                    continue;
                var observed = Point(status["position"]!);
                if (Math.Abs(observed.x - x) <= .3f
                    && Math.Abs(observed.z - z) <= .3f
                    && !status["teleporting"]!.GetValue<bool>())
                    return;
            }
            throw new IOException("Native fixture teleport did not settle");
        }

        private static void VerifyRecovery(string id, JsonNode result, double expected)
        {
            var sample = result["sample"]!;
            string[] resources = { "health", "stamina", "eitr" };
            var actual = new double[3];
            var ticks = new double[3];
            for (int index = 0; index < 3; index++)
            {
                ticks[index] = sample["ticks"]![index]!.GetValue<double>();
                actual[index] = sample["after"]![resources[index]]!.GetValue<double>()
                    - sample["before"]![resources[index]]!.GetValue<double>();
            }
            for (int index = 0; index < 3; index++)
            {
                if (ticks[index] < 1 || ticks[index] > 2 || Math.Abs(actual[index] - expected * ticks[index]) > .02)
                {
                    cases.Add(new
                    {
                        id,
                        passed = false,
                        expectedPerTick = expected,
                        actual,
                        ticks,
                        result
                    });
                    Save();
                    throw new IOException(
                        id + " actual " + resources[index] + " recovery mismatch: " + actual[index] + " at "
                            + ticks[index]
                            + " ticks, expected "
                            + expected
                    );
                }
            }
            cases.Add(new
            {
                id,
                passed = true,
                expectedPerTick = expected,
                actual,
                ticks,
                result
            });
            Save();
            Console.WriteLine("QA_NATIVE_AURA_PASS " + id);
        }

        private static async Task Boundary(float radius, bool wall)
        {
            var receiver = await Command("client-b", "snapshot");
            var point = Point(receiver["position"]!);
            var sourcePoint = new
            {
                x = point.x - radius,
                y = point.y,
                z = point.z
            };
            await Command(
                "client-a",
                "moveitem",
                new
                {
                    key = "A",
                    position = sourcePoint
                }
            );
            if (wall)
                await Command(
                    "client-b",
                    "wall",
                    new
                    {
                        position = new
                        {
                            x = point.x - radius / 2,
                            y = point.y,
                            z = point.z
                        }
                    }
                );
            await Task.Delay(2000);
            var result = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 50
                }
            );
            VerifyRecovery(wall ? "AURA-02-resource" : "AURA-01-resource-" + radius, result, radius <= 20f ? 5.3 : 3.2);
            var actualPosition = Point(result["position"]!);
            var source = result["sources"]!.AsArray().First(
                s => s!["owner"]!.GetValue<long>() != result["playerId"]!.GetValue<long>()
                    && s["ranks"]![0]!.GetValue<int>() == 2
            )!;
            var actualSource = Point(source["position"]!);
            float distance = (float)Math.Sqrt(
                Math.Pow(actualPosition.x - actualSource.x, 2) + Math.Pow(actualPosition.y - actualSource.y, 2)
                    + Math.Pow(
                    actualPosition.z - actualSource.z,
                    2
                )
            );
            if (Math.Abs(distance - radius) > .00001 || radius == 20f && distance != 20f)
                throw new IOException("Actual 3D boundary fixture drifted: " + distance + " expected " + radius);
            cases.Add(new
            {
                id = wall ? "AURA-02-geometry" : "AURA-01-geometry-" + radius,
                passed = true,
                radius,
                actual3D = distance,
                receiver = actualPosition,
                source = actualSource,
                wall
            });
            Save();
        }

        private static void Save() => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "evidence/R01-R02/cases.json"),
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                scope = "Real native D/A/B; resource deltas after explicit native natural-regen suppression",
                cases,
                utc = DateTime.UtcNow
            })
        );
        private static (float x, float y, float z) Point(
            JsonNode node
        ) => (node["x"]!.GetValue<float>(), node["y"]!.GetValue<float>(), node["z"]!.GetValue<float>());
    }
}
