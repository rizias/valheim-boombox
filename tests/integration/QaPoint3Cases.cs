using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaPoint3Cases
    {
        private static QaLedger ledger = null!;
        private static readonly List<object> results = new();
        private static (float x, float y, float z) origin;
        private static bool tail;
        private static bool audioOnly;
        public static void Evidence(QaLedger owner)
        {
            string folder = Path.Combine(owner.Root, "evidence/R01-R02"),
                path = Path.Combine(folder, "point3-current.json");
            var proof = JsonNode.Parse(File.ReadAllText(path))!;
            if (!proof["cases"]!.AsArray().All(c => c!["passed"]!.GetValue<bool>()))
                throw new IOException("Point3 current matrix still contains failure");
            var contexts = new JsonObject();
            foreach (string role in new[] { "server", "client-a", "client-b" })
                contexts[role] = JsonNode.Parse(File.ReadAllText(Path.Combine(
                            owner.Root,
                            "evidence/R09",
                            role + "-connected.json"
                        )));
            var restore = JsonNode.Parse(File.ReadAllText(Path.Combine(owner.Root, "evidence/R09/restore.json")))!;
            if (restore["sharedPreferencesRestored"]!.GetValue<bool>() != true
            || restore["ownedProcessesStopped"]!.GetValue<bool>() != true
            || restore["serverBootstrapRemoved"]!.GetValue<bool>() != true)
                throw new IOException("Point3 native restore incomplete");
            if (owner.State.Reservations.Count != 0
                || owner.State.NativeReservations.Count != 0
                || owner.State.External.Count != 0
                || owner.State.Phase != "ready")
                throw new IOException("Point3 ledger is not quiescent");
            string candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
            if (proof["candidateSha"]?.GetValue<string>() != candidateSha)
                throw new IOException("Point3 proof belongs to another product DLL");
            var resources = QaAuraCompletion.RecoveryEvidence(Path.Combine(folder, "aura-completion"), candidateSha);
            proof["nativeContext"] = contexts;
            proof["restore"] = restore;
            proof["defaultFiveSecondRecoveryProof"] = resources;
            proof["contextRecordedUtc"] = DateTime.UtcNow.ToString("O");
            proof["nativeStartupTimes"] = new JsonObject
            {
                ["server"] = contexts["server"]!["startUTC"]!.DeepClone(),
                ["client-a"] = contexts["client-a"]!["startUTC"]!.DeepClone(),
                ["client-b"] = contexts["client-b"]!["startUTC"]!.DeepClone()
            };
            proof["footprint"] = JsonSerializer.SerializeToNode(new
            {
                phase = owner.State.Phase,
                files = owner.State.Files.Count,
                bytes = owner.State.Files.Values.Sum(f => f.Bytes),
                reservations = owner.State.Reservations.Count,
                free = new DriveInfo(Path.GetPathRoot(owner.Project)!).AvailableFreeSpace
            });
            proof["applicability"] =
                JsonSerializer.SerializeToNode(
                new
                {
                    current = "D9 native five-speed windows, all three 3D boundaries and wall, six five-second resource groups, same-physical source IDs and totals, both own Source/Listener tones. No old315C numeric pass copied blindly.",
                    damage = "Current wall/boundary arrays independently measure summed damage15.75/20.75% and outsideB+C10.5%; BoomboxMusicNetwork.Begin multiplies native tier damage by1+the same BoomboxAuraMath.Aggregate at source-owner position. Existing R06 actual11.575/12.075 positive hits retain their original9E53 SHA and require root applicability review; no new NPC damage run claimed.",
                    deferred = "Transfer/persistence belongs to point7. Remote-view cleanup NullReferenceException belongs to point5 and is not concealed by point3 acceptance.",
                    sourceHashes = new[] {
                "BoomboxAuraData.cs",
                "BoomboxAuraSources.cs",
                "BoomboxAuraRegistry.cs",
                "BoomboxAuraNetwork.cs",
                "BoomboxAuraRecipient.cs",
                "BoomboxMusicNetwork.cs",
                "BoomboxRemoteCodec.cs"
                        }.ToDictionary(n => n, n => QaFiles.Hash(Path.Combine(owner.Project, "src", n)))
                }
            );
            QaFiles.Write(owner, path, JsonSerializer.SerializeToUtf8Bytes(proof));
            QaFiles.Copy(
                owner,
                Path.Combine(owner.Root, "evidence/R09/restore.json"),
                Path.Combine(folder, "point3-final-restore.json")
            );
            Console.WriteLine("QA_POINT3_FINAL_EVIDENCE native context/default recovery/restore retained");
        }
        public static void PrepareTail(QaLedger owner)
        {
            string prior = Path.Combine(owner.Root, "evidence/R01-R02/point3-current.json"),
                kept = Path.Combine(owner.Root, "evidence/R01-R02/point3-attempt3.json");
            string tailFailure = Path.Combine(owner.Root, "evidence/R01-R02/point3-tail-attempt1.json");
            if (File.Exists(tailFailure))
                tailFailure = Path.Combine(owner.Root, "evidence/R01-R02/point3-tail-attempt2.json");
            if (File.Exists(tailFailure))
                tailFailure = Path.Combine(owner.Root, "evidence/R01-R02/point3-tail-attempt3.json");
            if (File.Exists(prior) && !File.Exists(tailFailure)
            && JsonNode.Parse(File.ReadAllText(prior))?["tail"]?.GetValue<bool>() == true)
            {
                QaFiles.Copy(owner, prior, tailFailure);
                QaFiles.Copy(
                    owner,
                    Path.Combine(owner.Root, "evidence/R09/restore.json"),
                    Path.ChangeExtension(tailFailure, null) + "-restore.json"
                );
            }
            audioOnly = false;
            JsonNode? savedWall = File.Exists(prior)
                ? JsonNode.Parse(File.ReadAllText(prior))?["cases"]?.AsArray().FirstOrDefault(
                c => c?["id"]?.GetValue<string>() == "current-wall" && c["passed"]!.GetValue<bool>()
            )?.DeepClone()
                : null;
            if (!File.Exists(kept))
                QaFiles.Copy(owner, prior, kept);
            string restore = Path.Combine(owner.Root, "evidence/R01-R02/point3-attempt3-restore.json");
            if (!File.Exists(restore))
                QaFiles.Copy(owner, Path.Combine(owner.Root, "evidence/R09/restore.json"), restore);
            ledger = owner;
            results.Clear();
            var saved = JsonNode.Parse(File.ReadAllText(kept))!;
            string currentSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
            if (saved["candidateSha"]!.GetValue<string>() != currentSha)
                throw new IOException("Passed point3 prefix belongs to a different product DLL");
            foreach (var entry in saved["cases"]!.AsArray().Where(c => c!["passed"]!.GetValue<bool>()))
                results.Add(entry!.DeepClone());
            if (savedWall != null)
            {
                results.Add(savedWall);
                audioOnly = true;
            }
            var outside = saved["cases"]!.AsArray().Single(
                c => c?["observed"]?["distance"] is JsonValue distance
                    && Math.Abs(distance.GetValue<double>() - 20.1) < .00002
            )!;
            var observed = outside["observed"]!;
            double[] values = Values(observed["observed"]!["sample"]!["values"]!), expected = { .055 + .05, 4.2, 4.2, 4.2, .105 + .05 };
            bool passed = values.Zip(expected, (a, b) => Math.Abs(a - b) <= .00001).All(b => b) && values.Length == 5
            && Math.Abs(observed["actual3D"]!.GetValue<double>() - 20.1) <= .00002;
            var ids = observed["observed"]!["sample"]!["ids"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
            passed &= ids.Length == 3 && ids.Distinct().Count() == 3;
            foreach (var resource in new[] { ("health", 0), ("stamina", 1), ("eitr", 2) })
            {
                var data = observed["recovery"]!["sample"]!;
                double ticks = data["ticks"]![resource.Item2]!.GetValue<double>();
                double delta = data["after"]![resource.Item1]!.GetValue<double>()
                    - data["before"]![resource.Item1]!.GetValue<double>();
                passed &= ticks >= 1 && ticks <= 4 && Math.Abs(delta - 4.2 * ticks) <= .02;
            }
            Add(
                "current-radius-20.1-corrected-oracle",
                passed,
                new
                {
                    originalFailPreserved = kept,
                    originalSha = QaFiles.Hash(kept),
                    expected,
                    reason = "Damage B .055 + C .05 = .105; speed B .105 + C .05 = .155. Original assertion confused damage and speed; no changed game data or rerun.",
                    outside
                }
            );
            if (!passed)
                throw new IOException("Saved outside measurement did not satisfy corrected literal fixture");
        }
        public static async Task RunTail(QaLedger owner)
        {
            ledger = owner;
            tail = true;
            var timer = Stopwatch.StartNew();
            try
            {
                await QaAuraCases.Prepare(owner);
                foreach (string role in new[] { "server", "client-a", "client-b" })
                    await Command(role, "point3-trace");
                if (!audioOnly)
                {
                    var a = await Command("client-a", "snapshot");
                    var p = a["position"]!;
                    origin = (p["x"]!.GetValue<float>(), p["y"]!.GetValue<float>() + 100, p["z"]!.GetValue<float>());
                    await Command(
                        "client-b",
                        "aura-floor",
                        new
                        {
                            position = Position()
                        }
                    );
                    await Command(
                        "client-b",
                        "aura-position",
                        new
                        {
                            position = Position()
                        }
                    );
                    await Command("client-b", "freeze");
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
                                x = origin.x + 4,
                                origin.y,
                                z = origin.z + 8
                            }
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
                            position = Position()
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
                                x = origin.x + 2,
                                origin.y,
                                z = origin.z + 8
                            }
                        }
                    );
                    await Ready(.2075);
                    await Command("server", "point3-cadence");
                    await Boundary(19.9f, true);
                }
                await Command("server", "host");
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                var near = (await Command("client-a", "snapshot"))["position"]!;
                await Command(
                    "client-b",
                    "aura-position",
                    new
                    {
                        position = new
                        {
                            x = near["x"]!.GetValue<float>() + 4,
                            y = near["y"]!.GetValue<float>(),
                            z = near["z"]!.GetValue<float>()
                        }
                    }
                );
                await Command("client-a", "point3-audio-start");
                await Command("client-b", "point3-audio-start");
                await Audio("client-a");
                await Audio("client-b");
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                Add(
                    "point3-tail-complete",
                    true,
                    new
                    {
                        seconds = timer.Elapsed.TotalSeconds,
                        passedPrefixReusedWithoutRerun = true
                    }
                );
            }
            catch (Exception failure)
            {
                Add(
                    "point3-tail-failure",
                    false,
                    new
                    {
                        error = failure.ToString(),
                        seconds = timer.Elapsed.TotalSeconds
                    }
                );
                throw;
            }
            finally
            {
                tail = false;
            }
        }
        public static void PrepareFiles(QaLedger owner)
        {
            string prior = Path.Combine(owner.Root, "evidence/R01-R02/point3-current.json"),
                kept = Path.Combine(owner.Root, "evidence/R01-R02/point3-attempt1.json");
            if (File.Exists(kept))
                kept = Path.Combine(owner.Root, "evidence/R01-R02/point3-attempt2.json");
            if (File.Exists(prior) && !File.Exists(kept))
                QaFiles.Copy(owner, prior, kept);
            foreach (string role in new[] { "client-a", "client-b" })
            {
                string path = Path.Combine(owner.Root, "current/point3-private-audio", role + ".wav");
                const int frames = 48000 * 8, bytes = frames * 4;
                if (File.Exists(path) && new FileInfo(path).Length == bytes + 44)
                    continue;
                if (File.Exists(path))
                    owner.RemoveOwned(path);
                using var output = new MemoryStream(bytes + 44);
                using var wave = new BinaryWriter(output, Encoding.ASCII, true);
                wave.Write(Encoding.ASCII.GetBytes("RIFF"));
                wave.Write(bytes + 36);
                wave.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                wave.Write(16);
                wave.Write((short)1);
                wave.Write((short)2);
                wave.Write(48000);
                wave.Write(192000);
                wave.Write((short)4);
                wave.Write((short)16);
                wave.Write(Encoding.ASCII.GetBytes("data"));
                wave.Write(bytes);
                double frequency = role == "client-a" ? 375 : 750;
                for (int frame = 0; frame < frames; frame++)
                {
                    short sample = (short)Math.Round(1600 * Math.Sin(2 * Math.PI * frequency * frame / 48000));
                    wave.Write(sample);
                    wave.Write(sample);
                }
                wave.Flush();
                QaFiles.Write(owner, path, output.ToArray());
            }
        }
        public static async Task Run(QaLedger owner)
        {
            ledger = owner;
            tail = false;
            results.Clear();
            var timer = Stopwatch.StartNew();
            try
            {
                await QaAuraCases.Prepare(owner);
                foreach (string role in new[] { "server", "client-a", "client-b" })
                    await Command(role, "point3-trace");
                var a = await Command("client-a", "snapshot");
                var p = a["position"]!;
                var oldPath = await Command(
                    "client-b",
                    "point3-path",
                    new
                    {
                        position = new
                        {
                            x = p["x"]!.GetValue<float>(),
                            y = p["y"]!.GetValue<float>() + 30,
                            z = p["z"]!.GetValue<float>()
                        }
                    }
                );
                Add("old-runway-collision-observation", true, oldPath);
                origin = (p["x"]!.GetValue<float>(), p["y"]!.GetValue<float>() + 100, p["z"]!.GetValue<float>());
                await Command(
                    "client-b",
                    "aura-floor",
                    new
                    {
                        position = Position()
                    }
                );
                await QaAuraCases.Teleport("client-b", origin.x, origin.y, origin.z);
                await Command("client-b", "unfreeze");
                await Motion("control", null, 0, false, false, 0);
                await Motion("A-cassette", new[] { 2, 2, 2, 2, 1 }, 0, false, false, .05);
                await Motion("A-combined", new[] { 2, 2, 2, 2, 1 }, 1, false, false, .0525);
                await Motion("A+B", new[] { 2, 2, 2, 2, 1 }, 1, true, false, .1575);
                await Motion("A+B+C", new[] { 2, 2, 2, 2, 1 }, 1, true, true, .2075);
                await Command(
                    "client-b",
                    "aura-position",
                    new
                    {
                        position = Position()
                    }
                );
                await Command("client-b", "freeze");
                await Command("server", "point3-cadence");
                foreach (float distance in new[] { 19.9f, 20f, 20.1f })
                    await Boundary(distance, false);
                await Boundary(19.9f, true);
                await Command("server", "host");
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                var near = (await Command("client-a", "snapshot"))["position"]!;
                await Command(
                    "client-b",
                    "aura-position",
                    new
                    {
                        position = new
                        {
                            x = near["x"]!.GetValue<float>() + 4,
                            y = near["y"]!.GetValue<float>(),
                            z = near["z"]!.GetValue<float>()
                        }
                    }
                );
                await Command("client-a", "point3-audio-start");
                await Command("client-b", "point3-audio-start");
                await Audio("client-a");
                await Audio("client-b");
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                Add(
                    "point3-complete",
                    true,
                    new
                    {
                        seconds = timer.Elapsed.TotalSeconds
                    }
                );
            }
            catch (Exception error)
            {
                Add(
                    "point3-failure",
                    false,
                    new
                    {
                        error = error.ToString(),
                        seconds = timer.Elapsed.TotalSeconds
                    }
                );
                throw;
            }
        }
        private static object Position() => new { origin.x, origin.y, origin.z };
        private static Task<JsonNode> Command(
            string role,
            string action,
            object? args = null
        ) => QaAuraCases.Command(role, action, args);
        private static async Task Motion(string name, int[]? ranks, int decorations, bool b, bool c, double speed)
        {
            await Command("client-a", "clear");
            await Command("client-b", "clear");
            await Command(
                "client-b",
                "aura-position",
                new
                {
                    position = Position()
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
                            x = origin.x + 4,
                            origin.y,
                            z = origin.z + 8
                        }
                    }
                );
            if (b)
                await Command(
                    "client-b",
                    "create",
                    new
                    {
                        key = "B",
                        ranks = new[] { 1, 3, 3, 3, 2 },
                        decorations = 2,
                        placed = false,
                        position = Position()
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
                            x = origin.x + 2,
                            origin.y,
                            z = origin.z + 8
                        }
                    }
                );
            await Ready(speed);
            var motion = await Command("client-b", "aura-move");
            var sample = motion["sample"]!;
            double[] velocities = Values(sample["speeds"]!),
                targets = Values(sample["nativeTargets"]!),
                bases = Values(sample["nativeBases"]!);
            double[] times = Values(sample["sampleTimes"]!);
            bool passed = velocities.Length >= 20
                && targets.Length == velocities.Length
                && bases.Length == velocities.Length
                && times.Length == velocities.Length
                && times.All(t => t >= 2
                && t < 4)
                && bases.All(b => Math.Abs(b - 4) <= .0001)
                && sample["freshNativeCalls"]!.AsArray().All(n => n!.GetValue<bool>())
                && sample["nativeCalls"]!.GetValue<int>() > 0
                && sample["onGround"]!.GetValue<bool>();
            double targetError = 0, residual = 0;
            for (int index = 0; index < velocities.Length; index++)
            {
                targetError = Math.Max(targetError, Math.Abs(targets[index] / bases[index] - (1 + speed)));
                residual = Math.Max(residual, Math.Abs(targets[index] - velocities[index]));
            }
            passed &= double.IsFinite(targetError) && targetError <= .0001 && residual <= .010001;
            Add(
                name + "-native-speed",
                passed,
                new
                {
                    expected = 1 + speed,
                    targetError,
                    residual,
                    nativeDeadzone = .01,
                    numericEpsilon = .000001,
                    noSampleRemoval = true,
                    motion
                }
            );
            if (!passed)
                throw new IOException("Native speed case failed: " + name);
        }
        private static async Task Ready(double speed)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                var state = await Command("client-b", "snapshot");
                if (Math.Abs(state["effects"]!.AsArray().Sum(e => e!["speed"]!.GetValue<double>()) - speed) < .000001)
                    return;
                await Task.Delay(100);
            } while (DateTime.UtcNow < deadline);
            throw new IOException("Expected native source/status state did not arrive");
        }
        private static async Task Boundary(float distance, bool wall)
        {
            var receiver = await Command("client-b", "snapshot");
            var p = receiver["position"]!;
            float x = p["x"]!.GetValue<float>(), y = p["y"]!.GetValue<float>(), z = p["z"]!.GetValue<float>();
            await Command(
                "client-a",
                "moveitem",
                new
                {
                    key = "A",
                    position = new
                    {
                        x = x - distance,
                        y,
                        z
                    }
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
                            x = x - distance / 2,
                            y,
                            z
                        }
                    }
                );
            bool inside = distance <= 20;
            await Ready(inside ? .2075 : .155);
            var state = await Command("client-b", "point3-snapshot");
            double[] values = Values(state["sample"]!["values"]!);
            double[] expected = inside ? new[] { .2075, 6.3, 6.3, 6.3, .2075 } : new[] { .105, 4.2, 4.2, 4.2, .155 };
            var recovery = await Command(
                "client-b",
                "sample",
                new
                {
                    duration = .55,
                    missing = 40
                }
            );
            bool passed = values.Length == 5 && values.Zip(expected, (x, y) => Math.Abs(x - y) <= .00001).All(x => x);
            foreach (var pair in new[] { ("health", 0), ("stamina", 1), ("eitr", 2) })
            {
                double ticks = recovery["sample"]!["ticks"]![pair.Item2]!.GetValue<double>();
                double delta = recovery["sample"]!["after"]![pair.Item1]!.GetValue<double>()
                    - recovery["sample"]!["before"]![pair.Item1]!.GetValue<double>();
                passed &= ticks >= 1 && ticks <= 4 && Math.Abs(delta - expected[1] * ticks) <= .02;
            }
            var ids = state["sample"]!["ids"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
            passed &= ids.Distinct().Count() == ids.Length;
            var source = recovery["sources"]!.AsArray().Single(s => s!["ranks"]![0]!.GetValue<int>() == 2)!["position"]!;
            var position = recovery["position"]!;
            double actual3D = Math.Sqrt(
                new[] { "x", "y", "z" }.Sum(
                    axis => Math.Pow(source[axis]!.GetValue<double>() - position[axis]!.GetValue<double>(), 2)
                )
            );
            passed &= Math.Abs(actual3D - distance) <= .00002 && (distance != 20 || actual3D == 20);
            Add(
                wall ? "current-wall" : "current-radius-" + distance,
                passed,
                new
                {
                    distance,
                    actual3D,
                    wall,
                    expected,
                    observed = state,
                    recovery,
                    fixturePeriod = .25,
                    defaultFiveSecondGroupsAlreadyAccepted = true
                }
            );
            if (!passed)
                throw new IOException("Boundary/source uniqueness failed");
        }
        private static async Task Audio(string role)
        {
            JsonNode state;
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            string own = role == "client-a" ? "375" : "750", other = role == "client-a" ? "750" : "375";
            var reads = new List<JsonNode>();
            do
            {
                state = await Command(role, "point3-snapshot");
                reads.Add(state.DeepClone());
                if (state["sample"]?["playing"]?.GetValue<bool>() == true
                    && state["sample"]?["position"]?.GetValue<int>() > 4096
                    && state["sample"]?["source" + own]?.GetValue<double>() > .000001
                    && state["sample"]?["listener" + own]?.GetValue<double>() > .000001)
                    break;
                await Task.Delay(100);
            } while (DateTime.UtcNow < deadline);
            var measured = new List<JsonNode> { state.DeepClone() };
            for (int i = 0; i < 2; i++)
            {
                await Task.Delay(100);
                measured.Add(await Command(role, "point3-snapshot"));
            }
            bool passed = true;
            foreach (var measurement in measured)
            {
                var sample = measurement["sample"]!;
                passed &= sample["playing"]?.GetValue<bool>() == true
                    && sample["foreignAudioSources"]!.GetValue<int>() == 0
                    && sample["packets"]!.GetValue<long>() > 0
                    && !sample["privatePathFound"]!.GetValue<bool>();
                foreach (string bus in new[] { "source", "listener" })
                    passed &= sample[bus + own]!.GetValue<double>() > .000001
                    && sample[bus + own]!.GetValue<double>() > sample[bus + other]!.GetValue<double>() * 3;
            }
            Add(
                role + "-own-source-and-listener",
                passed,
                new
                {
                    analyzerReadinessReads = reads,
                    contiguousMeasurements = measured,
                    measurementCount = 3,
                    noMeasurementRemoval = true,
                    firstReadAllocatesAnalyzer = "Unity GetOutputData initializes per-object recording buffers; zero initialization is retained in readiness evidence"
                }
            );
            if (!passed)
                throw new IOException("Native personal audio/packet isolation failed: " + role);
        }
        private static double[] Values(JsonNode array) => array.AsArray().Select(n => n!.GetValue<double>()).ToArray();
        private static void Add(string id, bool passed, object observed)
        {
            results.Add(new
            {
                id,
                passed,
                observed,
                utc = DateTime.UtcNow
            });
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R01-R02/point3-current.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                        tail,
                        cases = results,
                        scope = "Only point3: read-only native speed/real physics, current radius/resources/source uniqueness, two actual personal Source/Listener audio outputs and RPC private-path observation. Numeric actor reposition is preparation outside measurement. Default5s recovery groups are retained; boundary fixture explicitly uses configurable .25s period and restores5s.",
                        utc = DateTime.UtcNow
                    }
                )
            );
            Console.WriteLine("QA_POINT3 " + id + " " + passed);
        }
    }
}
