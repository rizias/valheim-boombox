using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaReleaseRefresh
    {
        public static void PruneCaptureCopies(QaLedger ledger)
        {
            string folder = ledger.Resolve(Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture"));
            var kept = new System.Collections.Generic.List<object>();
            long freed = 0;
            string oldStage = ledger.Resolve(Path.Combine(QaEnvironment.CandidateRoot, "package-stage"));
            string oldArchive = oldStage + ".zip";
            if (Directory.Exists(oldStage) && File.Exists(oldArchive))
            {
                using var archive = ZipFile.OpenRead(oldArchive);
                foreach (string file in QaInventory.Files(oldStage).ToArray())
                {
                    string path = ledger.Resolve(file);
                    if (!path.StartsWith(oldStage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Stage cleanup escaped");
                    var entry = archive.GetEntry(Path.GetRelativePath(oldStage, path).Replace('\\', '/'));
                    if (entry == null || entry.Length != new FileInfo(path).Length)
                        continue;
                    string expected = QaFiles.Hash(path);
                    using var input = entry.Open();
                    if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)) != expected)
                        continue;
                    long length = new FileInfo(path).Length;
                    kept.Add(new
                    {
                        removed = path,
                        retained = oldArchive,
                        member = entry.FullName,
                        sha = expected,
                        bytes = length
                    });
                    ledger.RemoveOwned(path);
                    freed += length;
                }
            }
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length == 0
            && System.Diagnostics.Process.GetProcessesByName("valheim_server").Length == 0)
            {
                string derived = ledger.Resolve(
                    Path.Combine(ledger.Root, "profiles/client-a/BepInEx/plugins/ValheimBoombox/ValheimBoombox.dll")
                );
                foreach (string name in new[] { "ValheimBoombox.dll", "previous.dll" })
                {
                    string retained = ledger.Resolve(Path.Combine(QaEnvironment.CandidateRoot, name));
                    if (File.Exists(derived)
                        && File.Exists(retained)
                        && QaFiles.Hash(derived) == QaFiles.Hash(retained))
                    {
                        long bytes = new FileInfo(derived).Length;
                        kept.Add(new
                        {
                            removed = derived,
                            retained,
                            sha = QaFiles.Hash(retained),
                            bytes
                        });
                        ledger.RemoveOwned(derived);
                        freed += bytes;
                        break;
                    }
                }
            }
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length == 0
            && System.Diagnostics.Process.GetProcessesByName("valheim_server").Length == 0)
                foreach (string role in new[] {
                    "r12-failed-still",
                    "r12-offline-mux",
                    "r12-still-demo-probe",
                    "r12-still-demo",
                    "demo-final-main",
                    "demo-final-mini12",
                    "demo-final-mini15"
                })
                {
                    string log = ledger.Resolve(Path.Combine(ledger.Root, "current", role, "stdout.log")), packed = log
                        + ".zip";
                    if (!File.Exists(log))
                        continue;
                    string hash = QaFiles.Hash(log);
                    long size = new FileInfo(log).Length;
                    if (!File.Exists(packed))
                    {
                        using (var output = new QaQuotaStream(ledger, packed, "preserve-old-log", size + 1024))
                        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                        using (var input = File.OpenRead(log))
                        using (var target = archive.CreateEntry("stdout.log", CompressionLevel.Optimal).Open())
                            input.CopyTo(target);
                    }
                    using (var archive = ZipFile.OpenRead(packed))
                    using (var input = archive.GetEntry("stdout.log").Open())
                        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)) != hash)
                            throw new IOException("Archived log differs");
                    ledger.RemoveOwned(log);
                    freed += size - new FileInfo(packed).Length;
                    kept.Add(new
                    {
                        removed = log,
                        retained = packed,
                        sha = hash,
                        bytes = size
                    });
                }
            string serverCopy = ledger.Resolve(
                Path.Combine(ledger.Root, "profiles/server/BepInEx/plugins/ValheimBoombox/ValheimBoombox.dll")
            );
            string retainedCopy = ledger.Resolve(
                Path.Combine(ledger.Root, "profiles/client-b/BepInEx/plugins/ValheimBoombox/ValheimBoombox.dll")
            );
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length == 0
            && System.Diagnostics.Process.GetProcessesByName("valheim_server").Length == 0
            && File.Exists(serverCopy)
            && File.Exists(retainedCopy)
            && QaFiles.Hash(serverCopy) == QaFiles.Hash(retainedCopy))
            {
                long bytes = new FileInfo(serverCopy).Length;
                kept.Add(new
                {
                    removed = serverCopy,
                    retained = retainedCopy,
                    sha = QaFiles.Hash(retainedCopy),
                    bytes
                });
                ledger.RemoveOwned(serverCopy);
                freed += bytes;
            }
            var evidence = Directory.GetFiles(
                Path.Combine(ledger.Root, "evidence"),
                "*.png",
                SearchOption.AllDirectories
            )
                .ToLookup(
                p => new FileInfo(p).Length
            );
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length == 0
            && System.Diagnostics.Process.GetProcessesByName("valheim_server").Length == 0
            && File.Exists(retainedCopy))
            {
                string packed = ledger.Resolve(Path.Combine(QaEnvironment.CandidateRoot, "client-b-preserved.dll.zip"));
                string expected = QaFiles.Hash(retainedCopy);
                long length = new FileInfo(retainedCopy).Length;
                if (!File.Exists(packed))
                {
                    using var output = new QaQuotaStream(ledger, packed, "preserve-test-dll", length + 1024);
                    using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
                    using var input = File.OpenRead(retainedCopy);
                    using var target = archive.CreateEntry("ValheimBoombox.dll", CompressionLevel.Optimal).Open();
                    input.CopyTo(target);
                }
                using (var archive = ZipFile.OpenRead(packed))
                using (var input = archive.GetEntry("ValheimBoombox.dll").Open())
                    if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)) != expected)
                        throw new IOException("Preserved test DLL differs");
                kept.Add(new
                {
                    removed = retainedCopy,
                    retained = packed,
                    sha = expected,
                    bytes = length
                });
                ledger.RemoveOwned(retainedCopy);
                freed += length - new FileInfo(packed).Length;
            }
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length == 0
            && System.Diagnostics.Process.GetProcessesByName("valheim_server").Length == 0
            && File.Exists(retainedCopy))
            {
                string expected = QaFiles.Hash(retainedCopy);
                long length = new FileInfo(retainedCopy).Length;
                foreach (string backup in Directory.GetFiles(QaHistory.Root(ledger), "*.zip"))
                {
                    using var archive = ZipFile.OpenRead(backup);
                    var entry = archive.Entries.FirstOrDefault(
                        e => e.FullName.EndsWith("/ValheimBoombox.dll", StringComparison.Ordinal) && e.Length == length
                    );
                    if (entry == null)
                        continue;
                    using var input = entry.Open();
                    if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)) != expected)
                        continue;
                    kept.Add(new
                    {
                        removed = retainedCopy,
                        retained = backup,
                        member = entry.FullName,
                        sha = expected,
                        bytes = length
                    });
                    ledger.RemoveOwned(retainedCopy);
                    freed += length;
                    break;
                }
            }
            foreach (string file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                string path = ledger.Resolve(file);
                string proof = evidence[new FileInfo(path).Length].FirstOrDefault(p => QaFiles.Hash(path)
                    == QaFiles.Hash(p));
                if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !ledger.State.Files.ContainsKey(path))
                    throw new IOException("Capture cleanup ownership mismatch");
                if (proof == null || !File.Exists(proof) || QaFiles.Hash(path) != QaFiles.Hash(proof))
                    continue;
                long bytes = new FileInfo(path).Length;
                kept.Add(new
                {
                    removed = path,
                    retained = proof,
                    sha = QaFiles.Hash(proof),
                    bytes
                });
                ledger.RemoveOwned(path);
                freed += bytes;
            }
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R10/capture-duplicates-removed.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    freed,
                    kept
                })
            );
            Console.WriteLine("QA_IDENTICAL_CAPTURE_COPIES_REMOVED bytes=" + freed);
        }
        public static void Brand(QaLedger ledger)
        {
            var identity = QaCandidateIdentity.Current;
            string release = Path.Combine(QaEnvironment.CandidateRoot, "release-" + identity.Version);
            foreach (string name in new[] { $"ValheimBoombox-{identity.Version}.zip", $"ValheimBoombox-{identity.Version}-source.zip" })
                ledger.RenameOwned(Path.Combine(release, name), Path.Combine(release, "Rizias-" + name));
            foreach (string name in new[] { "package-final.json", "source.json" })
            {
                string path = Path.Combine(ledger.Root, "evidence/R14", name);
                var proof = System.Text.Json.Nodes.JsonNode.Parse(QaFiles.ReadSharedText(path))!;
                proof["archive"] = Path.Combine(
                    release,
                    "Rizias-" + Path.GetFileName(proof["archive"]!.GetValue<string>())
                );
                proof["author"] = "Rizias";
                QaFiles.Write(ledger, path, JsonSerializer.SerializeToUtf8Bytes(proof));
            }
            QaReleaseStage.Deliver(ledger);
        }

        public static void ArchiveBaseline(QaLedger ledger)
        {
            string source = ledger.Resolve(Path.Combine(QaEnvironment.CandidateRoot, "d9-point3-baseline.dll"));
            string archive = ledger.Resolve(Path.Combine(ledger.Root, "evidence/R14/d9-point3-baseline.zip"));
            if (!File.Exists(source))
                throw new IOException("Named baseline already absent; inspect archive before further cleanup");
            if (!ledger.State.Files.ContainsKey(source))
                throw new IOException("Unowned baseline");
            string expected = QaFiles.Hash(source);
            if (!File.Exists(archive))
            {
                using var output = new QaQuotaStream(
                    ledger,
                    archive,
                    "baseline-preservation",
                    new FileInfo(source).Length + QaLedger.MiB
                );
                using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
                using var input = File.OpenRead(source);
                using var target = zip.CreateEntry(Path.GetFileName(source), CompressionLevel.Optimal).Open();
                input.CopyTo(target);
            }
            using (var zip = ZipFile.OpenRead(archive))
            using (var input = zip.GetEntry(Path.GetFileName(source))!.Open())
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)) != expected)
                    throw new IOException("Baseline archive differs");
            ledger.RemoveOwned(source);
            Console.WriteLine("QA_BASELINE_PRESERVED " + expected + " " + archive);
        }

        public static async Task Run(QaLedger ledger)
        {
            var identity = QaCandidateIdentity.Current;
            string mod = ledger.Project;
            string root = ledger.Resolve(QaEnvironment.CandidateRoot);
            string release = Path.Combine(root, "release-" + identity.Version), stage = Path.Combine(release, "ValheimBoombox-" + identity.Version);
            string zip = stage + ".zip", sourceZip = Path.Combine(release, $"ValheimBoombox-{identity.Version}-source.zip");
            string keep = QaHistory.Root(ledger);
            foreach (string path in new[] {
                zip,
                sourceZip,
                Path.Combine(release, "Rizias-" + Path.GetFileName(zip)),
                Path.Combine(release, "Rizias-" + Path.GetFileName(sourceZip))
            })
            {
                if (!File.Exists(path))
                    continue;
                string preserved = Path.Combine(
                    release,
                    Path.GetFileNameWithoutExtension(path) + "-previous-" + QaFiles.Hash(path).Substring(0, 8) + ".zip"
                );
                string archiveHash = QaFiles.Hash(path);
                if (!File.Exists(preserved))
                    ledger.RenameOwned(path, preserved);
                else
                {
                    if (archiveHash != QaFiles.Hash(preserved))
                        throw new IOException("Archive preservation mismatch");
                    ledger.RemoveOwned(path);
                }
                if (archiveHash != QaFiles.Hash(preserved))
                    throw new IOException("Preserved archive hash mismatch");
            }
            foreach (string name in new[] { "source.json", "package-final.json", "delivery.json" })
            {
                string path = Path.Combine(ledger.Root, "evidence/R14", name);
                string preserved = Path.Combine(keep, name);
                if (File.Exists(path) && !File.Exists(preserved))
                    QaHistory.Copy(ledger, path, preserved);
            }
            foreach (string tree in new[] {
                Path.Combine(release, "source-stage"),
                Path.Combine(root, "source build in a long Windows directory with spaces " + new string('x', 166))
            })
            {
                string exact = ledger.Resolve(tree);
                if (!exact.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Derived cleanup escaped candidate root");
                if (!Directory.Exists(exact))
                    continue;
                var files = QaInventory.Files(exact).ToArray();
                if (files.Any(p => !ledger.State.Files.ContainsKey(p)))
                    throw new IOException("Unowned derived file: cleanup refused");
                foreach (string file in files)
                    ledger.RemoveOwned(file);
                foreach (string directory in Directory.GetDirectories(exact, "*", SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Length))
                    Directory.Delete(ledger.Resolve(directory), false);
                Directory.Delete(exact, false);
            }
            string candidate = Path.Combine(root, "ValheimBoombox.dll");
            string oldPlugin = Path.Combine(stage, "BepInEx/plugins/ValheimBoombox"),
                plugin = Path.Combine(stage, "BepInEx/plugins/Rizias-ValheimBoombox");
            if (Directory.Exists(oldPlugin))
                ledger.RenameOwnedTree(oldPlugin, plugin);
            QaFiles.Copy(ledger, candidate, Path.Combine(plugin, "ValheimBoombox.dll"));
            foreach (string translation in QaInventory.Files(Path.Combine(mod, "Translations")))
                QaFiles.Copy(ledger, translation, Path.Combine(plugin, "Translations", Path.GetFileName(translation)));
            QaFiles.Copy(ledger, Path.Combine(mod, "icon.png"), Path.Combine(plugin, "icon.png"));
            QaFiles.Copy(ledger, Path.Combine(mod, "icon.png"), Path.Combine(stage, "icon.png"));
            QaFiles.Copy(
                ledger,
                Path.Combine(QaEnvironment.FFmpegRoot, "manifest.json"),
                Path.Combine(plugin, "Tools/FFmpeg/manifest.json")
            );
            foreach (string name in new[] { "PLAY.md", "CHANGELOG.md", "manifest.json", "LICENSE" })
                QaFiles.Copy(ledger, Path.Combine(mod, name), Path.Combine(stage, name));
            if (File.Exists(Path.Combine(mod, "PUBLISHING.md")))
                QaFiles.Copy(ledger, Path.Combine(mod, "PUBLISHING.md"), Path.Combine(stage, "PUBLISHING.md"));
            QaFiles.Write(
                ledger,
                Path.Combine(stage, "VERIFICATION.md"),
                Encoding.UTF8.GetBytes(QaReleaseStage.VerificationReport(candidate))
            );
            QaFiles.Copy(ledger, Path.Combine(mod, "README.md"), Path.Combine(stage, "README.md"));
            var shots = new[] {
                ("model03/main-peak-front.png", "boombox-in-game.png"),
                ("model03/mini12-peak-front.png", "mini-speaker.png"),
                ("local-finish/full-mount-shield-cape-standing-back.png", "shield-and-cape.png")
            };
            foreach (var shot in shots)
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R12", shot.Item1),
                    Path.Combine(stage, "Screenshots", shot.Item2)
                );
            string sums = Path.Combine(stage, "SHA256.json");
            if (File.Exists(sums))
                ledger.RemoveOwned(sums);
            long budget = QaInventory.Files(stage).Sum(p => new FileInfo(p).Length) + QaLedger.MiB;
            ledger.ReserveFile(sums, 65536);
            ledger.ReserveFile(zip, budget);
            using (var archive = new QaSupervised(
                ledger,
                "release-refresh",
                QaBuildCandidate.PowerShell,
                ledger.Project,
                new[] {
                "-NoProfile",
                "-File",
                Path.Combine(mod, "package.ps1"),
                "-Action",
                "Archive",
                "-Destination",
                stage,
                "-MaximumBytes",
                budget.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                maximumLogBytes: 256 * 1024
            ))
            {
                int exit = await archive.WaitAsync(TimeSpan.FromSeconds(60));
                if (File.Exists(sums))
                    ledger.CommitFile(sums);
                if (File.Exists(zip))
                    ledger.CommitFile(zip);
                if (exit != 0)
                    throw new IOException("Final package archive failed; previous archives preserved");
            }
            QaReleaseStage.Verify(zip, QaFiles.Hash(candidate));
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R14/package-final.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    prepared = true,
                    candidateSha = QaFiles.Hash(candidate),
                    archive = zip,
                    archiveSha = QaFiles.Hash(zip),
                    bytes = new FileInfo(zip).Length,
                    sourcePending = true,
                    utc = DateTime.UtcNow
                })
            );
            Console.WriteLine("QA_REFRESH_INSTALL_READY " + zip);
        }
    }
}
