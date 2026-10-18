using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BoomboxQa
{
    public static class QaProfiles
    {
        public static void Prepare(QaLedger ledger, string scenario = "bootstrap")
        {
            string inputCore = QaEnvironment.Core;
            if (!File.Exists(Path.Combine(QaEnvironment.ClientRoot, "winhttp.dll")))
                throw new IOException(
                    "Configured client has no Doorstop loader; profile preparation refused before changes."
                );
            if (scenario != "listen" && scenario != "local-finish")
            {
                _ = QaEnvironment.ServerRoot;
            }
            ledger.Preflight(16 * QaLedger.MiB);
            string game = QaEnvironment.ClientRoot;
            foreach (string role in scenario == "local-finish"
                ? new[] { "client-a" }
                : scenario == "listen"
                    ? new[] { "client-a", "client-b" }
                    : new[] { "server", "client-a", "client-b" })
            {
                string profile = Path.Combine(ledger.Root, "profiles", role);
                foreach (string directory in new[] {
                    "BepInEx/core",
                    "BepInEx/config",
                    "BepInEx/plugins/QaBootstrap",
                    "BepInEx/plugins/ValheimBoombox",
                    "BepInEx/patchers",
                    "saves"
                })
                    Directory.CreateDirectory(Path.Combine(profile, directory));
                foreach (string source in Directory.GetFiles(QaEnvironment.Core, "*.dll"))
                    CopyOnce(ledger, source, Path.Combine(profile, "BepInEx", "core", Path.GetFileName(source)));
                CopyOnce(
                    ledger,
                    Path.Combine(QaEnvironment.BinRoot, "QaBootstrap.dll"),
                    Path.Combine(profile, "BepInEx", "plugins", "QaBootstrap", "QaBootstrap.dll")
                );
                CopyOnce(
                    ledger,
                    Path.Combine(QaEnvironment.BinRoot, "QaPreloader.dll"),
                    Path.Combine(profile, "BepInEx", "patchers", "QaPreloader.dll")
                );
                string product = Path.Combine(profile, "BepInEx/plugins/ValheimBoombox/ValheimBoombox.dll");
                bool include = scenario != "bootstrap" && !(scenario == "missing-client" && role == "client-b")
                && !(scenario == "missing-server" && role == "server");
                if (include)
                    CopyOnce(
                        ledger,
                        Path.Combine(
                            QaEnvironment.CandidateRoot,
                            scenario == "incompatible-client" && role == "client-b"
                                ? "protocol-incompatible/ValheimBoombox.dll"
                                : "ValheimBoombox.dll"
                        ),
                        product
                    );
                else if (File.Exists(product))
                    ledger.RemoveOwned(product);
                string gameplay = Path.Combine(profile, "BepInEx/plugins/QaGameplay/QaGameplay.dll");
                if (scenario == "ui-recipe" || scenario == "local-finish")
                    CopyOnce(ledger, Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll"), gameplay);
                if (scenario == "config-point4" || scenario == "auras" || QaAuraCompletion.IsScenario(scenario)
                || scenario.StartsWith("aura-point3", StringComparison.Ordinal)
                || scenario == "damage"
                || scenario == "visual"
                || scenario.StartsWith("remote-point5", StringComparison.Ordinal)
                || scenario == "video-probe"
                || scenario.StartsWith("model03", StringComparison.Ordinal)
                || scenario.StartsWith("audio", StringComparison.Ordinal)
                || scenario == "listen"
                || scenario.StartsWith("lifecycle", StringComparison.Ordinal)
                || scenario.StartsWith("perf-", StringComparison.Ordinal))
                    CopyOnce(ledger, Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll"), gameplay);
                else if (scenario != "ui-recipe" && scenario != "local-finish" && File.Exists(gameplay))
                    ledger.RemoveOwned(gameplay);
                foreach (string name in new[] { "qa-gameplay-command.json", "qa-gameplay-status.json" })
                {
                    string transient = Path.Combine(profile, "BepInEx/config", name);
                    if (File.Exists(transient))
                        ledger.RemoveOwned(transient);
                }
                string config =
                    "[Caching]\nEnableAssemblyCache = false\n[Logging.Console]\nEnabled = false\n[Logging.Disk]\nEnabled = false\n[Preloader]\nDumpAssemblies = false\n[Preloader.Entrypoint]\nAssembly = UnityEngine.CoreModule.dll\nForceEntryType = true\nType = GameObject\nMethod = .cctor\n";
                string target = Path.Combine(profile, "BepInEx", "config", "BepInEx.cfg");
                if (!File.Exists(target) || File.ReadAllText(target) != config)
                    QaFiles.Write(ledger, target, Encoding.UTF8.GetBytes(config));
                string cache = Path.Combine(profile, "BepInEx/cache/harmony_interop_cache.dat");
                ledger.ReserveFile(cache, 64 * 1024);
            }
            if (scenario == "listen" || scenario == "local-finish")
                return;
            string server = QaEnvironment.ServerRoot;
            string loader = Path.Combine(server, "winhttp.dll"), settings = Path.Combine(server, "doorstop_config.ini");
            ledger.AllowServerLoader(loader);
            ledger.AllowServerLoader(settings);
            CopyOnce(ledger, Path.Combine(game, "winhttp.dll"), loader);
            if (!File.Exists(settings))
                QaFiles.Write(
                    ledger,
                    settings,
                    Encoding.UTF8.GetBytes("[General]\nenabled = true\nredirect_output_log = false\n")
                );
        }
        private static void CopyOnce(QaLedger ledger, string source, string target)
        {
            if (File.Exists(target) && !ledger.State.Files.ContainsKey(ledger.Resolve(target)))
                throw new IOException("Unowned existing QA profile file");
            if (File.Exists(target)
                && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))
                == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))))
                return;
            QaFiles.Copy(ledger, source, target);
        }
    }
}
