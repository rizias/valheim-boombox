using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BoomboxQa;

static class RestartCheck
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length > 1 && args[1] == "child-launch")
        {
            string root = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT")!;
            string role = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROLE")!;
            File.WriteAllText(Path.Combine(root, role + ".pid"), Environment.ProcessId.ToString());
            if (args.Skip(4).Contains("-batchmode"))
                args = new[] {
                    args[0],
                    args[1],
                    args[2],
                    args[3],
                    Assembly.GetExecutingAssembly().Location,
                    "worker",
                    "sleep"
                };
            return QaChildLaunch.Run(args);
        }
        if (args.Length > 0 && args[0] == "worker")
        {
            if (args.Length > 1 && args[1] == "sleep")
                Thread.Sleep(30000);
            Console.WriteLine("check child complete");
            return 0;
        }
        string rootPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "run-" + Guid.NewGuid().ToString("N")
        );
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(45));
            Environment.FailFast("Temporary check exceeded 45 seconds");
        });
        var startup = new QaLedger(Path.Combine(rootPath, "startup")) { FailOutputResolve = true };
        try
        {
            using var child = Start(startup, "startup", "sleep");
            throw new Exception("Startup failure missing");
        }
        catch (IOException error) when (error.Message == "injected output-path failure") { }
        AssertStopped(ReadPid(startup, "startup"));
        Console.WriteLine("PASS constructor failure stops acquired wrapper and job");

        var replacement = new QaLedger(Path.Combine(rootPath, "replacement"));
        var running = new List<QaSupervised>();
        try
        {
            foreach (string role in new[] { "server", "client-a", "client-b" })
                running.Add(Start(replacement, role, "exit"));
            var previous = running[1];
            replacement.FailValidateCall = replacement.ValidateCalls + 1;
            try
            {
                await QaUiCases.RunCombo(replacement, running, "unused");
                throw new Exception("Replacement failure missing");
            }
            catch (IOException error) when (error.Message == "injected launch failure") { }
            if (!ReferenceEquals(previous, running[1]) || !running[1].Wrapper.HasExited)
                throw new Exception("Failed replacement lost inspectable prior owner");
            Console.WriteLine("PASS actual UI restart keeps inspectable prior owner when replacement fails");
        }
        finally
        {
            foreach (var child in running)
                child.Job.Stop();
            foreach (var child in running)
            {
                await child.WaitAsync(TimeSpan.FromSeconds(5));
                child.Job.WaitEmpty();
                child.Dispose();
            }
        }

        var network = new QaLedger(
            Path.Combine(rootPath, "network")
        )
        {
            FailValidateCall = 3,
            FailShutdown = true,
            FailRestorationSteps = true
        };
        AggregateException? cleanupFailure = null;
        try
        {
            await QaNetworkSession.Run(network);
        }
        catch (AggregateException error)
        {
            cleanupFailure = error;
        }
        if (cleanupFailure == null)
            throw new Exception("Network cleanup failure missing");
        string errors = cleanupFailure.ToString();
        foreach (string expected in new[] {
            "injected launch failure",
            "injected shutdown failure",
            "injected pipe failure",
            "injected loader failure",
            "injected preferences observation failure"
        })
            if (!errors.Contains(expected, StringComparison.Ordinal))
                throw new Exception("Failure lost: " + expected);
        if (!network.LoaderAttempted || !network.RegistryRestored || !network.Reconciled
        || network.State.Phase != "recovery-required")
            throw new Exception("Network cleanup skipped restoration");
        foreach (string role in new[] { "server", "client-a" })
            AssertStopped(ReadPid(network, role));
        foreach (int pid in network.ChildPids)
            AssertStopped(pid);
        Console.WriteLine("PASS actual network cleanup stops both jobs and attempts later restoration after failures");
        Console.WriteLine("NO_GAME_NO_REGISTRY_NO_SAVE_MUTATION");
        return 0;
    }
    static QaSupervised Start(QaLedger ledger, string role, string mode) => new(
        ledger,
        role,
        Environment.ProcessPath!,
        ledger.Project,
        new[] { Assembly.GetExecutingAssembly().Location, "worker", mode }
    );
    internal static int ReadPid(QaLedger ledger, string role)
    {
        string path = Path.Combine(ledger.Root, role + ".pid");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
        return int.Parse(File.ReadAllText(path));
    }
    static void AssertStopped(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            if (!process.HasExited)
                throw new Exception("Owned wrapper survives: " + id);
        }
        catch (ArgumentException) { }
    }
}

namespace BoomboxQa
{
    public sealed class QaState
    {
        public string Phase = "ready";
        public Dictionary<string, object> Reservations = new();
    }
    public sealed class QaLedger
    {
        public const long MiB = 1024 * 1024;
        public readonly string Project, Root;
        public QaState State = new();
        public bool FailOutputResolve,
            FailShutdown,
            FailRestorationSteps,
            LoaderAttempted,
            RegistryRestored,
            Reconciled;
        public int ValidateCalls, FailValidateCall;
        public readonly List<int> ChildPids = new();
        public QaLedger(string root)
        {
            Root = Project = root;
            Directory.CreateDirectory(root);
        }
        public void ValidateRunningBudget(long bytes)
        {
            if (++ValidateCalls != FailValidateCall)
                return;
            if (FailShutdown)
                foreach (string role in new[] { "server", "client-a" })
                {
                    string path = Path.Combine(Root, "current", role, "stdout.log");
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    string? child = null;
                    while (DateTime.UtcNow < deadline)
                    {
                        if (File.Exists(path))
                        {
                            using var stream = new FileStream(
                                path,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.ReadWrite
                            );
                            using var reader = new StreamReader(stream);
                            child = reader.ReadToEnd().Split('\n').FirstOrDefault(
                                line => line.StartsWith("QA_CHILD_PID=", StringComparison.Ordinal)
                            );
                        }
                        if (child != null)
                            break;
                        Thread.Sleep(10);
                    }
                    if (child == null)
                        throw new Exception("Live child not observed for " + role);
                    ChildPids.Add(int.Parse(child.Split('=')[1]));
                }
            throw new IOException("injected launch failure");
        }
        public string Resolve(string path)
        {
            if (FailOutputResolve && path.EndsWith("stdout.log", StringComparison.Ordinal))
            {
                RestartCheck.ReadPid(this, "startup");
                throw new IOException("injected output-path failure");
            }
            if (FailShutdown
                && path.EndsWith(Path.Combine("current", "server", "stdout.log"), StringComparison.Ordinal))
            {
                string status = Path.Combine(Root, "profiles/server/BepInEx/config/qa-status.json");
                Directory.CreateDirectory(Path.GetDirectoryName(status)!);
                File.WriteAllText(status, "{\"worldUID\":1,\"hostPort\":24870,\"pid\":1}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }
        public void RemoveOwned(string path) => File.Delete(path);
        public void ReserveFile(string path, long bytes)
        {
        }
        public void CommitFile(string path, string role = "")
        {
        }
        public void AdoptExisting(string path, string role)
        {
        }
        public void RestoreServerLoader()
        {
            LoaderAttempted = true;
            if (FailRestorationSteps)
                throw new IOException("injected loader failure");
        }
        public void RecoveryRequired() => State.Phase = "recovery-required";
        public void Reconcile(string path) => Reconciled = true;
    }
    public sealed class QaQuotaStream : FileStream
    {
        public QaQuotaStream(
            QaLedger ledger,
            string path,
            string role,
            long maximum
        ) : base(ledger.Resolve(path), FileMode.CreateNew, FileAccess.Write, FileShare.Read) { }
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            Flush();
        }
    }
    public static class QaLocalFinish
    {
        public static string? SelectedAction;
    }
    public static class QaBuildCandidate
    {
        public static string PowerShell = "unused";
    }
    public static class QaHistory
    {
        public static string Root(QaLedger ledger) => Path.Combine(ledger.Root, "history");
        public static void Copy(
            QaLedger ledger,
            string source,
            string target
        ) => throw new IOException("Unexpected history mutation in restart fixture");
    }
    public static class QaEnvironment
    {
        public static string ClientRoot = "unused";
        public static string CandidateRoot => "unused";
        public static string Encoder => Environment.ProcessPath!;
        public static string MediaProbe => Environment.ProcessPath!;
        public static string ServerExe => Environment.ProcessPath!;
        public static string ClientExe => Environment.ProcessPath!;
        public static bool IsGameExecutable(string path) => false;
        public static void ValidateGameLaunch(
            string project,
            string executable,
            string[] args
        ) => throw new Exception("Game launch forbidden");
    }
    public static class QaFiles
    {
        public static void Write(QaLedger ledger, string path, byte[] bytes)
        {
            if (ledger.FailShutdown && path.EndsWith("qa-command.txt", StringComparison.Ordinal))
                throw new IOException("injected shutdown failure");
            ledger.Resolve(path);
            File.WriteAllBytes(path, bytes);
        }
        public static void Copy(QaLedger ledger, string from, string to)
        {
        }
        public static string Hash(string path) => "unused";
        public static string ReadSharedText(string path) => File.ReadAllText(path);
    }
    public sealed class QaRegistry
    {
        readonly QaLedger ledger;
        public QaRegistry(QaLedger value) => ledger = value;
        public string[] Changes()
        {
            if (ledger.FailRestorationSteps)
                throw new IOException("injected preferences observation failure");
            return Array.Empty<string>();
        }
        public void Restore() => ledger.RegistryRestored = true;
    }
    public static class QaRegistryData
    {
        public static object Capture() => new();
        public static string Hash(object value) => "same";
    }
    public sealed class QaPipe : IDisposable
    {
        readonly QaLedger ledger;
        bool disposed;
        public string Name = "unused";
        public QaPipe(QaLedger value) => ledger = value;
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (ledger.FailRestorationSteps)
                throw new IOException("injected pipe failure");
        }
    }
    public static class QaProfiles
    {
        public static void Prepare(QaLedger ledger, string scenario)
        {
            string path = Path.Combine(ledger.Root, "profiles/server/BepInEx/config/qa-status.json");
            ledger.Resolve(path);
            File.WriteAllText(path, "{\"worldUID\":1,\"hostPort\":24870,\"pid\":1}");
        }
    }
    public static class QaListeners
    {
        public static bool OwnedLoopback(int pid, int port) => true;
    }
    public static class QaJoinChecks
    {
        public static void PreservePrevious(QaLedger ledger, string scenario)
        {
        }
        public static bool Rejected(QaLedger ledger, string scenario) => false;
    }
    public static class QaAuraCases
    {
        public static DateTime? CommandDeadline;
        public static Task Prepare(QaLedger ledger, bool transferred = false) => Task.CompletedTask;
        public static Task Run(QaLedger ledger) => Task.CompletedTask;
        public static Task<JsonNode> Command(
            string role,
            string op,
            object? args = null,
            int seconds = 30
        ) => Task.FromResult(JsonNode.Parse("{\"sample\":{\"frames\":[]}}")!);
    }
    public static partial class QaUiCases
    {
        public static bool Combo;
        public static Task Run(QaLedger ledger) => Task.CompletedTask;
    }
    public static class QaPerfCases
    {
        public static Task PrepareScene(QaLedger ledger) => Task.CompletedTask;
        public static Task Run(QaLedger ledger, List<QaSupervised> running, string scenario) => Task.CompletedTask;
    }
    public static class QaAuraCompletion
    {
        public static bool IsScenario(string scenario) => false;
        public static Task Run(QaLedger ledger, string scenario) => Task.CompletedTask;
    }
    public static class QaLifecycleCurrent
    {
        public static bool KeepTransferredB;
    }
    public static class QaNetworkScenario
    {
        public static Task Run(QaLedger ledger, string scenario, bool boundedAura,
            List<QaSupervised> running, string pipeName)
            => throw new InvalidOperationException("Restart fixture must fail before scenario execution");
    }
}
