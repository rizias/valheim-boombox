using System;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace BoomboxQa
{
    public static class QaJoinChecks
    {
        public static void PreserveFailure(QaLedger ledger)
        {
            string evidence = Path.Combine(ledger.Root, "evidence/R08/protocol5/incompatible-client/attempt-1");
            foreach (string role in new[] { "server", "client-b" })
            {
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "current", role, "stdout.log"),
                    Path.Combine(evidence, role + "-stdout.log")
                );
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json"),
                    Path.Combine(evidence, role + "-status.json")
                );
            }
            foreach (string file in new[] { "timing.json", "restore.json" })
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R08/protocol5/incompatible-client", file),
                    Path.Combine(evidence, file)
                );
        }
        public static void PreservePrevious(QaLedger ledger, string scenario)
        {
            if (scenario != "missing-client")
                return;
            string folder = Path.Combine(ledger.Root, "evidence/R08/protocol5", scenario),
                kept = Path.Combine(folder, "before-fix");
            if (File.Exists(Path.Combine(kept, "case.json")) || !File.Exists(Path.Combine(folder, "case.json")))
                return;
            foreach (string file in new[] { "case.json", "timing.json", "restore.json", "client-b-rejected.json" })
                QaFiles.Copy(ledger, Path.Combine(folder, file), Path.Combine(kept, file));
        }
        public static void Complete(QaLedger ledger)
        {
            var identity = QaCandidateIdentity.Current;
            string root = Path.Combine(ledger.Root, "evidence/R08/protocol5"),
                candidate = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
            var cases = new System.Collections.Generic.List<JsonElement>();
            var runs = new System.Collections.Generic.List<JsonElement>();
            foreach (string scenario in new[] {
                "matching-version",
                "missing-client",
                "incompatible-client",
                "missing-server"
            })
            {
                using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, scenario, "case.json")));
                using var timing = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, scenario, "timing.json")));
                using var restore = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, scenario, "restore.json")));
                ValidateCurrentCase(proof.RootElement, timing.RootElement, restore.RootElement, candidate, scenario,
                    identity.Version, identity.Protocol);
                cases.Add(proof.RootElement.Clone());
                runs.Add(timing.RootElement.Clone());
            }
            var history = new System.Collections.Generic.List<JsonElement>();
            foreach (string file in new[] {
                "matching-version/attempt-1-timing.json",
                "missing-client/before-fix/timing.json",
                "incompatible-client/attempt-1/timing.json"
            })
            {
                string path = Path.Combine(root, file);
                if (!File.Exists(path))
                    continue;
                using var timing = JsonDocument.Parse(File.ReadAllText(path));
                history.Add(timing.RootElement.Clone());
            }
            int gameProcesses = System.Diagnostics.Process.GetProcessesByName("valheim").Length
                + System.Diagnostics.Process.GetProcessesByName("valheim_server").Length;
            if (gameProcesses != 0 || ledger.State.Reservations.Count != 0 || ledger.State.NativeReservations.Count != 0
            || ledger.State.External.Count != 0
            || ledger.State.Phase != "ready")
                throw new IOException("R08 owned resources not quiescent");
            QaFiles.Write(
                ledger,
                Path.Combine(root, "cases.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        scope = "JOIN-01…04 product compatibility only; native TCP D+A/B, UI label reads, no rendered GUI or Steam auth acceptance",
                        candidateSha = candidate,
                        version = identity.Version,
                        protocol = identity.Protocol,
                        cases,
                        runs,
                        history,
                        timingScope = "Each native session includes preparation, game runtime and restore; individual phase timestamps were not measured. Controller compilation and candidate builds are outside session totals. Failed attempts and affected retries remain separate.",
                        sourceHashes = new
                        {
                            handshake = QaFiles.Hash(Path.Combine(ledger.Project, "src/BoomboxNetworkHandshake.cs")),
                            patches = QaFiles.Hash(Path.Combine(ledger.Project, "src/BoomboxNetworkPatches.cs"))
                        },
                        gameProcesses,
                        reservations = 0,
                        nativeReservations = 0,
                        externalLoaderFiles = 0,
                        preferences = QaRegistryData.Hash(QaRegistryData.Capture()),
                        utc = DateTime.UtcNow
                    }
                )
            );
        }
        internal static void ValidateCurrentCase(
            JsonElement proof,
            JsonElement timing,
            JsonElement restore,
            string candidate,
            string scenario,
            string requiredVersion,
            int requiredProtocol
        )
        {
            bool matching = scenario == "matching-version";
            string versionKey = matching
                ? "version"
                : "requiredVersion", protocolKey = matching
                    ? "protocol"
                    : "requiredProtocol";
            if (!proof.GetProperty("passed").GetBoolean() || proof.GetProperty("candidateSha").GetString() != candidate
            || proof.GetProperty(versionKey).GetString() != requiredVersion
            || proof.GetProperty(protocolKey).GetInt32() != requiredProtocol)
                throw new IOException("R08 current case identity/result mismatch: " + scenario);
            if (!timing.GetProperty("passed").GetBoolean()
                || timing.GetProperty("candidateSha").GetString() != candidate
                || timing.GetProperty("scenario").GetString() != (matching ? "candidate" : scenario))
                throw new IOException("R08 current timing identity/result mismatch: " + scenario);
            double seconds = timing.GetProperty("seconds").GetDouble();
            if (!double.IsFinite(seconds) || seconds < 0
            || timing.GetProperty("endedUtc").GetDateTime() < timing.GetProperty("startedUtc").GetDateTime())
                throw new IOException("R08 invalid timing: " + scenario);
            string? before = restore.GetProperty("preferencesBefore").GetString();
            if (!restore.GetProperty("sharedPreferencesRestored").GetBoolean()
            || !restore.GetProperty("ownedProcessesStopped").GetBoolean()
            || !restore.GetProperty("serverBootstrapRemoved").GetBoolean()
            || string.IsNullOrEmpty(before)
            || before != restore.GetProperty("preferencesAfter").GetString())
                throw new IOException("R08 restore incomplete: " + scenario);
        }
        public static bool Rejected(QaLedger ledger, string scenario)
        {
            var identity = QaCandidateIdentity.Current;
            if (scenario != "missing-client" && scenario != "missing-server" && scenario != "incompatible-client")
                return false;
            string role = scenario == "missing-server" ? "client-a" : "client-b";
            string path = Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json");
            if (!File.Exists(path))
                return false;
            try
            {
                using var doc = JsonDocument.Parse(QaFiles.ReadSharedText(path));
                var state = doc.RootElement;
                string connection = state.GetProperty("connection").GetString()!;
                if (connection != "ErrorKicked" || state.GetProperty("localPlayer").GetInt64() != 0
                || state.GetProperty("worldUID").GetInt64() != 0)
                    return false;
                var product = state.GetProperty("product");
                if (product.GetProperty("activeAuraSources").GetInt32() != 0
                    || product.GetProperty("decks").GetInt32() != 0)
                    return false;
                bool loaded = product.GetProperty("loaded").GetBoolean();
                if (loaded != (scenario != "missing-client"))
                    return false;
                if (loaded)
                {
                    string dll = Path.Combine(
                        QaEnvironment.CandidateRoot,
                        scenario == "incompatible-client"
                            ? "protocol-incompatible/ValheimBoombox.dll"
                            : "ValheimBoombox.dll"
                    );
                    if (product.GetProperty("sha").GetString() != QaFiles.Hash(dll)
                        || product.GetProperty("version").GetString() != identity.Version
                        || product.GetProperty("protocol").GetInt32() != (scenario == "incompatible-client"
                            ? QaCandidateIdentity.IncompatibleProtocol : identity.Protocol))
                        return false;
                }
                string reason = state.GetProperty("joinReason").GetString()!;
                string log = QaFiles.ReadSharedText(Path.Combine(
                        ledger.Root,
                        "current",
                        scenario == "missing-server"
                        ? "client-a"
                        : "server",
                        "stdout.log"
                    ));
                string exactLog = log.Split('\n').FirstOrDefault(
                    line => line.Contains("Valheim Boombox " + identity.Version, StringComparison.Ordinal)
                        && (line.Contains(
                        "required",
                        StringComparison.Ordinal
                    ) || line.Contains(
                        "Требуется",
                        StringComparison.Ordinal
                    ))
                );
                if (exactLog == null)
                    return false;
                if (!exactLog.Contains("protocol " + identity.Protocol, StringComparison.Ordinal)
                && !exactLog.Contains("протокол " + identity.Protocol, StringComparison.Ordinal))
                    return false;
                if (scenario == "incompatible-client" && !exactLog.Contains("101", StringComparison.Ordinal))
                    return false;
                if (scenario != "missing-client" && !reason.Contains("Valheim Boombox " + identity.Version, StringComparison.Ordinal))
                    return false;
                if (scenario == "incompatible-client"
                && !reason.Contains(
                    $"Valheim Boombox {identity.Version} (protocol {identity.Protocol}) is required. Received: {identity.Version} (protocol {QaCandidateIdentity.IncompatibleProtocol}).",
                    StringComparison.Ordinal
                ))
                    return false;
                if (scenario == "incompatible-client"
                && !exactLog.Contains(
                    $"Valheim Boombox {identity.Version} (protocol {identity.Protocol}) is required. Received: {identity.Version} (protocol {QaCandidateIdentity.IncompatibleProtocol}).",
                    StringComparison.Ordinal
                ))
                    return false;
                string evidence = Path.Combine(ledger.Root, "evidence/R08/protocol5", scenario);
                QaFiles.Copy(ledger, path, Path.Combine(evidence, role + "-rejected.json"));
                foreach (string actor in new[] { "server", "client-a", "client-b" })
                    QaFiles.Copy(
                        ledger,
                        Path.Combine(ledger.Root, "profiles", actor, "BepInEx/config/qa-status.json"),
                        Path.Combine(evidence, actor + "-observed.json")
                    );
                QaFiles.Write(
                    ledger,
                    Path.Combine(evidence, "case.json"),
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            id = scenario == "missing-client"
                                ? "JOIN-02"
                                : scenario == "incompatible-client"
                                    ? "JOIN-03"
                                    : "JOIN-04",
                            passed = true,
                            scenario,
                            role,
                            connection,
                            reason,
                            exactLog,
                            requiredVersion = identity.Version,
                            requiredProtocol = identity.Protocol,
                            receivedProtocol = scenario == "incompatible-client" ? QaCandidateIdentity.IncompatibleProtocol : -1,
                            proof = "Real client rejected before native world/player readiness; exact product log captured; UI label read only, no visual GUI acceptance",
                            candidateSha = QaFiles.Hash(
                            Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")
                        ),
                            utc = DateTime.UtcNow
                        }
                    )
                );
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
    }
}
