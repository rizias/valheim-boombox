using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace BoomboxQa
{
    public static class QaStorageChecks
    {
        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true
        )]
        private static extern bool CreateHardLink(
            string newName,
            string existing,
            IntPtr attributes
        );
        private static readonly List<object> results = new();
        private static void Check(string id, bool passed)
        {
            results.Add(new
            {
                id,
                passed
            });
            if (!passed)
                throw new IOException("Storage check failed: " + id);
        }
        private static bool Denied(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }
        public static void Run(QaLedger ledger)
        {
            ledger.Preflight(8 * QaLedger.MiB);
            string root = ledger.Resolve(Path.Combine(ledger.Root, "current", "storage-checks"));
            Directory.CreateDirectory(root);
            Check(
                "DISK-02-outside-root",
                Denied(() => ledger.Resolve(Path.Combine(ledger.Project, "src", "escape.dat")))
            );
            Check(
                "DISK-02-appdata-escape",
                Denied(
                    () => ledger.Resolve(
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "boombox-qa-escape.dat"
                        )
                    )
                )
            );
            long oldCap = ledger.RunCap;
            ledger.RunCap = 0;
            string denied = Path.Combine(root, "denied.dat");
            Check(
                "DISK-02-denied-before-create",
                Denied(() => ledger.ReserveFile(denied, 1))
                    && !File.Exists(denied)
                    && ledger.State.Reservations.Count == 0
            );
            ledger.RunCap = oldCap;
            string repeated = Path.Combine(root, "repeat.dat");
            for (int i = 0; i < 10; i++)
                QaFiles.Write(ledger, repeated, Encoding.UTF8.GetBytes("same owned result"));
            Check("DISK-03-repeat-one-path", Directory.GetFiles(root, "repeat*").Length == 1);
            string linked = Path.Combine(root, "owned-hardlink.dat");
            ledger.ReserveFile(linked, 0);
            if (!CreateHardLink(linked, repeated, IntPtr.Zero))
                throw new IOException("Hardlink fixture creation failed");
            Check("DISK-02-hardlink-write-denied", Denied(() => ledger.Resolve(linked)));
            ledger.RemoveOwned(linked);
            Check("DISK-05-owned-hardlink-unlinked", File.Exists(repeated) && !File.Exists(linked));
            string bounded = Path.Combine(root, "stream.log");
            if (File.Exists(bounded))
                ledger.RemoveOwned(bounded);
            using (var output = new QaQuotaStream(ledger, bounded, "storage-test", 1024))
            {
                output.Write(new byte[1024], 0, 1024);
                Check("DISK-06-exact-stream-boundary", output.Length == 1024);
                Check(
                    "DISK-06-boundary-plus-one",
                    Denied(() => output.Write(new byte[1], 0, 1)) && output.Length == 1024
                );
            }
            string partial = Path.Combine(root, "interrupted.partial");
            ledger.ReserveFile(partial, 100);
            File.WriteAllBytes(partial, new byte[10]);
            ledger.RecoveryRequired();
            Check("DISK-04-recovery-blocks-next", Denied(() => ledger.Preflight(0)));
            ledger.RecoverReservations();
            Check(
                "DISK-04-owned-partial-recovered",
                !File.Exists(partial) && ledger.State.Phase == "ready" && ledger.State.Reservations.Count == 0
            );
            long oldReserve = ledger.Reserve;
            ledger.Reserve = new DriveInfo(Path.GetPathRoot(ledger.Project)!).AvailableFreeSpace + 1;
            Check("DISK-01-restore-headroom-gate", Denied(() => ledger.Preflight(0)));
            ledger.Reserve = oldReserve;
            string unknown = Path.Combine(root, "not-runner-owned.txt");
            File.WriteAllText(unknown, "fixture representing a foreign file");
            Check("DISK-05-unowned-cleanup-denied", Denied(() => ledger.RemoveOwned(unknown)) && File.Exists(unknown));
            ledger.AdoptExisting(unknown, "storage-test-fixture");
            ledger.RemoveOwned(unknown);
            ledger.Reconcile(root);
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence", "R00", "storage-checks.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        scope = "Controller file/byte/recovery cases; process/native-game enforcement pending",
                        results
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
    }
}
