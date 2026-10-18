using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public sealed class QaSupervised : IDisposable
    {
        public readonly Process Wrapper;
        public readonly QaJob Job;
        private readonly Task stdout = Task.CompletedTask, stderr = Task.CompletedTask;
        public Exception? Fault;
        private readonly long logMaximum;
        private readonly bool bufferedLogs;
        public QaSupervised(
            QaLedger ledger,
            string role,
            string executable,
            string workingDirectory,
            IEnumerable<string> arguments,
            long maximumLogBytes = 12 * QaLedger.MiB,
            string? pipe = null,
            bool listenHost = false,
            bool listenSession = false,
            bool bufferedLogs = false,
            bool fastUi = false
        )
        {
            logMaximum = maximumLogBytes;
            this.bufferedLogs = bufferedLogs;
            ledger.ValidateRunningBudget(2 * maximumLogBytes + 8 * QaLedger.MiB);
            var launch = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ledger.Project
            };
            foreach (string value in new[] {
                Assembly.GetExecutingAssembly().Location,
                ledger.Project,
                "child-launch",
                executable,
                workingDirectory
            })
                launch.ArgumentList.Add(value);
            foreach (string value in arguments)
                launch.ArgumentList.Add(value);
            string temp = ledger.Resolve(Path.Combine(ledger.Root, "current", role, "temp"));
            Directory.CreateDirectory(temp);
            foreach (string variable in new[] { "TEMP", "TMP", "LOCALAPPDATA", "APPDATA" })
                launch.Environment[variable] = temp;
            launch.Environment["BOOMBOX_QA_ROOT"] = ledger.Root;
            launch.Environment["BOOMBOX_QA_ROLE"] = role;
            if (QaLocalFinish.SelectedAction != null)
                launch.Environment["BOOMBOX_QA_CASE"] = QaLocalFinish.SelectedAction;
            launch.Environment["BOOMBOX_QA_SAVE_ROOT"] = Path.Combine(ledger.Root, "profiles", role, "saves");
            if (pipe != null)
                launch.Environment["BOOMBOX_QA_PIPE"] = pipe;
            if (listenHost)
                launch.Environment["BOOMBOX_QA_LISTEN_HOST"] = "1";
            if (listenSession)
                launch.Environment["BOOMBOX_QA_LISTEN_SESSION"] = "1";
            if (fastUi)
                launch.Environment["BOOMBOX_QA_FAST_UI"] = "1";
            launch.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            Job = new QaJob();
            Process? wrapper = null;
            try
            {
                Wrapper = wrapper = Process.Start(launch) ?? throw new IOException("QA wrapper start failed");
                Job.Add(Wrapper);
                string output = ledger.Resolve(Path.Combine(ledger.Root, "current", role, "stdout.log"));
                string error = ledger.Resolve(Path.Combine(ledger.Root, "current", role, "stderr.log"));
                foreach (string file in new[] { output, error })
                    if (File.Exists(file))
                        ledger.RemoveOwned(file);
                stdout = Pump(ledger, Wrapper.StandardOutput.BaseStream, output, role);
                stderr = Pump(ledger, Wrapper.StandardError.BaseStream, error, role);
                Wrapper.StandardInput.WriteLine("launch");
                Wrapper.StandardInput.Flush();
            }
            catch (Exception failure)
            {
                var cleanupErrors = new List<Exception>();
                try
                {
                    Job.Stop();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                if (wrapper != null)
                {
                    try
                    {
                        if (!wrapper.HasExited)
                            wrapper.Kill(entireProcessTree: true);
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }
                    try
                    {
                        if (!wrapper.WaitForExit(20000))
                            throw new TimeoutException("QA failed startup wrapper did not stop");
                    }
                    catch (Exception error)
                    {
                        cleanupErrors.Add(error);
                    }
                }
                try
                {
                    Job.WaitEmpty();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                try
                {
                    if (!Task.WhenAll(stdout, stderr).Wait(TimeSpan.FromSeconds(20)))
                        throw new TimeoutException("QA failed startup logs did not stop");
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                try
                {
                    Job.Dispose();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
                try
                {
                    wrapper?.Dispose();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
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
                    cleanupErrors.Insert(0, failure);
                    throw new AggregateException("QA wrapper startup and cleanup failed", cleanupErrors);
                }
                throw;
            }
        }
        private async Task Pump(QaLedger ledger, Stream input, string path, string role)
        {
            try
            {
                if (bufferedLogs)
                {
                    ledger.ReserveFile(path, logMaximum);
                    try
                    {
                        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                        byte[] block = new byte[8192];
                        int size;
                        while ((size = await input.ReadAsync(block)) > 0)
                        {
                            if (file.Length + size > logMaximum)
                                throw new IOException("QA output exceeds reserved limit");
                            await file.WriteAsync(block.AsMemory(0, size));
                        }
                        file.Flush(true);
                    }
                    finally
                    {
                        if (File.Exists(path))
                            ledger.CommitFile(path, role);
                    }
                    return;
                }
                using var output = new QaQuotaStream(ledger, path, role, logMaximum);
                byte[] buffer = new byte[8192];
                int count;
                while ((count = await input.ReadAsync(buffer)) > 0)
                    output.Write(buffer, 0, count);
            }
            catch (Exception error)
            {
                Fault = error;
                Console.Error.WriteLine("QA output failure " + role + " " + path + ": " + error);
                ledger.RecoveryRequired();
                Job.Stop();
            }
        }
        public async Task<int> WaitAsync(TimeSpan timeout)
        {
            Task completion = Wrapper.WaitForExitAsync();
            bool timedOut = await Task.WhenAny(completion, Task.Delay(timeout)) != completion;
            if (timedOut)
            {
                Job.Stop();
                if (await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(5))) != completion)
                    throw new TimeoutException("QA process did not exit after bounded stop");
                await completion;
            }
            Task output = Task.WhenAll(stdout, stderr);
            if (await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(5))) != output)
            {
                Job.Stop();
                throw new TimeoutException("QA output did not close after process exit");
            }
            await output;
            Job.WaitEmpty();
            if (timedOut)
                throw new TimeoutException("QA process watchdog");
            if (Fault != null)
                throw new IOException("QA child output failed", Fault);
            return Wrapper.ExitCode;
        }
        public void Dispose()
        {
            try
            {
                Job.Dispose();
            }
            finally
            {
                Wrapper.Dispose();
            }
        }
    }
    public static class QaChildLaunch
    {
        public static int Run(string[] arguments)
        {
            if (Console.ReadLine() != "launch")
                return 2;
            string executable = Path.GetFullPath(arguments[2]);
            string project = Path.GetFullPath(arguments[0]);
            var allowed = new[] {
                Environment.ProcessPath!,
                QaBuildCandidate.PowerShell,
                QaEnvironment.Encoder,
                QaEnvironment.MediaProbe
            };
            if (Array.IndexOf(allowed, executable) < 0 && !QaEnvironment.IsGameExecutable(executable))
                throw new IOException("QA executable outside explicit allowlist");
            if (QaEnvironment.IsGameExecutable(executable))
                QaEnvironment.ValidateGameLaunch(project, executable, arguments.Skip(4).ToArray());
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = arguments[3],
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            for (int i = 4; i < arguments.Length; i++)
                start.ArgumentList.Add(arguments[i]);
            using var child = Process.Start(start) ?? throw new IOException("QA child process missing");
            Console.WriteLine("QA_CHILD_PID=" + child.Id);
            Task stdout = child.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
            Task stderr = child.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
            child.WaitForExit();
            Task.WaitAll(stdout, stderr);
            return child.ExitCode;
        }
    }
}
