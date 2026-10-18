using System;
using System.IO;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaEnvironmentChecks
    {
        public static void Run(string project)
        {
            int checks = 0;
            void Check(bool value, string label)
            {
                if (!value)
                    throw new IOException(label);
                checks++;
            }
            void Refuses(Action action, string label)
            {
                bool refused = false;
                try
                {
                    action();
                }
                catch (IOException)
                {
                    refused = true;
                }
                Check(refused, label);
            }
            string client = QaEnvironment.ClientExe, core = QaEnvironment.Core;
            Check(File.Exists(client), "client exists");
            Check(File.Exists(Path.Combine(core, "BepInEx.dll")), "core exists");
            Check(QaEnvironment.IsGameExecutable(client), "configured executable allowed");
            Check(
                !QaEnvironment.IsGameExecutable(Path.Combine(Path.GetDirectoryName(client)!, "another.exe")),
                "sibling executable refused"
            );
            foreach (string role in new[] { "client-a", "client-b", "server" })
            {
                string profile = Path.GetFullPath(Path.Combine(QaEnvironment.QaRoot, "profiles", role));
                string[] args = {
                    "-savedir",
                    Path.Combine(profile, "saves"),
                    "--doorstop-target-assembly",
                    Path.Combine(profile, "BepInEx/core/BepInEx.Preloader.dll"),
                    "--doorstop-enabled",
                    "true",
                    "-logFile",
                    "-"
                };
                QaEnvironment.ValidateGameLaunch(project, client, args);
                checks++;
                string[] wrong = (string[])args.Clone();
                wrong[1] = Path.Combine(project, "personal-saves");
                Refuses(() => QaEnvironment.ValidateGameLaunch(project, client, wrong), "external saves refused");
                wrong = (string[])args.Clone();
                wrong[3] = Path.Combine(core, "BepInEx.Preloader.dll");
                Refuses(() => QaEnvironment.ValidateGameLaunch(project, client, wrong), "personal preloader refused");
            }
            Refuses(
                () => QaEnvironment.ValidateGameLaunch(project, client, Array.Empty<string>()),
                "missing isolation arguments refused"
            );
            Refuses(
                () => QaEnvironment.ValidateGameLaunch(project, client, new[] { "-savedir", "a", "-savedir", "b" }),
                "duplicate isolation arguments refused"
            );
            string? old = Environment.GetEnvironmentVariable("VALHEIM_QA_CLIENT");
            try
            {
                Environment.SetEnvironmentVariable("VALHEIM_QA_CLIENT", null);
                Refuses(
                () =>
                    {
                        _ = QaEnvironment.ClientRoot;
                    },
                "missing client has no fallback"
                );
            }
            finally
            {
                Environment.SetEnvironmentVariable("VALHEIM_QA_CLIENT", old);
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                passed = true,
                checks,
                client,
                core,
                scope = "Environment and launch-isolation checks only; no game, writes or deletion"
            }));
        }
    }
}
