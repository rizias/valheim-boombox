using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace BoomboxQa
{
    public static class QaRegistryChecks
    {
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
            string original = QaRegistryData.Hash(QaRegistryData.Capture());
            _ = new QaRegistry(ledger);
            string marker = Path.Combine(ledger.Root, "baseline/registry-active.json");
            string snapshot = Path.Combine(ledger.Root, "baseline/registry.json");
            var transaction = JsonSerializer.Deserialize<QaRegistryTransaction>(File.ReadAllText(marker))!;
            string savedHash = QaFiles.Hash(snapshot);
            ledger.RegistryOwned = false;
            bool nextBlocked = Denied(() => ledger.Preflight(0));
            bool baselineProtected = Denied(() => _ = new QaRegistry(ledger)) && savedHash == QaFiles.Hash(snapshot);
            transaction.SnapshotSha = "corrupted-owned-test-marker";
            QaFiles.Write(ledger, marker, JsonSerializer.SerializeToUtf8Bytes(transaction));
            bool integrity = Denied(() => QaRegistry.Recover(ledger)) && File.Exists(marker)
            && original == QaRegistryData.Hash(QaRegistryData.Capture());
            transaction.SnapshotSha = savedHash;
            QaFiles.Write(ledger, marker, JsonSerializer.SerializeToUtf8Bytes(transaction));
            ledger.RecoveryRequired();
            ledger.RecoverReservations();
            QaRegistry.Recover(ledger);
            bool recovered = !File.Exists(marker) && original == QaRegistryData.Hash(QaRegistryData.Capture());
            var fixtures = new[]
            {
                new QaRegistryValue {
                    Name = "integer",
                    Kind = RegistryValueKind.DWord,
                    Value = JsonSerializer.SerializeToElement(-17)
                },
                new QaRegistryValue {
                    Name = "long",
                    Kind = RegistryValueKind.QWord,
                    Value = JsonSerializer.SerializeToElement(long.MaxValue)
                },
                new QaRegistryValue {
                    Name = "text",
                    Kind = RegistryValueKind.String,
                    Value = JsonSerializer.SerializeToElement("test")
                },
                new QaRegistryValue {
                    Name = "expand",
                    Kind = RegistryValueKind.ExpandString,
                    Value = JsonSerializer.SerializeToElement("%TEMP%\\raw")
                },
                new QaRegistryValue {
                    Name = "binary",
                    Kind = RegistryValueKind.Binary,
                    Value = JsonSerializer.SerializeToElement(new byte[] { 0, 255, 37 })
                },
                new QaRegistryValue {
                    Name = "multi",
                    Kind = RegistryValueKind.MultiString,
                    Value = JsonSerializer.SerializeToElement(new[] { "one", "two" })
                }
            };
            var roundtrip = JsonSerializer.Deserialize<QaRegistryValue[]>(JsonSerializer.Serialize(fixtures))!;
            bool kinds = fixtures.Zip(roundtrip).All(pair => pair.First.Matches(pair.Second)
                && JsonSerializer.Serialize(pair.First.Typed()) == JsonSerializer.Serialize(pair.Second.Typed()));
            var first = new QaRegistryData { Existed = true, Values = new() { fixtures[2] } };
            var second = new QaRegistryData
            {
                Existed = true,
                Values = new() {
                    new QaRegistryValue {
                        Name = "text",
                        Kind = RegistryValueKind.ExpandString,
                        Value = fixtures[2].Value
                    }
                }
            };
            bool typeSensitive = QaRegistryData.Hash(first) != QaRegistryData.Hash(second);
            if (!nextBlocked || !baselineProtected || !integrity || !recovered || !kinds || !typeSensitive)
                throw new IOException("Registry transaction check failed");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R00/registry-checks.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        nextBlocked,
                        baselineProtected,
                        integrity,
                        recovered,
                        kinds,
                        typeSensitive,
                        scope = "Durable marker/typed codec recovery; no intentional common-preference mutation. Actual native mutation/restore proven separately by network run."
                    }
                )
            );
        }
    }
}
