using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaNativeAudioCases
    {
        private static QaLedger ledger = null!;
        public static async Task BuildProbe(QaLedger owner)
        {
            string bin = QaEnvironment.BinRoot;
            var targets = new[] {
                ("QaBootstrap.dll", 256 * 1024L),
                ("QaPreloader.dll", 64 * 1024L),
                ("probe.rsp", 64 * 1024L),
                ("patcher.rsp", 64 * 1024L)
            };
            foreach (var target in targets)
                owner.ReserveFile(Path.Combine(bin, target.Item1), target.Item2);
            try
            {
                using var build = new QaSupervised(
                    owner,
                    "probe-build",
                    QaBuildCandidate.PowerShell,
                    owner.Project,
                    new[] { "-NoProfile", "-File", Path.Combine(owner.Project, "tools/qa/build-probe.ps1") },
                    maximumLogBytes: 512 * 1024
                );
                int exit = await build.WaitAsync(TimeSpan.FromSeconds(45));
                foreach (var target in targets)
                    owner.CommitFile(Path.Combine(bin, target.Item1));
                if (exit != 0)
                    throw new IOException("Native write guard compile failed");
            }
            catch
            {
                owner.RecoveryRequired();
                throw;
            }
        }
        public static async Task Run(QaLedger owner)
        {
            ledger = owner;
            string evidence = Path.Combine(ledger.Root, "evidence/R11/native-audio.json");
            if (File.Exists(evidence))
            {
                var previous = JsonNode.Parse(File.ReadAllText(evidence))!;
                previous["scope"] =
                    "Previous mixed native 150 series; same-queue/pause controls are AUDIO-03 facts and do not count as strict AUDIO-06 mutations";
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R11/native-audio-previous.json"),
                    JsonSerializer.SerializeToUtf8Bytes(previous)
                );
            }
            try
            {
                await QaAuraCases.Prepare(ledger);
                JsonNode final = await Command(
                    "audio-start",
                    new
                    {
                        count = 150
                    },
                    600
                );
                var audio = final["audio"]!;
                int mutations = audio["cases"]!.AsArray().Count(entry => entry?["mutation"]?.GetValue<bool>() == true);
                if (audio["completed"]!.GetValue<int>() != 150
                    || audio["cases"]!.AsArray().Count != 150
                    || mutations != 150)
                    throw new IOException("Native audio cycle did not execute exactly 150 actual mutations");
                foreach (var entry in audio["cases"]!.AsArray())
                    if (entry!["buffer"]!.GetValue<long>() != 1843200 || entry["ownedThreads"]!.GetValue<int>() > 3
                    || entry["clips"]!.GetValue<int>() > 1)
                        throw new IOException("Native audio resources exceeded per-stream bound");
                var clear = await Command("audio-clear", null, 30);
                await Task.Delay(2000);
                clear = await Command("audio-snapshot", null, 30);
                if (clear["audio"]!["clips"]!.GetValue<int>() != 0
                || clear["audio"]!["retiredStopped"]!.GetValue<int>() != clear["audio"]!["retired"]!.GetValue<int>())
                    throw new IOException("Native audio clear retained clips/old supervisors");
                QaFiles.Write(
                    ledger,
                    evidence,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            passed = true,
                            candidateSha = QaFiles.Hash(
                            Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")
                        ),
                            fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                            operations = 150,
                            mutations,
                            final,
                            clear,
                            scope = "Actual AudioDeck/AudioSource PCM and 150 actual replacements/seeks/EOF mutations on A with real D+A/B TCP actors. No-op/pause-only controls excluded. Device reset/12h/full AUDIO/PERF remain unverified",
                            utc = DateTime.UtcNow
                        }
                    )
                );
                Console.WriteLine(
                    "QA_NATIVE_AUDIO_150_PASS actual PCM, bounded streams and native deferred clip cleanup"
                );
            }
            catch (Exception error)
            {
                JsonNode? status = Read();
                QaFiles.Write(
                    ledger,
                    evidence,
                    JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        passed = false,
                        failure = error.ToString(),
                        status,
                        candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                        scope = "Exact native audio failure; no 150-cycle acceptance",
                        utc = DateTime.UtcNow
                    })
                );
                throw;
            }
        }
        private static JsonNode? Read()
        {
            try
            {
                return JsonNode.Parse(
                    QaFiles.ReadSharedText(
                        Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/qa-gameplay-status.json")
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
        private static async Task<JsonNode> Command(string operation, object? arguments, int seconds)
        {
            string id = Guid.NewGuid().ToString("N");
            var request = arguments == null ? new JsonObject() : JsonSerializer.SerializeToNode(arguments)!.AsObject();
            request["id"] = id;
            request["action"] = operation;
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/qa-gameplay-command.json"),
                JsonSerializer.SerializeToUtf8Bytes(request)
            );
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds), next = DateTime.UtcNow.AddSeconds(15);
            int completed = -1;
            while (DateTime.UtcNow < deadline)
            {
                JsonNode? status = Read();
                if (status?["id"]?.GetValue<string>() == id)
                {
                    if (status["commandError"] != null || status["error"] != null)
                        throw new IOException(
                            "Native audio " + operation + " failed: " + (status["commandError"] ?? status["error"])
                        );
                    if (status["busy"]?.GetValue<bool>() == false)
                        return status;
                    int count = status["audio"]?["completed"]?.GetValue<int>() ?? -1;
                    if (DateTime.UtcNow >= next && count != completed)
                    {
                        completed = count;
                        next = DateTime.UtcNow.AddSeconds(15);
                        Console.WriteLine("QA_NATIVE_AUDIO_PROGRESS " + count + "/150");
                    }
                }
                await Task.Delay(1000);
            }
            throw new TimeoutException("Native audio command deadline: " + operation);
        }
    }
}
