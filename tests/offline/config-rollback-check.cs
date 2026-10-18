using System;
using System.IO;
using System.Linq;
using System.Text;
using BoomboxQa;

namespace BoomboxQa
{
    public static class QaEnvironment
    {
        public static string ServerRoot => throw new IOException("No game inputs in rollback fixture");
        public static string SourceRoot(string project)
        {
            QaRoot = project;
            return project;
        }
        public static string QaRoot = "";
        public static string CandidateRoot => Path.Combine(QaRoot, "stage");
    }
    public static class QaCleanup
    {
        public static void Delete(string path) => throw new IOException("Deletion prohibited in rollback fixture");
    }
}

public static class ConfigRollbackCheck
{
    private static readonly string[] Roles = { "server", "client-a", "client-b" };
    private static byte[] Original(
        string role
    ) => new byte[] { 0xef, 0xbb, 0xbf, 0, 255, 13, 10 }.Concat(
        Encoding.UTF8.GetBytes(role == "client-b"
            ? "personal б"
            : "same original")
    ).ToArray();
    private static string Target(
        QaLedger ledger,
        string role
    ) => Path.Combine(ledger.Root, "profiles", role, "BepInEx", "config", "local.valheim.boombox.cfg");
    private static void Assert(bool value, string label)
    {
        if (!value)
            throw new IOException("FAIL " + label);
        Console.WriteLine("PASS " + label);
    }
    private static void Denied(Action action, string label)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
            Console.WriteLine("PASS " + label);
            return;
        }
        throw new IOException("FAIL accepted " + label);
    }
    private static QaLedger Create(string project)
    {
        Directory.CreateDirectory(project);
        QaEnvironment.QaRoot = project;
        var ledger = new QaLedger(project);
        foreach (string role in Roles)
        {
            string path = Target(ledger, role);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Original(role));
            ledger.AdoptExisting(path, role);
        }
        return ledger;
    }
    public static int Main(string[] args)
    {
        try
        {
            string project = Path.GetFullPath(args[0]);
            QaEnvironment.QaRoot = project;
            if (args[1] == "prepare")
            {
                using var ledger = Create(project);
                ledger.BeginConfigRollback();
                Assert(ledger.State.PendingConfigs.Count == 3, "three durable references");
                Assert(
                    Directory.GetFiles(Path.Combine(ledger.Root, "baseline", "config-rollback")).Length == 2,
                    "identical content shares snapshot"
                );
                foreach (string role in Roles)
                    File.WriteAllBytes(Target(ledger, role), Encoding.UTF8.GetBytes("changed " + role));
                ledger.RecoveryRequired();
                Console.WriteLine("PREPARED_FOR_NEW_PROCESS");
                return 0;
            }
            if (args[1] == "recover")
            {
                using var ledger = new QaLedger(project);
                Assert(
                    ledger.State.Phase == "recovery-required" && ledger.State.PendingConfigs.Count == 3,
                    "new process loaded pending rollback"
                );
                ledger.State.Phase = "ready";
                Denied(() => ledger.Preflight(0), "new work refused pending recovery");
                Denied(
                    () => QaFiles.Write(ledger, Target(ledger, "server"), new byte[] { 1 }),
                    "ordinary writer refused recovery"
                );
                ledger.RecoveryRequired();
                ledger.RunCap = 0;
                ledger.RestoreConfigRollback();
                foreach (string role in Roles)
                    Assert(
                        File.ReadAllBytes(Target(ledger, role)).SequenceEqual(Original(role)),
                        "exact original bytes restored " + role
                    );
                Assert(ledger.State.PendingConfigs.Count == 0, "pending cleared after verified restore");
                Assert(ledger.State.Phase == "recovery-required", "restore preserves unrelated recovery state");
                ledger.RestoreConfigRollback();
                ledger.State.Phase = "ready";
                ledger.RunCap = 256 * QaLedger.MiB;
                int count = Directory.GetFiles(Path.Combine(ledger.Root, "baseline", "config-rollback")).Length;
                ledger.BeginConfigRollback();
                ledger.RestoreConfigRollback();
                Assert(
                    Directory.GetFiles(Path.Combine(ledger.Root, "baseline", "config-rollback")).Length == count,
                    "repeat reuses snapshots and restore is idempotent"
                );
                foreach (string role in Roles)
                    ledger.Reconcile(Path.Combine(ledger.Root, "profiles", role));
                ledger.Reconcile(Path.Combine(ledger.Root, "baseline"));
                return 0;
            }
            if (args[1] == "safety")
            {
                using (var ledger = Create(Path.Combine(project, "foreign")))
                {
                    ledger.State.Files[Target(ledger, "client-b")].Owner = "foreign";
                    Denied(ledger.BeginConfigRollback, "foreign-owned profile config rejected");
                    Assert(ledger.State.PendingConfigs.Count == 0, "foreign rejection creates no pending transaction");
                }
                using (var ledger = Create(Path.Combine(project, "corrupt")))
                {
                    ledger.BeginConfigRollback();
                    foreach (string role in Roles)
                        File.WriteAllText(Target(ledger, role), "changed");
                    var snapshot = ledger.State.PendingConfigs.Last();
                    File.WriteAllBytes(
                        Path.Combine(ledger.Root, "baseline", "config-rollback", snapshot.Sha256 + ".cfg"),
                        new byte[] { 9 }
                    );
                    ledger.RecoveryRequired();
                    Denied(ledger.RestoreConfigRollback, "corrupt snapshot rejected");
                    Assert(
                        Roles.All(role => File.ReadAllText(Target(ledger, role)) == "changed"),
                        "all snapshots validated before any overwrite"
                    );
                    Assert(ledger.State.PendingConfigs.Count == 3, "corrupt snapshot retains pending recovery");
                }
                using (var ledger = Create(Path.Combine(project, "unrelated")))
                {
                    ledger.BeginConfigRollback();
                    string unrelated = Path.Combine(ledger.Root, "unrelated.cfg");
                    File.WriteAllText(unrelated, "untouched");
                    ledger.State.PendingConfigs[0].Role = "../../../unrelated";
                    ledger.RecoveryRequired();
                    Denied(ledger.RestoreConfigRollback, "arbitrary target role rejected");
                    Assert(File.ReadAllText(unrelated) == "untouched", "unrelated file unchanged");
                    ledger.State.PendingConfigs[0].Role = "server";
                    ledger.State.Files[Target(ledger, "client-b")].Owner = "foreign";
                    Denied(ledger.RestoreConfigRollback, "foreign target ownership rejected during restore");
                }
                using (var ledger = Create(Path.Combine(project, "interrupted")))
                {
                    ledger.BeginConfigRollback();
                    var snapshot = ledger.State.PendingConfigs[0];
                    string partial = Target(ledger, snapshot.Role) + ".config-rollback";
                    ledger.State.Reservations[partial] = new QaReservation { Path = partial, Maximum = snapshot.Bytes };
                    File.WriteAllBytes(partial, new byte[] { 99 });
                    ledger.RecoveryRequired();
                }
                using (var ledger = new QaLedger(Path.Combine(project, "interrupted")))
                {
                    ledger.RestoreConfigRollback();
                    Assert(
                        Roles.All(role => File.ReadAllBytes(Target(ledger, role)).SequenceEqual(Original(role))),
                        "interrupted recovery partial converges after reopening ledger"
                    );
                    ledger.State.Phase = "ready";
                    foreach (string role in Roles)
                        ledger.Reconcile(Path.Combine(ledger.Root, "profiles", role));
                }
                using (var ledger = Create(Path.Combine(project, "headroom")))
                {
                    long owned = ledger.State.Files.Values.Sum(f => f.Bytes),
                        originals = Roles.Sum(role => Original(role)
                        .LongLength);
                    ledger.RuntimeCap = owned + originals;
                    Denied(ledger.BeginConfigRollback, "snapshot and restore headroom checked before transaction");
                    Assert(ledger.State.PendingConfigs.Count == 0, "quota rejection creates no pending transaction");
                }
                foreach (bool moved in new[] { false, true })
                {
                    string moveProject = Path.Combine(project, moved ? "move-after" : "move-before");
                    string source, target;
                    using (var ledger = Create(moveProject))
                    {
                        source = Target(ledger, "client-a");
                        target = source + ".moved";
                        ledger.NativeMoveReserve("client-a", "move-fixture", source, target);
                        Assert(
                            !ledger.State.Files.ContainsKey(target) && ledger.State.NativeReservations.Count == 1,
                            "move reserves without recording a nonexistent destination"
                        );
                        if (moved)
                            File.Move(source, target);
                    }
                    using (var ledger = new QaLedger(moveProject))
                    {
                        ledger.RecoverReservations();
                        Assert(
                            ledger.State.NativeReservations.Count == 0,
                            "move recovery releases durable reservation"
                        );
                        Assert(
                            ledger.State.Files.ContainsKey(source) == !moved
                                && ledger.State.Files.ContainsKey(target) == moved,
                            "move recovery follows actual source and destination existence"
                        );
                        Assert(
                            File.ReadAllBytes(moved ? target : source).SequenceEqual(Original("client-a")),
                            "move recovery preserves original bytes"
                        );
                    }
                }
                foreach (string phase in new[] { "before", "after", "truncated" })
                {
                    string replacementProject = Path.Combine(project, "replace-" + phase);
                    string target, source, backup;
                    byte[] replacement = Enumerable.Repeat((byte)41, 13).ToArray();
                    using (var ledger = Create(replacementProject))
                    {
                        target = Target(ledger, "client-a");
                        source = target + ".swap";
                        backup = target + ".bak";
                        File.WriteAllBytes(source, replacement);
                        ledger.AdoptExisting(source, "client-a");
                        File.WriteAllBytes(backup, new byte[] { 7, 8, 9 });
                        ledger.AdoptExisting(backup, "client-a");
                        ledger.NativeReplaceReserve("client-a", "replacement", source, target, backup);
                        if (phase != "before")
                            File.Replace(source, target, backup);
                        if (phase == "truncated")
                            using (var stream = new FileStream(target, FileMode.Open, FileAccess.Write))
                                stream.SetLength(1);
                    }
                    using (var ledger = new QaLedger(replacementProject))
                    {
                        if (phase == "truncated")
                        {
                            Denied(ledger.RecoverReservations, "short replacement is not accepted as completed");
                            Assert(
                                ledger.State.Phase == "recovery-required" && ledger.State.NativeReservations.Count > 0,
                                "incomplete replacement retains recovery evidence"
                            );
                            Assert(
                                new FileInfo(target).Length == 1
                                    && File.ReadAllBytes(backup).SequenceEqual(Original("client-a")),
                                "failed recovery preserves damaged target and intact backup"
                            );
                        }
                        else
                        {
                            ledger.RecoverReservations();
                            Assert(
                                ledger.State.NativeReservations.Count == 0,
                                "replacement recovery accepts intact " + phase + " state"
                            );
                            Assert(
                                File.ReadAllBytes(target).SequenceEqual(phase == "before"
                                    ? Original("client-a")
                                    : replacement),
                                "replacement recovery preserves correct " + phase + " bytes"
                            );
                        }
                    }
                }
                Console.WriteLine("NO_GAME_NO_REGISTRY_NO_REAL_PROFILES");
                return 0;
            }
            throw new IOException("Unknown fixture action");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
