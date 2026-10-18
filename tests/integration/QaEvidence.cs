using System;
using System.IO;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaEvidence
    {
        public static void CaptureMatchingJoin(QaLedger ledger)
        {
            var identity = QaCandidateIdentity.Current;
            string folder = Path.Combine(ledger.Root, "evidence/R08/protocol5/matching-version");
            string candidate = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
            foreach (string role in new[] { "server", "client-a", "client-b" })
            {
                string path = Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json");
                JsonDocument report = null;
                DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                while (report == null && DateTime.UtcNow < deadline)
                {
                    try
                    {
                        report = JsonDocument.Parse(QaFiles.ReadSharedText(path));
                    }
                    catch (IOException) { }
                    catch (JsonException) { }
                    if (report == null)
                        System.Threading.Thread.Sleep(50);
                }
                if (report == null)
                    throw new IOException("JOIN-01 fresh native snapshot unavailable: " + role);
                using (report)
                {
                    var product = report.RootElement.GetProperty("product");
                    if (!product.GetProperty("loaded").GetBoolean()
                        || product.GetProperty("sha").GetString() != candidate
                        || product.GetProperty("version").GetString() != identity.Version
                        || product.GetProperty("protocol").GetInt32() != identity.Protocol)
                        throw new IOException("JOIN-01 actual loaded product mismatch: " + role);
                    QaFiles.Write(
                        ledger,
                        Path.Combine(folder, role + "-connected.json"),
                        JsonSerializer.SerializeToUtf8Bytes(report.RootElement)
                    );
                }
            }
            QaFiles.Write(
                ledger,
                Path.Combine(folder, "case.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        id = "JOIN-01",
                        passed = true,
                        candidateSha = candidate,
                        version = identity.Version,
                        protocol = identity.Protocol,
                        scope = "Actual loaded matching DLL/handshake and native D+A/B world/player/peer readiness; current ZDO proof observed. R09 Teleport baseline not rerun",
                        utc = DateTime.UtcNow
                    }
                )
            );
        }
        public static void PreserveJoinOne(QaLedger ledger)
        {
            string folder = Path.Combine(
                ledger.Root,
                "evidence/R08/history/R09-matching-copy",
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")
            );
            foreach (string name in new[] {
                "bootstrap-result.json",
                "server-connected.json",
                "client-a-connected.json",
                "client-b-connected.json",
                "replication-client-a.json",
                "replication-client-b.json",
                "restore.json"
            })
                QaFiles.Copy(ledger, Path.Combine(ledger.Root, "evidence/R09", name), Path.Combine(folder, name));
            Console.WriteLine("QA_JOIN_HISTORY_ARCHIVED " + folder + "; no current JOIN-01 verdict created");
        }
        public static void PreserveBootstrap(QaLedger ledger)
        {
            string folder = Path.Combine(ledger.Root, "evidence/R09/native-baseline");
            if (File.Exists(Path.Combine(folder, "cases.json")))
                return;
            foreach (string name in new[] {
                "bootstrap-result.json",
                "server-connected.json",
                "client-a-connected.json",
                "client-b-connected.json",
                "replication-client-a.json",
                "replication-client-b.json",
                "restore.json"
            })
                QaFiles.Copy(ledger, Path.Combine(ledger.Root, "evidence/R09", name), Path.Combine(folder, name));
            bool Recorded(string name, string flag)
            {
                using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, name)));
                return report.RootElement.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True;
            }
            bool baseline = Recorded("bootstrap-result.json", "passed")
                && Recorded("replication-client-a.json", "passed")
                && Recorded("replication-client-b.json", "passed")
                && Recorded("restore.json", "sharedPreferencesRestored")
                && Recorded("restore.json", "ownedProcessesStopped");
            string status = baseline ? "PASS_RECORDED" : "NOT RUN";
            QaFiles.Write(
                ledger,
                Path.Combine(folder, "cases.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        scope = "Archived native TCP baseline verdict requires recorded bootstrap, both replication and restoration passes. No new execution, Steam/PlayFab/NAT or listen-host acceptance.",
                        cases = new[] {
                    new { id = "NET-BASE-00", status },
                    new { id = "NET-BASE-01", status },
                    new { id = "NET-BASE-02", status },
                    new { id = "NET-BASE-03", status },
                    new { id = "NET-BASE-04", status = "NOT RUN" }
                            },
                        clientSha = QaFiles.Hash(Path.Combine(QaEnvironment.Managed, "assembly_valheim.dll")),
                        serverSha = QaFiles.Hash(
                        Path.Combine(QaEnvironment.ServerRoot, "valheim_server_Data/Managed/assembly_valheim.dll")
                    ),
                        probeSha = QaFiles.Hash(
                        Path.Combine(ledger.Root, "profiles/client-a/BepInEx/plugins/QaBootstrap/QaBootstrap.dll")
                    )
                    }
                )
            );
        }
    }
}
