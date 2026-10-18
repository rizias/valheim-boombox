using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Principal;
using Microsoft.Win32;

namespace BoomboxQa
{
    public sealed class QaRegistry
    {
        private readonly QaRegistryData before;
        private readonly QaLedger ledger;
        private readonly string marker;
        public QaRegistry(QaLedger owner)
        {
            ledger = owner;
            marker = Path.Combine(ledger.Root, "baseline/registry-active.json");
            if (File.Exists(marker))
                throw new IOException("Unfinished registry transaction; recover before a new baseline");
            PreserveFirstFailure();
            before = QaRegistryData.Capture();
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(before);
            if (bytes.Length > QaLedger.MiB)
                throw new IOException("Preference metadata quota exceeded");
            string snapshot = Path.Combine(ledger.Root, "baseline/registry.json");
            QaFiles.Write(ledger, snapshot, bytes);
            QaFiles.Write(
                ledger,
                marker,
                JsonSerializer.SerializeToUtf8Bytes(
                    new QaRegistryTransaction
                    {
                        SnapshotSha = QaFiles.Hash(snapshot),
                        AccountSid = WindowsIdentity.GetCurrent().User!.Value
                    }
                )
            );
            ledger.RegistryOwned = true;
        }
        private QaRegistry(QaLedger owner, QaRegistryData snapshot)
        {
            ledger = owner;
            before = snapshot;
            marker = Path.Combine(ledger.Root, "baseline/registry-active.json");
            ledger.RegistryOwned = true;
        }
        private void PreserveFirstFailure()
        {
            string previous = Path.Combine(ledger.Root, "evidence/R09/restore.json");
            string failure = Path.Combine(ledger.Root, "evidence/R00/native-preferences-first-failure.json");
            if (File.Exists(previous) && !File.Exists(failure))
            {
                using var report = JsonDocument.Parse(File.ReadAllText(previous));
                if (report.RootElement.TryGetProperty("sharedPreferencesRestored", out var unchanged)
                    && !unchanged.GetBoolean())
                    QaFiles.Write(
                        ledger,
                        failure,
                        JsonSerializer.SerializeToUtf8Bytes(
                            new
                            {
                                status = "FAIL",
                                report = report.RootElement.Clone(),
                                limitation = "That run saved only a hash; its original raw registry values are unavailable. Full byte/kind snapshots are mandatory for every subsequent run."
                            }
                        )
                    );
            }
        }
        public string[] Changes() => Changed(before, QaRegistryData.Capture());
        private static string[] Changed(QaRegistryData baseline, QaRegistryData current)
        {
            return baseline.Values.Select(v => v.Name).Concat(current.Values.Select(v => v.Name)).Distinct(
                StringComparer.Ordinal
            )
            .Where(
                name => !(baseline.Find(name)?.Matches(current.Find(name)) ?? current.Find(name) == null)
            ).OrderBy(
                s => s,
                StringComparer.Ordinal
            ).ToArray();
        }
        private static bool NativeName(string name) => name.StartsWith("unity.player_", StringComparison.Ordinal)
            || name.StartsWith("unity_connect.", StringComparison.Ordinal)
            || name.StartsWith("Screenmanager ", StringComparison.Ordinal)
            || name.StartsWith("UnitySelectMonitor_", StringComparison.Ordinal)
            || name.StartsWith("UnityGraphicsQuality_", StringComparison.Ordinal);
        public void Restore()
        {
            RequireNoGame();
            QaRegistryData after = QaRegistryData.Capture();
            string[] changes = Changed(before, after);
            if (changes.Any(name => !NativeName(name)))
                throw new IOException("Non-native preference change requires an exact writer audit; baseline retained");
            if (ledger.State.Phase != "recovery-required")
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "baseline/registry-after.json"),
                    JsonSerializer.SerializeToUtf8Bytes(after)
                );
            RequireNoGame();
            using var key = Registry.CurrentUser.OpenSubKey(QaRegistryData.Subkey, true)
                ?? throw new IOException("Valheim registry key disappeared");
            foreach (string name in changes)
            {
                QaRegistryData current = QaRegistryData.Capture();
                if (!(after.Find(name)?.Matches(current.Find(name)) ?? current.Find(name) == null))
                    throw new IOException("Registry restore conflict; baseline retained");
                var original = before.Find(name);
                if (original == null)
                    key.DeleteValue(name, false);
                else
                    key.SetValue(name, original.Typed(), original.Kind);
            }
            key.Flush();
            RequireNoGame();
            if (QaRegistryData.Hash(before) != QaRegistryData.Hash(QaRegistryData.Capture()))
                throw new IOException("Typed raw preference hash not restored");
            ledger.RemoveOwned(marker);
            ledger.RegistryOwned = false;
        }
        public static void Recover(QaLedger ledger)
        {
            string marker = Path.Combine(ledger.Root, "baseline/registry-active.json");
            if (!File.Exists(marker))
                return;
            RequireNoGame();
            var transaction = JsonSerializer.Deserialize<QaRegistryTransaction>(File.ReadAllText(marker))!;
            string snapshot = Path.Combine(ledger.Root, "baseline/registry.json");
            if (transaction.AccountSid != WindowsIdentity.GetCurrent().User!.Value
                || transaction.SnapshotSha != QaFiles.Hash(snapshot))
                throw new IOException("Recovery account or preference snapshot hash mismatch");
            var data = JsonSerializer.Deserialize<QaRegistryData>(File.ReadAllText(snapshot))!;
            new QaRegistry(ledger, data).Restore();
        }
        private static void RequireNoGame()
        {
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length != 0
            || System.Diagnostics.Process.GetProcessesByName("valheim_server").Length != 0)
                throw new IOException("Valheim process active; preference restore postponed");
        }
    }
}
