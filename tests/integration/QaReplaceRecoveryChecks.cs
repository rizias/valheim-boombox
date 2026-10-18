using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaReplaceRecoveryChecks
    {
        public static void Run(QaLedger ledger)
        {
            if (Process.GetProcessesByName("valheim").Length != 0
                || Process.GetProcessesByName("valheim_server").Length != 0)
                throw new IOException("Replace recovery check requires stopped native game jobs");
            ledger.Preflight(QaLedger.MiB);
            string root = Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config");
            string target = Path.Combine(root, "qa-replace-recovery.bin"), source = target + ".tmp", backup = target + ".bak";
            var paths = new[] { source, target, backup };
            var cases = new List<object>();
            void Check(string name, bool passed, object? observation = null)
            {
                cases.Add(new
                {
                    name,
                    passed,
                    observation
                });
                if (!passed)
                    throw new IOException("Replace recovery check failed: " + name);
            }
            void Write(string path, int bytes, byte value)
            {
                string reservation = Guid.NewGuid().ToString("N");
                ledger.NativeReserve("client-a", reservation, path, bytes);
                File.WriteAllBytes(path, Enumerable.Repeat(value, bytes).ToArray());
                ledger.NativeCommit("client-a", reservation, path, bytes);
            }
            Write(source, 8, 11);
            Write(target, 64, 22);
            Write(backup, 128, 33);
            var before = paths.ToDictionary(p => p, QaFiles.Hash);
            string id = Guid.NewGuid().ToString("N");
            ledger.NativeReplaceReserve("client-a", id, source, target, backup);
            var destination = ledger.State.NativeReservations[id + "-target"];
            var retained = ledger.State.NativeReservations[id + "-backup"];
            Check(
                "Durable ceilings cover both pre-state and expected post-state",
                destination.MaximumLength == 64 && destination.ExpectedLength == 8 && retained.MaximumLength == 128
                && retained.ExpectedLength == 64
            );
            bool failed = false, sharingViolation = false;
            FileStream? failureLock = null;
            try
            {
                failureLock = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None);
                try
                {
                    File.Replace(source, target, backup);
                }
                catch (IOException error)
                {
                    failed = true;
                    sharingViolation = (error.HResult & 65535) == 32;
                    ledger.RecoveryRequired();
                }
            }
            finally
            {
                failureLock?.Dispose();
            }
            Check(
                "Real File.Replace failed before side effects under controlled lock",
                failed && sharingViolation && paths.All(p => File.Exists(p) && QaFiles.Hash(p) == before[p])
            );
            using (var quiescence = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check("Owned failure lock released before recovery", quiescence.Length == 64);
            Check(
                "Failure reservations remain durable",
                ledger.State.Phase == "recovery-required" && ledger.State.NativeReservations.ContainsKey(id + "-target")
                && ledger.State.NativeReservations.ContainsKey(id + "-backup")
            );
            ledger.RecoverReservations();
            Check(
                "Shrinking replacement recovery unblocks after owned quiescence",
                ledger.State.Phase == "ready" && ledger.State.NativeReservations.Count == 0
                && paths.All(p => QaFiles.Hash(p) == before[p])
            );
            id = Guid.NewGuid().ToString("N");
            ledger.NativeReplaceReserve("client-a", id, source, target, backup);
            File.Replace(source, target, backup);
            ledger.NativeReplaceCommit("client-a", id, source, target, backup);
            Check(
                "Strict expected sizes committed for successful shrink",
                !File.Exists(source) && new FileInfo(target).Length == 8 && new FileInfo(backup).Length == 64
                && File.ReadAllBytes(target).All(b => b == 11)
                && File.ReadAllBytes(backup).All(b => b == 22)
            );
            Write(source, 4, 44);
            string unknown = Path.Combine(root, "qa-replace-unknown.bin");
            string anotherTarget = Path.Combine(root, "qa-replace-foreign-backup.bin");
            Write(anotherTarget, 16, 66);
            string unknownBackup = anotherTarget + ".bak";
            foreach (string path in new[] { unknown, unknownBackup })
            {
                ledger.ReserveFile(path, 16);
                File.WriteAllBytes(path, Enumerable.Repeat((byte)55, 16).ToArray());
            }
            bool targetDenied = false, backupDenied = false;
            try
            {
                ledger.NativeReplaceReserve("client-a", Guid.NewGuid().ToString("N"), source, unknown, null);
            }
            catch (IOException)
            {
                targetDenied = true;
            }
            try
            {
                ledger.NativeReplaceReserve(
                    "client-a",
                    Guid.NewGuid().ToString("N"),
                    source,
                    anotherTarget,
                    unknownBackup
                );
            }
            catch (IOException)
            {
                backupDenied = true;
            }
            Check(
                "Existing unknown target and backup denied without adoption",
                targetDenied && backupDenied && !ledger.State.Files.ContainsKey(unknown)
                && !ledger.State.Files.ContainsKey(unknownBackup)
                && new FileInfo(unknown).Length == 16
                && new FileInfo(unknownBackup).Length == 16
                && ledger.State.NativeReservations.Count == 0
            );
            foreach (string path in paths.Concat(new[] { unknown, unknownBackup, anotherTarget }))
                if (File.Exists(path))
                    ledger.RemoveOwned(path);
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R11/replace-recovery.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        cases,
                        scope = "Actual Windows File.Replace with controlled sharing lock, production reservation/commit/recovery and unknown-file ownership checks; all game jobs stopped",
                        utc = DateTime.UtcNow
                    }
                )
            );
            Console.WriteLine("QA_REPLACE_RECOVERY_PASS actual failed shrink recovered after lock release");
        }
    }
}
