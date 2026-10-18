using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaAuraCompletion
    {
        private static QaLedger ledger = null!;
        private static readonly List<object> cases = new();
        private static (float x, float y, float z) point;
        private static double control, tolerance;
        private static object? context;
        private static string scope = "";
        private static bool completed;
        public static bool IsScenario(
            string scenario
        ) => scenario == "aura-completion-cassettes" || scenario == "aura-completion-combined"
        || scenario == "aura-completion-sources"
        || scenario == "aura-completion-settings";
        public static string Scenario(string action)
        {
            string scenario = action.Replace("network-auras", "aura-completion", StringComparison.Ordinal);
            if (!IsScenario(scenario))
                throw new ArgumentException(
                    "Choose network-auras-cassettes, network-auras-combined, network-auras-sources or network-auras-settings; the former combined cycle exceeds the 120-second limit."
                );
            return scenario;
        }
        public static void PreserveFailure(QaLedger owner)
        {
            string prior = Path.Combine(owner.Root, "evidence/R01-R02/aura-completion/cases.json"),
                kept = Path.Combine(owner.Root, "evidence/R01-R02/aura-completion/attempt2-cases.json");
            if (File.Exists(kept))
                kept = Path.Combine(owner.Root, "evidence/R01-R02/aura-completion/attempt3-cases.json");
            if (File.Exists(prior) && !File.Exists(kept))
                QaFiles.Copy(owner, prior, kept);
            string status = Path.Combine(owner.Root, "profiles/client-b/BepInEx/config/qa-gameplay-status.json"),
                target = Path.Combine(owner.Root, "evidence/R01-R02/aura-completion/attempt1-first-control.json");
            if (File.Exists(target) || !File.Exists(status))
                return;
            var result = JsonNode.Parse(QaFiles.ReadSharedText(status))!;
            if (result["sample"]?["median"]?.GetValue<double>() != 0)
                return;
            QaFiles.Write(
                owner,
                target,
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = false,
                        failure = "Recorded zero median movement in this fixture; the snapshot alone does not establish the cause.",
                        observed = result,
                        restore = JsonNode.Parse(
                        File.ReadAllText(Path.Combine(owner.Root, "evidence/R09/restore.json"))
                    ),
                        utc = DateTime.UtcNow
                    }
                )
            );
        }
        public static async Task Run(QaLedger owner, string scenario)
        {
            if (!IsScenario(scenario))
                throw new ArgumentException("Unknown bounded aura scope: " + scenario);
            ledger = owner;
            cases.Clear();
            scope = scenario.Substring("aura-completion-".Length);
            completed = false;
            foreach (string suffix in new[] { ".json" })
            {
                string previous = Path.Combine(owner.Root, "evidence/R01-R02/aura-completion", scope + suffix);
                if (File.Exists(previous))
                    QaHistory.Copy(
                        owner,
                        previous,
                        Path.Combine(
                            QaHistory.Root(owner),
                            "aura-completion",
                            scope + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + suffix
                        )
                    );
            }
            context = new
            {
                fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                roles = new[] { "server", "client-a", "client-b" }.ToDictionary(
                    r => r,
                    r => new
                    {
                        native = JsonNode.Parse(
                        QaFiles.ReadSharedText(Path.Combine(owner.Root, "profiles", r, "BepInEx/config/qa-status.json"))
                    ),
                        configSha = QaFiles.Hash(
                        Path.Combine(owner.Root, "profiles", r, "BepInEx/config/local.valheim.boombox.cfg")
                    )
                    }
                )
            };
            try
            {
                await Cases(owner);
                completed = true;
                Save();
            }
            catch (Exception failure)
            {
                try
                {
                    Record(
                        "AURA-completion-first-failure",
                        false,
                        new
                        {
                            failure = failure.ToString()
                        }
                    );
                }
                catch (Exception recording)
                {
                    throw new AggregateException("Could not record aura failure", failure, recording);
                }
                throw;
            }
        }
        private static async Task Cases(QaLedger owner)
        {
            await QaAuraCases.Prepare(owner);
            context = new
            {
                fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                roles = new[] { "server", "client-a", "client-b" }.ToDictionary(
                    r => r,
                    r => new
                    {
                        native = JsonNode.Parse(
                        QaFiles.ReadSharedText(Path.Combine(owner.Root, "profiles", r, "BepInEx/config/qa-status.json"))
                    ),
                        configSha = QaFiles.Hash(
                        Path.Combine(owner.Root, "profiles", r, "BepInEx/config/local.valheim.boombox.cfg")
                    )
                    }
                )
            };
            var a = await Command("client-a", "snapshot");
            var position = a["position"]!;
            point = (position["x"]!.GetValue<float>(), position["y"]!.GetValue<float>() + 30f, position["z"]!.GetValue<float>());
            await Command(
                "client-b",
                "aura-floor",
                new
                {
                    position = new
                    {
                        point.x,
                        point.y,
                        point.z
                    }
                }
            );
            await QaAuraCases.Teleport("client-b", point.x, point.y, point.z);
            await Command("client-b", "unfreeze");
            await Command(
                "client-b",
                "create",
                new
                {
                    key = "B",
                    ranks = new[] { 1, 3, 3, 3, 2 },
                    decorations = 2,
                    placed = false,
                    position = new
                    {
                        point.x,
                        point.y,
                        point.z
                    }
                }
            );
            await Command(
                "client-b",
                "toggle",
                new
                {
                    key = "B"
                }
            );
            await Task.Delay(2200);
            if (scope == "settings")
            {
                await Command(
                    "client-b",
                    "toggle",
                    new
                    {
                        key = "B"
                    }
                );
                await Command(
                    "client-a",
                    "create",
                    new
                    {
                        key = "A",
                        ranks = new[] { 2, 2, 2, 2, 1 },
                        decorations = 1,
                        placed = true,
                        position = new
                        {
                            x = point.x + 4f,
                            point.y,
                            point.z
                        }
                    }
                );
                await Command(
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
                            x = point.x + 2f,
                            point.y,
                            point.z
                        }
                    }
                );
                await Task.Delay(2200);
                await QaAuraConfigCases.Run(owner);
                Add(
                    "CONFIG-HUD-complete",
                    true,
                    new
                    {
                        configurationProof = "evidence/R03-R04/runtime-cases.json"
                    }
                );
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                return;
            }
            var controls = new List<JsonNode>();
            for (int i = 0; i < 3; i++)
            {
                var move = await Move();
                controls.Add(move);
                if (move["sample"]!["median"]!.GetValue<double>() <= .1)
                {
                    Add(
                        "AURA-movement-control",
                        false,
                        new
                        {
                            controls,
                            scope = "Zero native motion is a fixture failure; active cases not run"
                        }
                    );
                    throw new IOException("Actual native movement control is zero");
                }
            }
            var values = controls.Select(v => v["sample"]!["median"]!.GetValue<double>()).ToArray();
            control = values.OrderBy(v => v).ElementAt(1);
            double spread = values.Max() - values.Min();
            tolerance = Math.Max(.0001, spread / control * 3 + .0001);
            if (control <= .1 || tolerance > .001)
            {
                Add(
                    "AURA-movement-control",
                    false,
                    new
                    {
                        values,
                        control,
                        spread,
                        tolerance,
                        scope = "Unstable control prevents quantitative movement acceptance"
                    }
                );
                throw new IOException("Actual native movement control is unstable");
            }
            Add(
                "AURA-movement-control",
                true,
                new
                {
                    values,
                    control,
                    spread,
                    tolerance,
                    controls,
                    measurement = "Native fixed-tick planar velocity after two-second acceleration; same gear and input; tolerance declared before active observations"
                }
            );
            if (scope == "cassettes")
            {
                await Group("A-cassette-only", new[] { 2, 2, 2, 2, 1 }, 0, false, false, 2, .05);
                await Group("A-decoration-only", new[] { 0, 0, 0, 0, 0 }, 1, false, false, .1, .0025);
            }
            else if (scope == "combined")
            {
                await Group("A-combined", new[] { 2, 2, 2, 2, 1 }, 1, false, false, 2.1, .0525);
                await Group("B-combined", null, 0, true, false, 3.2, .105);
            }
            else
            {
                await Group("A+B", new[] { 2, 2, 2, 2, 1 }, 1, true, false, 5.3, .1575);
                await Group("A+B+C", new[] { 2, 2, 2, 2, 1 }, 1, true, true, 6.3, .2075);
            }
            await Command("client-a", "clear");
            await Command("client-b", "clear");
            Save();
        }
        private static async Task Group(
            string name,
            int[]? ranks,
            int decorations,
            bool b,
            bool c,
            double recovery,
            double speed
        )
        {
            await Command("client-a", "clear");
            var currentB = await Command("client-b", "snapshot");
            if (currentB["owned"]!.AsArray().Single(n => n!["key"]!.GetValue<string>() == "B")!["playing"]!.GetValue<bool>()
                != b)
                await Command(
                    "client-b",
                    "toggle",
                    new
                    {
                        key = "B"
                    }
                );
            if (ranks != null)
                await Command(
                    "client-a",
                    "create",
                    new
                    {
                        key = "A",
                        ranks,
                        decorations,
                        placed = true,
                        position = new
                        {
                            x = point.x + 4f,
                            point.y,
                            point.z
                        }
                    }
                );
            if (c)
                await Command(
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
                            x = point.x + 2f,
                            point.y,
                            point.z
                        }
                    }
                );
            var motion = await Move();
            double measured = motion["sample"]!["median"]!.GetValue<double>(), ratio = measured / control;
            double statusSpeed = motion["effects"]!.AsArray().Sum(e => e!["speed"]!.GetValue<double>());
            bool moved = Math.Abs(ratio - (1 + speed)) <= tolerance && Math.Abs(statusSpeed - speed) <= .000001;
            Add(
                name + "-native-movement",
                moved,
                new
                {
                    expectedBonus = speed,
                    expectedRatio = 1 + speed,
                    control,
                    measured,
                    ratio,
                    statusSpeed,
                    tolerance,
                    motion
                }
            );
            await QaAuraCases.Teleport("client-b", point.x, point.y, point.z);
            await Task.Delay(2200);
            var sample = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 50
                }
            );
            var measuredResources = new double[3];
            var ticks = new double[3];
            string[] resources = { "health", "stamina", "eitr" };
            bool recovered = true;
            for (int i = 0; i < 3; i++)
            {
                ticks[i] = sample["sample"]!["ticks"]![i]!.GetValue<double>();
                measuredResources[i] = sample["sample"]!["after"]![resources[i]]!.GetValue<double>()
                    - sample["sample"]!["before"]![resources[i]]!.GetValue<double>();
                recovered &= ticks[i] >= 1
                    && ticks[i] <= 2
                    && Math.Abs(measuredResources[i] - recovery * ticks[i]) <= .02;
            }
            Add(
                name + "-native-recovery",
                recovered,
                new
                {
                    expectedPerTick = recovery,
                    measuredResources,
                    ticks,
                    sample
                }
            );
            if (!recovered)
                throw new IOException(name + " actual resource delta differs from fixture");
        }
        private static async Task<JsonNode> Move()
        {
            await QaAuraCases.Teleport("client-b", point.x, point.y, point.z);
            await Task.Delay(2200);
            return await Command("client-b", "aura-move");
        }
        private static Task<JsonNode> Command(
            string role,
            string op,
            object? args = null
        ) => QaAuraCases.Command(role, op, args);
        private static void Add(string id, bool passed, object observed)
        {
            Record(id, passed, observed);
            if (!passed)
                throw new IOException("Aura requirement failed: " + id);
        }

        private static void Record(string id, bool passed, object observed)
        {
            cases.Add(new
            {
                id,
                passed,
                observed,
                utc = DateTime.UtcNow
            });
            Save();
            Console.WriteLine("QA_AURA_COMPLETION " + id + " " + passed);
        }
        public static JsonArray RecoveryEvidence(string folder, string candidateSha)
        {
            var resources = new JsonArray();
            foreach (string part in new[] { "cassettes", "combined", "sources", "settings" })
            {
                var proof = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, part + ".json")))!;
                if (proof["candidateSha"]?.GetValue<string>() != candidateSha
                    || proof["scope"]?.GetValue<string>() != part
                    || proof["completed"]?.GetValue<bool>() != true
                    || proof["cases"] is not JsonArray entries
                    || entries.Count == 0
                    || entries.Any(c => c?["passed"]?.GetValue<bool>() != true))
                    throw new IOException("Incomplete or stale bounded aura evidence: " + part);
                var cycle = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, part + "-cycle.json")))!;
                double elapsed = cycle["seconds"]?.GetValue<double>() ?? double.NaN;
                if (cycle["candidateSha"]?.GetValue<string>() != candidateSha
                || cycle["scenario"]?.GetValue<string>() != "aura-completion-" + part
                || cycle["passed"]?.GetValue<bool>() != true
                || !double.IsFinite(elapsed)
                || elapsed < 0
                || elapsed > 120)
                    throw new IOException("Aura scope has no accepted bounded cleanup: " + part);
                string[] groups = part == "cassettes"
                    ? new[] { "A-cassette-only", "A-decoration-only" }
                    : part == "combined"
                        ? new[] { "A-combined", "B-combined" }
                        : part == "sources"
                            ? new[] { "A+B", "A+B+C" }
                            : Array.Empty<string>();
                if (groups.Length != 0
                    && entries.Count(c => c?["id"]?.GetValue<string>() == "AURA-movement-control") != 1)
                    throw new IOException("Native movement control is missing: " + part);
                foreach (string group in groups)
                {
                    foreach (string kind in new[] { "native-movement", "native-recovery" })
                        if (entries.Count(c => c?["id"]?.GetValue<string>() == group + "-" + kind) != 1)
                            throw new IOException("Missing or duplicated aura case: " + group + "-" + kind);
                    resources.Add(
                        entries.Single(c => c?["id"]?.GetValue<string>() == group + "-native-recovery")!.DeepClone()
                    );
                }
                if (part == "settings"
                    && entries.Count(c => c?["id"]?.GetValue<string>() == "CONFIG-HUD-complete") != 1)
                    throw new IOException("Settings/HUD scope is incomplete");
            }
            return resources;
        }
        private static void Save() => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "evidence/R01-R02/aura-completion", scope + ".json"),
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                context,
                scope,
                completed,
                cases,
                utc = DateTime.UtcNow
            })
        );
    }
}
