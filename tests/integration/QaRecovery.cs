using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaRecovery
    {
        public static void EnrollBootstrapArtifacts(QaLedger ledger)
        {
            var enrolled = new List<object>();
            foreach (string role in new[] { "server", "client-a", "client-b" })
            {
                var relative = new List<string> {
                    "BepInEx/cache/harmony_interop_cache.dat",
                    "BepInEx/config/qa-preferences.json"
                };
                if (role != "server")
                {
                    string name = role.Replace('-', '_');
                    relative.Add("saves/characters_local/" + name + ".fch");
                    relative.Add("saves/characters_local/" + name + "_backup_auto-20261004-042110.fch");
                }
                foreach (string entry in relative)
                {
                    string path = ledger.Resolve(Path.Combine(ledger.Root, "profiles", role, entry));
                    if (!File.Exists(path) || ledger.State.Files.ContainsKey(path))
                        continue;
                    if (new FileInfo(path).Length > QaLedger.MiB)
                        throw new IOException("Unexpected bootstrap artifact size");
                    if (entry.EndsWith("qa-preferences.json", StringComparison.Ordinal))
                    {
                        using var prefs = JsonDocument.Parse(File.ReadAllText(path));
                        if (prefs.RootElement.GetProperty("boombox.qa.role").GetString() != role)
                            throw new IOException("Bootstrap preferences identity mismatch");
                    }
                    ledger.AdoptExisting(path, role);
                    enrolled.Add(new
                    {
                        role,
                        path,
                        bytes = new FileInfo(path).Length,
                        sha256 = QaFiles.Hash(path)
                    });
                }
            }
            string proof = Path.Combine(ledger.Root, "evidence/R00/bootstrap-enrollment.json");
            if (enrolled.Count > 0 || !File.Exists(proof))
                QaFiles.Write(
                    ledger,
                    proof,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            scope = "Exact files created by earlier owned QA attempts before move/replace hooks; no legacy project files",
                            enrolled
                        }
                    )
                );
            foreach (string role in new[] { "server", "client-a", "client-b" })
                ledger.Reconcile(Path.Combine(ledger.Root, "profiles", role));
        }
    }
}
