using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaUiIntegrations
    {
        private static readonly List<string> installed = new();
        public static void Install(QaLedger ledger, bool slotsOnly = false)
        {
            string sourceRoot = Environment.GetEnvironmentVariable("VALHEIM_QA_INTEGRATIONS")
                ?? throw new IOException("Set VALHEIM_QA_INTEGRATIONS to an existing read-only plugins source");
            var receipts = new List<object>();
            foreach (string relative in new[] {
                "shudnal-ExtraSlots/ExtraSlots.dll",
                "shudnal-HipLantern/HipLantern.dll",
                "Azumatt-AAA_Crafting/AzuAntiArthriticCrafting.dll",
                "shudnal-ConditionalConfigSync/ConditionalConfigSync.dll",
                "shudnal-ConditionalConfigSync/ConditionalConfigSync.Plugin.dll",
                "ValheimModding-YamlDotNet/YamlDotNet.dll",
                "ValheimModding-YamlDotNet/YamlDotNetDetector.dll"
            })
            {
                if (slotsOnly && relative.StartsWith("Azumatt-", StringComparison.Ordinal))
                    continue;
                string source = Path.GetFullPath(Path.Combine(sourceRoot, relative)),
                    target = Path.GetFullPath(Path.Combine(ledger.Root, "profiles/client-a/BepInEx/plugins", relative));
                if (!File.Exists(source))
                    throw new IOException("Existing integration DLL missing: " + relative);
                if (File.Exists(target))
                {
                    if (QaFiles.Hash(source) != QaFiles.Hash(target))
                        throw new IOException("Integration target differs; preserve before replacing: " + target);
                }
                else
                {
                    QaFiles.Copy(ledger, source, target);
                    installed.Add(target);
                }
                receipts.Add(new
                {
                    relative,
                    sha = QaFiles.Hash(target),
                    bytes = new FileInfo(target).Length,
                    sourceReadOnly = true
                });
            }
            string receipt = Path.Combine(ledger.Root, "evidence/R10/ui-integrations.json");
            if (File.Exists(receipt))
                QaHistory.Copy(
                    ledger,
                    receipt,
                    Path.Combine(
                        QaHistory.Root(ledger),
                        "ui-integrations-" + QaFiles.Hash(receipt).Substring(0, 12) + ".json"
                    )
                );
            QaFiles.Write(ledger, receipt, JsonSerializer.SerializeToUtf8Bytes(receipts));
        }
        public static void Restore(QaLedger ledger)
        {
            foreach (string target in installed)
                if (File.Exists(target))
                    ledger.RemoveOwned(target);
            installed.Clear();
        }
        public static void AccountGenerated(QaLedger ledger)
        {
            string receipt = Path.Combine(ledger.Root, "evidence/R10/ui-integrations.json"),
                restore = Path.Combine(ledger.Root, "evidence/R09/restore.json");
            if (!File.Exists(receipt) || !File.Exists(restore))
                return;
            DateTime began = File.GetLastWriteTimeUtc(receipt), ended = File.GetLastWriteTimeUtc(restore).AddSeconds(5);
            foreach (string relative in new[] {
                "Azumatt.AzuAntiArthriticCrafting.cfg",
                "shudnal.ConditionalConfigSync/ConditionalConfigSync.Debug.cfg"
            })
            {
                string path = ledger.Resolve(Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config", relative));
                if (!File.Exists(path) || ledger.State.Files.ContainsKey(path))
                    continue;
                var file = new FileInfo(path);
                if (file.Length > 16 * 1024 || file.CreationTimeUtc < began || file.LastWriteTimeUtc > ended)
                    throw new IOException("Integration generated-config provenance does not match the owned run: "
                        + path);
                ledger.AdoptExisting(path, "client-a");
            }
        }
    }
}
