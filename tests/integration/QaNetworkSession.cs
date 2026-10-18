using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace BoomboxQa
{
    public static class QaNetworkSession
    {
        public static async Task Run(QaLedger ledger, string scenario = "bootstrap")
        {
            bool boundedAura = QaAuraCompletion.IsScenario(scenario);
            if (scenario.StartsWith("aura-completion", StringComparison.Ordinal) && !boundedAura)
                throw new ArgumentException(
                    "Choose aura-completion-cassettes, aura-completion-combined, aura-completion-sources or aura-completion-settings before launching a session."
                );
            DateTime? auraReadyUtc = null;
            if (boundedAura)
            {
                string previousCycle = Path.Combine(
                    ledger.Root,
                    "evidence/R01-R02/aura-completion",
                    scenario.Substring("aura-completion-".Length) + "-cycle.json"
                );
                if (File.Exists(previousCycle))
                    QaHistory.Copy(
                        ledger,
                        previousCycle,
                        Path.Combine(
                            QaHistory.Root(ledger),
                            "aura-completion",
                            Path.GetFileNameWithoutExtension(previousCycle) + "-"
                                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")
                                + ".json"
                        )
                    );
            }
            DateTime startedUtc = DateTime.UtcNow;
            QaJoinChecks.PreservePrevious(ledger, scenario);
            if (scenario == "candidate")
            {
                string prior = Path.Combine(ledger.Root, "evidence/R08/protocol5/matching-version/timing.json"),
                    kept = Path.Combine(ledger.Root, "evidence/R08/protocol5/matching-version/attempt-1-timing.json");
                if (File.Exists(prior) && !File.Exists(kept))
                    QaFiles.Copy(ledger, prior, kept);
            }
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length != 0
            || System.Diagnostics.Process.GetProcessesByName("valheim_server").Length != 0)
                throw new IOException(
                    "Another Valheim process is active; shared preference baseline cannot be restored safely"
                );
            var registry = new QaRegistry(ledger);
            string preferencesBefore = RegistryHash();
            using var pipe = new QaPipe(ledger);
            var running = new List<QaSupervised>();
            Exception? failure = null;
            try
            {
                QaProfiles.Prepare(ledger, scenario);
                foreach (string role in new[] { "server", "client-a", "client-b" })
                {
                    string executable = role == "server" ? QaEnvironment.ServerExe : QaEnvironment.ClientExe;
                    string profile = Path.Combine(ledger.Root, "profiles", role);
                    string status = Path.Combine(profile, "BepInEx/config/qa-status.json");
                    if (File.Exists(status))
                        ledger.RemoveOwned(status);
                    string command = Path.Combine(profile, "BepInEx/config/qa-command.txt");
                    if (File.Exists(command))
                        ledger.RemoveOwned(command);
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
                    bool graphics = scenario == "config-point4"
                        || scenario.StartsWith("aura-point3", StringComparison.Ordinal)
                        || scenario.StartsWith("remote-point5", StringComparison.Ordinal)
                        ? role != "server"
                        : role == "client-a"
                        && (scenario == "visual" || scenario.StartsWith("audio", StringComparison.Ordinal)
                        || scenario.StartsWith("lifecycle", StringComparison.Ordinal)
                        || scenario == "video-probe"
                        || scenario.StartsWith("model03", StringComparison.Ordinal)
                        || scenario.StartsWith("perf-", StringComparison.Ordinal));
                    if (scenario == "lifecycle-current")
                        graphics = false;
                    if (scenario == "ui-recipe")
                        graphics = role == "client-a";
                    if (graphics)
                    {
                        args.Remove("-nographics");
                        args.AddRange(
                            new[] { "-screen-fullscreen", "0", "-screen-width", "640", "-screen-height", "480" }
                        );
                    }
                    if (scenario == "ui-recipe" && graphics)
                    {
                        args.Remove("-batchmode");
                        args[args.IndexOf("640")] = QaUiCases.Combo ? "1920" : "1280";
                        args[args.IndexOf("480")] = QaUiCases.Combo ? "1080" : "720";
                    }
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
                    running.Add(
                        new QaSupervised(
                            ledger,
                            role,
                            executable,
                            Path.GetDirectoryName(executable)!,
                            args,
                            pipe: pipe.Name
                        )
                    );
                    if (role == "server")
                    {
                        DateTime readyDeadline = DateTime.UtcNow.AddMinutes(5);
                        while (!ServerReady(ledger))
                        {
                            if (running[0].Fault != null || running[0].Wrapper.HasExited)
                                throw new IOException("QA server exited during native world load");
                            if (DateTime.UtcNow >= readyDeadline)
                                throw new TimeoutException("Native world/listener not ready before deadline");
                            await Task.Delay(1000);
                        }
                        Console.WriteLine("QA_SERVER_READY native loopback listener");
                    }
                }
                DateTime deadline = DateTime.UtcNow.AddMinutes(4);
                bool perfPrepared = false;
                while (DateTime.UtcNow < deadline)
                {
                    if (auraReadyUtc is DateTime ready && DateTime.UtcNow >= ready.AddSeconds(105))
                        throw new TimeoutException(
                            "Aura action budget exhausted; stopping owned roles within the 120-second cycle."
                        );
                    foreach (var process in running)
                        if (process.Fault != null || process.Wrapper.HasExited)
                            throw new IOException(
                                "QA role exited before native connection proof: wrapper " + process.Wrapper.Id
                                    + ", exit "
                                    + (process.Wrapper.HasExited
                                        ? process.Wrapper.ExitCode.ToString()
                                        : "running")
                                    + ", output failure "
                                    + process.Fault
                            );
                    if (QaJoinChecks.Rejected(ledger, scenario))
                        return;
                    if ((scenario.StartsWith("perf-", StringComparison.Ordinal) || boundedAura
                    || scenario.StartsWith("aura-point3", StringComparison.Ordinal)
                    || scenario == "audio-reset"
                    || scenario == "audio-music"
                    || scenario == "model03-catalog"
                    || scenario == "damage"
                    || scenario == "lifecycle-current"
                    || scenario == "ui-recipe"
                    || scenario.StartsWith("remote-point5", StringComparison.Ordinal))
                    && !perfPrepared
                    && Connected(ledger, running, false))
                    {
                        if (boundedAura)
                        {
                            auraReadyUtc = DateTime.UtcNow;
                            QaAuraCases.CommandDeadline = auraReadyUtc.Value.AddSeconds(105);
                        }
                        if (scenario == "damage"
                            || scenario == "lifecycle-current"
                            || scenario == "ui-recipe"
                            || scenario == "audio-music"
                            || scenario == "model03-catalog"
                            || scenario.StartsWith("remote-point5", StringComparison.Ordinal))
                        {
                            await QaAuraCases.Prepare(
                                ledger,
                                scenario == "lifecycle-current" && QaLifecycleCurrent.KeepTransferredB
                            );
                            var near = (await QaAuraCases.Command("client-a", "snapshot"))["position"]!;
                            await QaAuraCases.Command(
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
                        }
                        else
                            await QaPerfCases.PrepareScene(ledger);
                        perfPrepared = true;
                    }
                    if (Connected(ledger, running))
                    {
                        if (scenario == "missing-client"
                            || scenario == "missing-server"
                            || scenario == "incompatible-client")
                            throw new IOException(
                                "Expected product rejection unexpectedly reached native Connected readiness: "
                                    + scenario
                            );
                        string proof = Guid.NewGuid().ToString("N");
                        QaFiles.Write(
                            ledger,
                            Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/qa-command.txt"),
                            Encoding.UTF8.GetBytes("proof:" + proof)
                        );
                        DateTime replicationDeadline = DateTime.UtcNow.AddSeconds(20);
                        if (QaAuraCases.CommandDeadline is DateTime auraLimit && auraLimit < replicationDeadline)
                            replicationDeadline = auraLimit;
                        while (!Observed(ledger, proof))
                        {
                            if (DateTime.UtcNow >= replicationDeadline)
                                throw new TimeoutException("Client B did not observe client A native ZDO change");
                            await Task.Delay(500);
                        }
                        await QaNetworkScenario.Run(ledger, scenario, boundedAura, running, pipe.Name);
                        if (scenario != "candidate" && !scenario.StartsWith("perf-", StringComparison.Ordinal))
                        {
                            QaFiles.Write(
                                ledger,
                                Path.Combine(ledger.Root, "evidence/R09/bootstrap-result.json"),
                                JsonSerializer.SerializeToUtf8Bytes(
                                    new
                                    {
                                        passed = true,
                                        scope = scenario.StartsWith("lifecycle", StringComparison.Ordinal)
                                            ? "Real D+A/B TCP actors; focused LIFE01–04 or affected native teleport repeat; prior NET-BASE retained, two-way baseline not repeated"
                                            : scenario == "audio"
                                                ? "Real D+A/B TCP actors; A AudioDeck PCM/150 cycles; prior NET-BASE baseline retained"
                                                : scenario == "visual"
                                                    ? "Native TCP real actors and real GPU capture; image acceptance pending; prior NET-BASE baseline retained, two-way replication not repeated"
                                                    : scenario == "auras" || scenario == "damage"
                                                        ? "Native TCP actors, actual peer IDs and native proof replication; focused gameplay cases separate; prior NET-BASE baseline retained"
                                                        : "Native TCP real clients; two-way Player.TeleportTo/ZDO replication; visual and platform authentication not yet accepted",
                                        preferencesBefore,
                                        preferencesWhileRunning = RegistryHash(),
                                        utc = DateTime.UtcNow
                                    }
                                )
                            );
                            foreach (string snapshotRole in new[] { "server", "client-a", "client-b" })
                            {
                                string path = Path.Combine(
                                    ledger.Root,
                                    "profiles",
                                    snapshotRole,
                                    "BepInEx/config/qa-status.json"
                                );
                                for (int attempt = 0; ; attempt++)
                                {
                                    try
                                    {
                                        string text = QaFiles.ReadSharedText(path);
                                        using var parsed = JsonDocument.Parse(text);
                                        QaFiles.Write(
                                            ledger,
                                            Path.Combine(
                                                ledger.Root,
                                                "evidence/R09/" + snapshotRole + "-connected.json"
                                            ),
                                            Encoding.UTF8.GetBytes(text)
                                        );
                                        break;
                                    }
                                    catch (Exception error) when (attempt < 9
                                        && (error is IOException || error is JsonException))
                                    {
                                        await Task.Delay(100);
                                    }
                                }
                            }
                        }
                        if (boundedAura)
                            return;
                        for (int i = running.Count - 1; i >= 0; i--)
                        {
                            string shutdownRole = i == 0
                                ? "server"
                                : i == 1
                                    ? "client-a"
                                    : "client-b";
                            QaFiles.Write(
                                ledger,
                                Path.Combine(ledger.Root, "profiles", shutdownRole, "BepInEx/config/qa-command.txt"),
                                Encoding.UTF8.GetBytes("shutdown")
                            );
                            await running[i].WaitAsync(TimeSpan.FromSeconds(45));
                        }
                        return;
                    }
                    await Task.Delay(1000);
                }
                throw new TimeoutException("Native TCP bootstrap did not establish two ready peers");
            }
            catch (Exception error)
            {
                failure = error;
                Console.Error.WriteLine("QA session failure: " + error.Message);
                throw;
            }
            finally
            {
                QaAuraCases.CommandDeadline = null;
                var cleanupErrors = new List<Exception>();
                for (int i = boundedAura ? -1 : running.Count - 1; i >= 0; i--)
                {
                    string shutdownRole = i == 0
                        ? "server"
                        : i == 1
                            ? "client-a"
                            : "client-b";
                    try
                    {
                        if (running[i].Wrapper.HasExited)
                            continue;
                        QaFiles.Write(
                            ledger,
                            Path.Combine(ledger.Root, "profiles", shutdownRole, "BepInEx/config/qa-command.txt"),
                            Encoding.UTF8.GetBytes("shutdown")
                        );
                        await running[i].WaitAsync(TimeSpan.FromSeconds(20));
                    }
                    catch (Exception shutdownError)
                    {
                        cleanupErrors.Add(shutdownError);
                        Console.Error.WriteLine("QA bounded graceful stop: " + shutdownError.Message);
                    }
                }
                foreach (var process in running)
                    try
                    {
                        process.Job.Stop();
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }
                bool ownedProcessesStopped = ledger.State.Phase != "recovery-required";
                foreach (var process in running)
                {
                    try
                    {
                        await process.WaitAsync(StopBudget(auraReadyUtc, 20));
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }
                    try
                    {
                        process.Job.WaitEmpty(StopBudget(auraReadyUtc, 15));
                    }
                    catch (Exception error)
                    {
                        ownedProcessesStopped = false;
                        cleanupErrors.Add(error);
                    }
                    try
                    {
                        process.Dispose();
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }
                }
                bool loaderRemoved = false;
                try
                {
                    pipe.Dispose();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                try
                {
                    ledger.RestoreServerLoader();
                    loaderRemoved = true;
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                string[]? nativePreferenceChanges = null;
                try
                {
                    nativePreferenceChanges = registry.Changes();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                try
                {
                    registry.Restore();
                    string after = RegistryHash();
                    QaFiles.Write(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                        JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            sharedPreferencesRestored = preferencesBefore == after,
                            nativePreferenceChanges,
                            preferencesBefore,
                            preferencesAfter = after,
                            ownedProcessesStopped,
                            serverBootstrapRemoved = loaderRemoved
                        })
                    );
                    if (preferencesBefore != after)
                        throw new IOException("Shared player preferences changed during QA");
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                try
                {
                    foreach (string role in new[] { "server", "client-a", "client-b" })
                    {
                        string cache = ledger.Resolve(
                            Path.Combine(ledger.Root, "profiles", role, "BepInEx/cache/harmony_interop_cache.dat")
                        );
                        if (ledger.State.Reservations.ContainsKey(cache))
                            ledger.CommitFile(cache, role);
                        ledger.Reconcile(Path.Combine(ledger.Root, "profiles", role));
                    }
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                if (boundedAura)
                {
                    double? seconds = auraReadyUtc.HasValue
                        ? (DateTime.UtcNow - auraReadyUtc.Value).TotalSeconds
                        : null;
                    if (seconds > 120)
                        cleanupErrors.Add(new TimeoutException("Aura world-ready cycle exceeded 120 seconds"));
                    QaFiles.Write(
                        ledger,
                        Path.Combine(
                            ledger.Root,
                            "evidence/R01-R02/aura-completion",
                            scenario.Substring("aura-completion-".Length) + "-cycle.json"
                        ),
                        JsonSerializer.SerializeToUtf8Bytes(
                            new
                            {
                                scenario,
                                readyUtc = auraReadyUtc,
                                seconds,
                                actionLimitSeconds = 105,
                                cycleLimitSeconds = 120,
                                passed = failure == null && cleanupErrors.Count == 0 && seconds.HasValue,
                                candidateSha = QaFiles.Hash(
                                Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")
                            ),
                                utc = DateTime.UtcNow
                            }
                        )
                    );
                }
                if (cleanupErrors.Count > 0)
                {
                    try
                    {
                        ledger.RecoveryRequired();
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }
                    if (failure != null)
                        cleanupErrors.Insert(0, failure);
                    throw new AggregateException("QA cleanup incomplete", cleanupErrors);
                }
                if (scenario.StartsWith("perf-", StringComparison.Ordinal))
                {
                    QaFiles.Copy(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                        Path.Combine(ledger.Root, "evidence/R11", scenario + "-restore.json")
                    );
                    QaFiles.Write(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R11", scenario + "-timing.json"),
                        JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            scenario,
                            startedUtc,
                            endedUtc = DateTime.UtcNow,
                            seconds = (DateTime.UtcNow - startedUtc).TotalSeconds,
                            passed = failure == null,
                            failure = failure?.Message
                        })
                    );
                }
                if (scenario == "candidate" || scenario == "missing-client" || scenario == "missing-server"
                || scenario == "incompatible-client")
                {
                    string name = scenario == "candidate" ? "matching-version" : scenario;
                    string folder = Path.Combine(ledger.Root, "evidence/R08/protocol5", name);
                    QaFiles.Copy(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                        Path.Combine(folder, "restore.json")
                    );
                    QaFiles.Write(
                        ledger,
                        Path.Combine(folder, "timing.json"),
                        JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            scenario,
                            startedUtc,
                            endedUtc = DateTime.UtcNow,
                            seconds = (DateTime.UtcNow - startedUtc).TotalSeconds,
                            passed = failure == null,
                            failure = failure?.Message,
                            candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"))
                        })
                    );
                }
            }
        }
        private static TimeSpan StopBudget(DateTime? readyUtc, double normalSeconds) => readyUtc.HasValue
            ? TimeSpan.FromMilliseconds(
            Math.Max(1, (readyUtc.Value.AddSeconds(120) - DateTime.UtcNow).TotalMilliseconds)
        )
            : TimeSpan.FromSeconds(normalSeconds);
        private static bool ServerReady(QaLedger ledger)
        {
            string path = Path.Combine(ledger.Root, "profiles/server/BepInEx/config/qa-status.json");
            if (!File.Exists(path))
                return false;
            try
            {
                using var report = JsonDocument.Parse(QaFiles.ReadSharedText(path));
                var state = report.RootElement;
                return state.GetProperty("worldUID").GetInt64() != 0
                    && state.GetProperty("hostPort").GetInt32() == 24870
                    && QaListeners.OwnedLoopback(state.GetProperty("pid").GetInt32(), 24870);
            }
            catch (IOException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
        private static bool Observed(QaLedger ledger, string proof)
        {
            try
            {
                using var state = JsonDocument.Parse(
                    QaFiles.ReadSharedText(Path.Combine(ledger.Root, "profiles/client-b/BepInEx/config/qa-status.json"))
                );
                long local = state.RootElement.GetProperty("localPlayer").GetInt64();
                foreach (var player in state.RootElement.GetProperty("players").EnumerateArray())
                    if (player.GetProperty("id").GetInt64() != local
                        && player.GetProperty("proof").GetString() == proof)
                        return true;
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
        private static bool Connected(QaLedger ledger, List<QaSupervised> running, bool requireVisible = true)
        {
            string path = Path.Combine(ledger.Root, "profiles/server/BepInEx/config/qa-status.json");
            if (!File.Exists(path))
                return false;
            try
            {
                using var state = JsonDocument.Parse(QaFiles.ReadSharedText(path));
                var peers = state.RootElement.GetProperty("peers");
                long worldUID = state.RootElement.GetProperty("worldUID").GetInt64();
                if (peers.GetArrayLength() != 2)
                    return false;
                var ids = new HashSet<long>();
                var players = new HashSet<long>();
                var characters = new HashSet<string>();
                foreach (var peer in peers.EnumerateArray())
                    if (!ids.Add(peer.GetProperty("uid").GetInt64()) || peer.GetProperty("uid").GetInt64() == 0
                    || !players.Add(peer.GetProperty("playerId").GetInt64())
                    || peer.GetProperty("playerId").GetInt64() == 0
                    || !characters.Add(peer.GetProperty("characterId").GetString()!)
                    || peer.GetProperty("characterId").GetString() == "0:0")
                        return false;
                foreach (string role in new[] { "client-a", "client-b" })
                {
                    string client = Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json");
                    if (!File.Exists(client))
                        return false;
                    using var report = JsonDocument.Parse(QaFiles.ReadSharedText(client));
                    var clientState = report.RootElement;
                    using var process = System.Diagnostics.Process.GetProcessById(clientState.GetProperty("pid")
                        .GetInt32());
                    if (!running[role == "client-a"
                        ? 1
                        : 2].Job.Contains(process)
                        || process.MainModule!.FileName != clientState.GetProperty("executable").GetString()
                        || process.StartTime.ToUniversalTime()
                        != DateTime.Parse(clientState.GetProperty("startUTC").GetString()!).ToUniversalTime())
                        return false;
                    if (clientState.GetProperty("connection").GetString() != "Connected"
                    || requireVisible && clientState.GetProperty("players").GetArrayLength() < 2
                    || clientState.GetProperty("worldUID").GetInt64() != worldUID
                    || !players.Contains(clientState.GetProperty("localPlayer").GetInt64()))
                        return false;
                    foreach (var player in clientState.GetProperty("players").EnumerateArray())
                        if (!players.Contains(player.GetProperty("id").GetInt64())
                        || !characters.Contains(player.GetProperty("character").GetString()!))
                            return false;
                }
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
        private static string RegistryHash() => QaRegistryData.Hash(QaRegistryData.Capture());
    }
}
