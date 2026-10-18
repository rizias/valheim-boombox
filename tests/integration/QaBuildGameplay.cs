using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaBuildGameplay
    {
        public static async Task Run(QaLedger ledger)
        {
            string game = QaEnvironment.ClientRoot;
            string output = Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll");
            string response = Path.Combine(QaEnvironment.BinRoot, "gameplay.rsp");
            var arguments = new List<string> {
                "/nologo",
                "/target:library",
                "/nostdlib+",
                "/langversion:latest",
                "/optimize+",
                "/deterministic+",
                "/warn:4",
                "/warnaserror+",
                "/out:" + output
            };
            arguments.AddRange(
                Directory.GetFiles(Path.Combine(game, "valheim_Data/Managed"), "*.dll").Select(p => "/reference:" + p)
            );
            foreach (string name in new[] { "BepInEx.dll", "0Harmony.dll" })
                arguments.Add("/reference:" + Path.Combine(QaEnvironment.Core, name));
            arguments.Add("/reference:" + Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
            arguments.AddRange(
                Directory.GetFiles(Path.Combine(QaEnvironment.SourceRoot(ledger.Project), "gameplay"), "*.cs")
            );
            arguments.Add(Path.Combine(QaEnvironment.SourceRoot(ledger.Project), "QaEnvironment.cs"));
            QaFiles.Write(
                ledger,
                response,
                Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, arguments.Select(a => "\"" + a + "\"")))
            );
            ledger.ReserveFile(output, 3 * 416 * 1024);
            using var compile = new QaSupervised(
                ledger,
                "gameplay-build",
                Environment.ProcessPath!,
                ledger.Project,
                new[] { QaEnvironment.Compiler, "@" + response },
                maximumLogBytes: 512 * 1024
            );
            int exit = await compile.WaitAsync(TimeSpan.FromMinutes(1));
            if (File.Exists(output))
                ledger.CommitFile(output);
            else
                ledger.RecoverReservations();
            if (exit != 0)
                throw new IOException("Gameplay probe compile failed; inspect current/gameplay-build/stdout.log");
        }
    }
}
