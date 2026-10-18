using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaProcessChecks
    {
        public static int Worker(string action, string project)
        {
            if (action == "test-worker-echo")
            {
                Console.WriteLine("owned worker stdout");
                Console.Error.WriteLine("owned worker stderr");
                return 0;
            }
            if (action == "test-worker-storm")
            {
                string line = new('x', 4096);
                for (int i = 0; i < 100000; i++)
                    Console.WriteLine(line);
                return 0;
            }
            if (action == "test-worker-parent")
            {
                var info = new ProcessStartInfo(
                    Environment.ProcessPath!
                )
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (string value in new[] { Assembly.GetExecutingAssembly()
                    .Location, project, "test-worker-sleep" })
                    info.ArgumentList.Add(value);
                using var child = Process.Start(info)!;
                Console.WriteLine("QA_GRANDCHILD_PID=" + child.Id);
                Console.Out.Flush();
                child.WaitForExit();
                return 0;
            }
            if (action == "test-worker-sleep")
            {
                Thread.Sleep(30000);
                return 0;
            }
            throw new ArgumentException("Unknown QA worker");
        }
        private static string[] WorkerArguments(
            QaLedger ledger,
            string action
        ) => new[] { Assembly.GetExecutingAssembly().Location, ledger.Project, action };
        public static async Task Run(QaLedger ledger)
        {
            using (var echo = new QaSupervised(
                ledger,
                "process-echo",
                Environment.ProcessPath!,
                ledger.Project,
                WorkerArguments(ledger, "test-worker-echo")
            ))
                if (await echo.WaitAsync(TimeSpan.FromSeconds(10)) != 0 || !echo.Job.Contains(echo.Wrapper))
                    throw new IOException("Owned worker supervision failed");
            bool watchdog = false;
            using (var parent = new QaSupervised(
                ledger,
                "process-tree",
                Environment.ProcessPath!,
                ledger.Project,
                WorkerArguments(ledger, "test-worker-parent")
            ))
            {
                try
                {
                    await parent.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (TimeoutException)
                {
                    watchdog = true;
                }
            }
            string trace = File.ReadAllText(Path.Combine(ledger.Root, "current", "process-tree", "stdout.log"));
            string line = trace.Split('\n').First(x => x.StartsWith("QA_GRANDCHILD_PID=", StringComparison.Ordinal));
            int id = int.Parse(line.Split('=')[1].Trim());
            bool orphan;
            try
            {
                using var process = Process.GetProcessById(id);
                orphan = !process.HasExited;
            }
            catch (ArgumentException)
            {
                orphan = false;
            }
            if (!watchdog || orphan)
                throw new IOException("Owned process descendant survived watchdog");
            bool quota = false;
            using (var storm = new QaSupervised(
                ledger,
                "process-storm",
                Environment.ProcessPath!,
                ledger.Project,
                WorkerArguments(ledger, "test-worker-storm"),
                128 * 1024
            ))
            {
                try
                {
                    await storm.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (IOException)
                {
                    quota = true;
                }
            }
            long bytes = new FileInfo(Path.Combine(ledger.Root, "current", "process-storm", "stdout.log")).Length;
            if (!quota || bytes > 128 * 1024 || ledger.State.Phase != "recovery-required")
                throw new IOException("Child log storm bypassed quota");
            ledger.RecoverReservations();
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence", "R00", "process-checks.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        ownedJobConfirmed = true,
                        watchdog,
                        orphan,
                        quota,
                        bytes,
                        scope = "Owned Windows job descendants and pipe-log bounds; actual game/native writes pending"
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
    }
}
