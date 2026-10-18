using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;

namespace BoomboxQa
{
    public static class QaBuildCandidate
    {
        public static string PowerShell => QaEnvironment.PowerShell;
        public static async Task Run(QaLedger ledger, bool incompatible = false)
        {
            string stage = ledger.Resolve(QaEnvironment.CandidateRoot);
            if (incompatible)
                stage = Path.Combine(stage, "protocol-incompatible");
            Directory.CreateDirectory(stage);
            string dll = Path.Combine(stage, "ValheimBoombox.dll"), response = Path.Combine(stage, "build.rsp");
            string baseline = Path.Combine(stage, "d9-point3-baseline.dll");
            if (!incompatible && File.Exists(dll)
            && QaFiles.Hash(dll) == "D9A1CA0DD3B15C6887E5C878AB2C2B06A00EFDF68764478236841653EE5FEA50"
            && !File.Exists(baseline))
                QaFiles.Copy(ledger, dll, baseline);
            string point4Baseline = Path.Combine(stage, "f998-point4-baseline.dll");
            if (!incompatible && File.Exists(dll)
            && QaFiles.Hash(dll) == "F998DC6232E06B2691F369E37DB664E915115AD4AE578510EF9DC2DEE0C87D0D"
            && !File.Exists(point4Baseline))
                QaFiles.Copy(ledger, dll, point4Baseline);
            string point6Baseline = Path.Combine(stage, "d24-point6-baseline.dll");
            if (!incompatible && File.Exists(dll)
            && QaFiles.Hash(dll) == "D24B08CABE70931E76B5A8C38558C71A56FAA8B3723CE4368C1BF59761E80F58"
            && !File.Exists(point6Baseline))
                QaFiles.Copy(ledger, dll, point6Baseline);
            long ceiling = new[] {
                "valheimboombox_bassfx",
                "valheimboombox_v3_2",
                "valheimboombox_musicfx",
                "contact-shapes.json",
                "pendant-fit.json",
                "pendant-particles.json",
                "ramsund-dragon-head.png",
                "cassette-icon.png"
            }.Sum(name => new FileInfo(Path.Combine(ledger.Project, "assets", name)).Length) + QaLedger.MiB;
            ceiling += Directory.GetFiles(Path.Combine(ledger.Project, "assets/bonus-icons"), "*.png").Sum(
                path => new FileInfo(path).Length
            );
            if (File.Exists(dll))
                QaFiles.Copy(ledger, dll, Path.Combine(stage, "previous.dll"));
            ledger.ReserveFile(dll, ceiling);
            ledger.ReserveFile(response, QaLedger.MiB);
            string game = QaEnvironment.ClientRoot;
            var arguments = new List<string> {
                "-NoProfile",
                "-File",
                Path.Combine(ledger.Project, "build.ps1"),
                "-GameManaged",
                Path.Combine(game, "valheim_Data/Managed"),
                "-BepInExCore",
                QaEnvironment.Core,
                "-OutputDirectory",
                stage,
                "-DllOnly"
            };
            if (incompatible)
            {
                string original = File.ReadAllText(Path.Combine(ledger.Project, "src/BoomboxNetworkProtocol.cs"));
                string altered = System.Text.RegularExpressions.Regex.Replace(
                    original,
                    @"public const int Protocol = \d+;",
                    "public const int Protocol = " + QaCandidateIdentity.IncompatibleProtocol + ";"
                );
                if (altered == original)
                    throw new IOException("Expected protocol fixture anchor missing");
                string fixture = Path.Combine(ledger.Root, "current/candidate-build/BoomboxNetworkProtocol.cs");
                QaFiles.Write(ledger, fixture, Encoding.UTF8.GetBytes(altered));
                arguments.AddRange(new[] { "-ProtocolSource", fixture });
            }
            try
            {
                using var build = new QaSupervised(
                    ledger,
                    "candidate-build",
                    PowerShell,
                    ledger.Project,
                    arguments,
                    maximumLogBytes: 512 * 1024
                );
                int exit = await build.WaitAsync(TimeSpan.FromMinutes(2));
                ledger.CommitFile(response);
                ledger.CommitFile(dll);
                if (exit != 0)
                    throw new IOException("Candidate compiler failed; inspect current/candidate-build/stdout.log");
                ledger.Reconcile(stage);
                Console.WriteLine("QA_CANDIDATE_SHA=" + QaFiles.Hash(dll));
            }
            catch
            {
                ledger.RecoveryRequired();
                throw;
            }
        }
    }
}
