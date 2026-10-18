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
    public static class QaConfigTail
    {
        private static QaLedger ledger = null!;
        private static readonly List<object> cases = new();
        private static DateTime started;
        private static bool localOnly;
        public static void Prepare(QaLedger owner)
        {
            ledger = owner;
            cases.Clear();
            owner.BeginConfigRollback();
            string current = Path.Combine(owner.Root, "evidence/R03-R04/point4-current.json"),
                saved = Path.Combine(owner.Root, "evidence/R03-R04/point4-attempt1.json");
            if (File.Exists(current) && !File.Exists(saved))
            {
                QaFiles.Copy(owner, current, saved);
                QaFiles.Copy(
                    owner,
                    Path.Combine(owner.Root, "evidence/R09/restore.json"),
                    Path.Combine(owner.Root, "evidence/R03-R04/point4-attempt1-restore.json")
                );
            }
            localOnly = File.Exists(saved)
                && JsonNode.Parse(File.ReadAllText(saved)) is JsonNode savedProof
                && savedProof["candidateSha"]?.GetValue<string>()
                    == QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"))
                && QaProofIdentity.Matches(savedProof, QaProofIdentity.Current);
            if (!File.Exists(Path.Combine(owner.Root, "evidence/R05/focused-restore.json")))
                QaFiles.Copy(
                    owner,
                    Path.Combine(owner.Root, "evidence/R09/restore.json"),
                    Path.Combine(owner.Root, "evidence/R05/focused-restore.json")
                );
        }
        public static void Restore()
        {
            ledger.RestoreConfigRollback();
        }
        public static async Task Run(QaLedger owner, List<QaSupervised> running, string pipe)
        {
            ledger = owner;
            started = DateTime.UtcNow;
            try
            {
                await QaAuraCases.Prepare(owner);
                if (localOnly)
                {
                    await LocalOnly();
                    return;
                }
                var a = await Command("client-a", "snapshot");
                var position = a["position"]!.DeepClone();
                await Command(
                    "client-b",
                    "aura-position",
                    new
                    {
                        position = new
                        {
                            x = position["x"]!.GetValue<float>() + 4,
                            y = position["y"]!.GetValue<float>(),
                            z = position["z"]!.GetValue<float>()
                        }
                    }
                );
                await Command(
                    "client-a",
                    "create",
                    new
                    {
                        key = "A",
                        ranks = new[] { 1, 1, 1, 1, 1 },
                        decorations = 0,
                        placed = false,
                        position
                    }
                );
                var active = await Until("client-b", s => s["sample"]!["effects"]!.AsArray().Count == 5);
                await Command(
                    "client-a",
                    "toggle",
                    new
                    {
                        key = "A"
                    }
                );
                var removed = await Until(
                    "client-b",
                    s => s["sample"]!["effects"]!.AsArray().Count == 0 && s["sample"]!["hudRows"]!.AsArray().Count == 0
                );
                Add(
                    "HUD-03-stop-all-removes-native-effects-and-rows",
                    true,
                    new
                    {
                        active,
                        removed
                    }
                );
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                float[] strengths = { .5f, 2, 3, 4, .25f };
                await Command(
                    "server",
                    "aura-config-set",
                    new
                    {
                        radius = 6,
                        strength = strengths
                    }
                );
                await Command(
                    "client-b",
                    "aura-config-set",
                    new
                    {
                        radius = 2,
                        strength = new[] { 9, 9, 9, 9, 9 }
                    }
                );
                var before = await Until("client-b", s => Matches(s, 6, strengths));
                string hostHash = QaFiles.Hash(Config("server")), personalHash = QaFiles.Hash(Config("client-b"));
                var identities = new[] { Read("server"), Read("client-a"), Read("client-b") };
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
                previous = running[2];
                running[2] = Launch("client-b", pipe);
                previous.Dispose();
                await Ready(
                    "client-b",
                    s => s["localPlayer"]!.GetValue<long>() != 0 && s["connection"]!.GetValue<string>() == "Connected"
                );
                var after = await Until("client-b", s => Matches(s, 6, strengths));
                Add(
                    "CONFIG-04-native-host-restart-new-client-receives-persisted-balance",
                    hostHash == QaFiles.Hash(Config("server")),
                    new
                    {
                        hostHash,
                        afterHash = QaFiles.Hash(Config("server")),
                        before,
                        after,
                        identitiesBefore = identities,
                        serverAfter = Read("server"),
                        clientAfter = Read("client-b"),
                        restartSeconds = (DateTime.UtcNow - restart).TotalSeconds
                    }
                );
                DateTime localStarted = DateTime.UtcNow;
                var exited = await Command("client-b", "aura-config-local", null, 150);
                var local = await Until("client-b", s => Matches(s, 2, new float[] { 9, 9, 9, 9, 9 }));
                var native = Read("client-b");
                Add(
                    "CONFIG-05-exit-clears-override-personal-local-world",
                    native["isServer"]!.GetValue<bool>() && native["localPlayer"]!.GetValue<long>() != 0
                        && native["peers"]!.AsArray().Count == 0
                        && personalHash == QaFiles.Hash(Config("client-b"))
                        && exited["sample"]!["temporaryOverride"]!.GetValue<bool>() == false
                        && local["sample"]!["effects"]!.AsArray().Count == 0,
                    new
                    {
                        personalHash,
                        afterHash = QaFiles.Hash(Config("client-b")),
                        exitObservation = exited,
                        local,
                        native,
                        localWorldSeconds = (DateTime.UtcNow - localStarted).TotalSeconds
                    }
                );
                Add(
                    "MP-P04-focused-cases-complete",
                    true,
                    new
                    {
                        seconds = (DateTime.UtcNow - started).TotalSeconds,
                        historicalOnly = "CONFIG01/02/03 and HUD01/02 from earlier runs are references, not acceptance of this candidate",
                        original = JsonNode.Parse(
                        File.ReadAllText(Path.Combine(owner.Root, "evidence/R03-R04/runtime-cases.json"))
                    )
                    }
                );
            }
            catch (Exception failure)
            {
                cases.Add(new
                {
                    id = "MP-P04-first-failure",
                    passed = false,
                    error = failure.ToString(),
                    server = Read("server"),
                    b = Read("client-b")
                });
                Save();
                throw;
            }
        }
        private static async Task LocalOnly()
        {
            var saved = JsonNode.Parse(
                File.ReadAllText(Path.Combine(ledger.Root, "evidence/R03-R04/point4-attempt1.json"))
            )!;
            string candidate = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
            if (saved["candidateSha"]?.GetValue<string>() != candidate
                || !QaProofIdentity.Matches(saved, QaProofIdentity.Current))
                throw new IOException(
                    "Recorded CONFIG restart belongs to another candidate DLL or QA controller/fixture; run the native restart path"
                );
            if (saved["cases"]?[0]?["passed"]?.GetValue<bool>() != true)
                throw new IOException("Inherited CONFIG case did not pass; no summary success reused");
            cases.Add(saved["cases"]![0]!.DeepClone());
            var original = saved["cases"]![1]!.DeepClone();
            var observed = original["observed"]!;
            float[] strengths = { .5f, 2, 3, 4, .25f };
            bool persistent = Matches(observed["before"]!, 6, strengths)
                && Matches(observed["after"]!, 6, strengths)
                && observed["identitiesBefore"]![0]!["pid"]!.GetValue<int>()
                != observed["serverAfter"]!["pid"]!.GetValue<int>()
                && observed["identitiesBefore"]![2]!["pid"]!.GetValue<int>()
                != observed["clientAfter"]!["pid"]!.GetValue<int>();
            Add(
                "CONFIG-04-native-host-restart-persistence-offline-corrected",
                persistent,
                new
                {
                    originalRawAssertion = original,
                    oracle = "Saved six host values survived native D restart and reached new B without config-set/template after restart; whole-file bytes are not required invariant",
                    observed
                }
            );
            await Command(
                "server",
                "aura-config-set",
                new
                {
                    radius = 6,
                    strength = strengths
                }
            );
            await Command(
                "client-b",
                "aura-config-set",
                new
                {
                    radius = 2,
                    strength = new[] { 9, 9, 9, 9, 9 }
                }
            );
            var before = await Until("client-b", s => Matches(s, 6, strengths));
            string personalHash = QaFiles.Hash(Config("client-b"));
            DateTime localStarted = DateTime.UtcNow;
            var exited = await Command("client-b", "aura-config-local", null, 150);
            var local = await Until("client-b", s => Matches(s, 2, new float[] { 9, 9, 9, 9, 9 }));
            await Ready("client-b", s => s["isServer"]!.GetValue<bool>() && s["localPlayer"]!.GetValue<long>() != 0);
            var native = Read("client-b");
            Add(
                "CONFIG-05-exit-clears-override-personal-local-world",
                native["peers"]!.AsArray().Count == 0 && personalHash == QaFiles.Hash(Config("client-b"))
                    && !exited["sample"]!["temporaryOverride"]!.GetValue<bool>()
                    && local["sample"]!["effects"]!.AsArray().Count == 0,
                new
                {
                    personalHash,
                    afterHash = QaFiles.Hash(Config("client-b")),
                    before,
                    exitObservation = exited,
                    local,
                    native,
                    localWorldSeconds = (DateTime.UtcNow - localStarted).TotalSeconds
                }
            );
            Add(
                "MP-P04-focused-cases-complete",
                true,
                new
                {
                    seconds = (DateTime.UtcNow - started).TotalSeconds,
                    historicalOnly = "Earlier CONFIG/HUD rows are references. The same-candidate restart record is checked separately; no new restart is claimed.",
                    original = JsonNode.Parse(
                    File.ReadAllText(Path.Combine(ledger.Root, "evidence/R03-R04/runtime-cases.json"))
                )
                }
            );
        }
        private static string Config(
            string role
        ) => Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/local.valheim.boombox.cfg");
        private static Task<JsonNode> Command(
            string role,
            string op,
            object? args = null,
            int timeout = 30
        ) => QaAuraCases.Command(role, op, args, timeout);
        private static bool Matches(
            JsonNode s,
            double radius,
            float[] strength
        ) => s["sample"]?["hostRadius"]?.GetValue<double>() == radius
        && s["sample"]!["hostStrength"]!.AsArray().Select(n => n!.GetValue<float>()).SequenceEqual(strength);
        private static JsonNode Read(
            string role
        ) => JsonNode.Parse(
            QaFiles.ReadSharedText(Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json"))
        )!;
        private static void Write(
            string role,
            string command
        ) => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-command.txt"),
            Encoding.UTF8.GetBytes(command)
        );
        private static async Task<JsonNode> Until(string role, Func<JsonNode, bool> predicate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            do
            {
                var s = await QaAuraCases.Command(role, "aura-config-read", until: deadline);
                if (predicate(s))
                    return s;
                await QaAuraCases.DelayWithin(deadline, 100);
            } while (DateTime.UtcNow < deadline);
            throw new IOException("P04 native config/HUD condition not reached: " + role);
        }
        private static async Task Ready(string role, Func<JsonNode, bool> predicate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(150);
            do
            {
                try
                {
                    if (predicate(Read(role)))
                        return;
                }
                catch (IOException) { }
                catch (JsonException) { }
                await Task.Delay(250);
            } while (DateTime.UtcNow < deadline);
            throw new IOException("P04 native restart not ready: " + role);
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
            string game = QaEnvironment.RuntimeRoot(role);
            string profile = Path.Combine(ledger.Root, "profiles", role);
            var args = new List<string> {
                "-batchmode",
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
                    "-nographics",
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
            else
                args.AddRange(new[] { "-screen-fullscreen", "0", "-screen-width", "640", "-screen-height", "480" });
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
            Console.WriteLine("QA_CONFIG " + id + " " + passed);
            if (!passed)
                throw new IOException(id);
        }
        private static void Save() => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "evidence/R03-R04/point4-current.json"),
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                verifierSha = QaProofIdentity.Current,
                started,
                cases,
                utc = DateTime.UtcNow,
                scope = "Only missing CONFIG04/05 and HUD03; no GUI/video/catalog/other points"
            })
        );
    }
}
