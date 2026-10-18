using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaReleaseStage
    {
        internal static string VerificationPath => Path.Combine(
            QaEnvironment.CandidateRoot,
            "release-" + QaCandidateIdentity.Current.Version + "/VERIFICATION.md"
        );
        internal static string VerificationReport(string candidate)
        {
            string report = File.ReadAllText(VerificationPath);
            if (!report.Contains(QaFiles.Hash(candidate), StringComparison.Ordinal)
            || !report.Contains("protocol " + QaCandidateIdentity.Read(candidate).Protocol, StringComparison.Ordinal))
                throw new IOException("Build verification report is not for current candidate");
            return report;
        }
        public static void Deliver(QaLedger ledger)
        {
            var identity = QaCandidateIdentity.Current;
            string attachmentReview = Path.Combine(ledger.Root, "evidence/R12/local-finish/ROOT-REVIEW.md");
            if (File.Exists(attachmentReview) && !ledger.State.Files.ContainsKey(ledger.Resolve(attachmentReview)))
                ledger.AdoptExisting(attachmentReview, "root-visual-review");
            string root = Path.Combine(QaEnvironment.CandidateRoot, "release-" + identity.Version);
            string install = Path.Combine(root, $"ValheimBoombox-{identity.Version}.zip"),
                source = Path.Combine(root, $"ValheimBoombox-{identity.Version}-source.zip"),
                dll = Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll");
            if (File.Exists(Path.Combine(root, "Rizias-" + Path.GetFileName(install))))
                install = Path.Combine(root, "Rizias-" + Path.GetFileName(install));
            if (File.Exists(Path.Combine(root, "Rizias-" + Path.GetFileName(source))))
                source = Path.Combine(root, "Rizias-" + Path.GetFileName(source));
            Verify(install, QaFiles.Hash(dll));
            using var sourceProof = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(ledger.Root, "evidence/R14/source.json"))
            );
            var proof = sourceProof.RootElement;
            if (!proof.GetProperty("longPathBuildPassed").GetBoolean()
                || !proof.GetProperty("resourcesMatch").GetBoolean()
                || !proof.GetProperty("codeMatch").GetBoolean()
                || proof.GetProperty("archiveSha").GetString() != QaFiles.Hash(source)
                || proof.GetProperty("candidateSha").GetString() != QaFiles.Hash(dll)
                || proof.GetProperty("rebuiltSha").GetString() != QaFiles.Hash(dll))
                throw new IOException("Final source artifact does not match the complete rebuilt DLL");
            bool releaseAccepted = VerifiedReceipt(
                Path.Combine(ledger.Root, "evidence/R12/local-finish/ROOT-REVIEW.json"),
                QaFiles.Hash(dll),
                QaFiles.Hash(install),
                QaFiles.Hash(source),
                out string acceptedScope
            );
            foreach (string name in new[] { "source.json", "package-final.json" })
            {
                string path = Path.Combine(ledger.Root, "evidence/R14", name);
                var accepted = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
                accepted["finalReleaseAccepted"] = releaseAccepted;
                accepted["acceptedScope"] = releaseAccepted
                    ? acceptedScope
                    : "Prepared artifacts; no current structured release verdict for these three hashes.";
                QaFiles.Write(ledger, path, JsonSerializer.SerializeToUtf8Bytes(accepted));
            }
            var hashes = new Dictionary<string, string>
            {
                [Path.GetFileName(install)] = QaFiles.Hash(install),
                [Path.GetFileName(source)] = QaFiles.Hash(source),
                ["ValheimBoombox.dll"] = QaFiles.Hash(dll),
                ["icon.png"] = QaFiles.Hash(Path.Combine(ledger.Project, "icon.png"))
            };
            string readme =
                $"# Valheim Boombox {identity.Version} MP Alpha\n\nАвтор: **Rizias**. Подготовленные файлы для Windows x64, protocol {identity.Protocol}.\n\n- "
                    + Path.GetFileName(install)
                    + $" — установочный пакет. На сервере и каждом клиенте нужен этот мод и BepInExPack Valheim 5.4.2351. Для ручной установки скопируйте BepInEx из ZIP в игровой профиль. Для карточки менеджера импортируйте полный ZIP через Import local mod / Импорт локального мода: автор Rizias, имя ValheimBoombox, версия {identity.Version}. Иконка приложена в корне ZIP и внутри папки плагина. Чужой профиль и mods.yml не нужны.\n- "
                    + Path.GetFileName(source)
                    + " — исходники, необходимые ассеты, переводы, сборочные скрипты, документация и скриншоты. Этот ZIP не устанавливают как плагин. Исходники извлечены и заново собраны; полная DLL и ресурсы сверены с кандидатом.\n- README.md внутри установочного ZIP — описание на английском и русском; PLAY.md — расширенная русская инструкция, Screenshots — игровые кадры.\n- VERIFICATION.md — проверенные сценарии и границы проверки. SHA256SUMS.txt — контрольные суммы.\n\nМузыка у каждого своя, по сети передаются состояние и игровые эффекты. Сборка и совпадение исходников сами по себе не подтверждают игровую приёмку текущих архивов.\n\nFFmpeg/ffprobe и их LGPL Sources приложены отдельно от исходников MIT-мода. Нет музыки, игровых DLL, тестовых профилей и личных сохранений. Публикация не выполнялась; для неё см. PUBLISHING.md внутри пакета.\n\n";
            readme +=
                releaseAccepted
                    ? "**Приёмка текущих архивов:** " + acceptedScope + "\n"
                    : "**Приёмка текущих архивов не подтверждена:** нет действующей записи VERIFIED для всех трёх SHA256.\n";
            QaFiles.Write(ledger, Path.Combine(root, "README.md"), Encoding.UTF8.GetBytes(readme));
            _ = VerificationReport(dll);
            QaFiles.Write(
                ledger,
                Path.Combine(root, "SHA256.json"),
                JsonSerializer.SerializeToUtf8Bytes(hashes, new JsonSerializerOptions { WriteIndented = true })
            );
            QaFiles.Write(
                ledger,
                Path.Combine(root, "SHA256SUMS.txt"),
                Encoding.UTF8.GetBytes(string.Join("\n", hashes.Select(p => p.Value + "  " + p.Key)) + "\n")
            );
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R14/delivery.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        completedArtifacts = true,
                        finalReleaseAccepted = releaseAccepted,
                        acceptedScope,
                        root,
                        version = identity.Version,
                        protocol = identity.Protocol,
                        hashes,
                        installBytes = new FileInfo(install).Length,
                        sourceBytes = new FileInfo(source).Length,
                        sourceBuildPassed = true,
                        wholeAssemblyAndResourcesMatch = true,
                        scope = "Prepared local install/source artifacts; gameplay acceptance requires the current structured verdict. No publication or manager-GUI/platform-auth claim.",
                        utc = DateTime.UtcNow
                    }
                )
            );
            Console.WriteLine("QA_RELEASE_ARTIFACTS_READY " + root);
        }
        internal static bool VerifiedReceipt(
            string path,
            string candidateSha,
            string installSha,
            string sourceSha,
            out string acceptedScope
        )
        {
            acceptedScope = "";
            if (!File.Exists(path))
                return false;
            try
            {
                using var receipt = JsonDocument.Parse(File.ReadAllText(path));
                var root = receipt.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return false;
                bool Matches(
                    string name,
                    string expected
                ) => root.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() == expected;
                if (!Matches("status", "VERIFIED")
                    || !Matches("candidateSha", candidateSha)
                    || !Matches("installSha", installSha)
                    || !Matches("sourceSha", sourceSha))
                    return false;
                if (!root.TryGetProperty("acceptedScope", out var scope) || scope.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(scope.GetString()))
                    return false;
                acceptedScope = scope.GetString()!;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        public static async Task Run(QaLedger ledger)
        {
            var identity = QaCandidateIdentity.Current;
            string mod = ledger.Project, candidate = Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll");
            string icon = Path.Combine(ledger.Root, "evidence/R12/support/icon-candidate-256.png"),
                ffmpeg = QaEnvironment.FFmpegRoot;
            string stage = Path.Combine(QaEnvironment.CandidateRoot, $"release-{identity.Version}/ValheimBoombox-{identity.Version}"),
                plugin = Path.Combine(stage, "BepInEx/plugins/Rizias-ValheimBoombox");
            string failed = Path.Combine(ledger.Root, "current/release-stage/stderr.log"),
                kept = Path.Combine(ledger.Root, "evidence/R14/stage-attempt1-stderr.log");
            if (File.Exists(failed)
                && !File.Exists(kept)
                && File.ReadAllText(failed).Contains(
                "Package preflight budget/free-space refusal",
                StringComparison.Ordinal
            ))
                QaFiles.Copy(ledger, failed, kept);
            if (Directory.Exists(stage) || File.Exists(stage + ".zip"))
                throw new IOException("Fixed package stage exists; preserve and inspect it before any retry");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(mod, "manifest.json")));
            if (manifest.RootElement.GetProperty("version_number").GetString() != identity.Version)
                throw new IOException("Candidate package version mismatch");
            var files = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            void Add(string source, string target)
            {
                QaFileIdentity.AssertRegular(source);
                _ = QaFiles.Hash(source);
                files.Add(target, new FileInfo(source).Length + 4096);
            }
            Add(candidate, Path.Combine(plugin, "ValheimBoombox.dll"));
            foreach (string name in new[] { "PLAY.md", "manifest.json", "LICENSE", "CHANGELOG.md", "ASSET-CREDITS.md" })
                Add(Path.Combine(mod, name), Path.Combine(stage, name));
            Add(icon, Path.Combine(stage, "icon.png"));
            Add(icon, Path.Combine(plugin, "icon.png"));
            files.Add(
                Path.Combine(stage, "README.md"),
                new FileInfo(Path.Combine(mod, "README.md")).Length + new FileInfo(Path.Combine(mod, "PLAY.md")).Length
                    + 4096
            );
            files.Add(Path.Combine(stage, "TOOLS.json"), 16384);
            foreach (string source in QaInventory.Files(Path.Combine(mod, "Translations")))
                Add(source, Path.Combine(plugin, "Translations", Path.GetFileName(source)));
            foreach (string name in new[] { "ffmpeg.exe", "ffprobe.exe" })
                Add(Path.Combine(ffmpeg, "bin", name), Path.Combine(plugin, "Tools/FFmpeg", name));
            foreach (string name in new[] { "LICENSE", "README.txt" })
                Add(Path.Combine(ffmpeg, name), Path.Combine(plugin, "Tools/FFmpeg", name));
            Add(Path.Combine(ffmpeg, "manifest.json"), Path.Combine(plugin, "Tools/FFmpeg/manifest.json"));
            foreach (string source in QaInventory.Files(Path.Combine(ffmpeg, "Sources")))
                Add(
                    source,
                    Path.Combine(
                        plugin,
                        "Tools/FFmpeg/Sources",
                        Path.GetRelativePath(Path.Combine(ffmpeg, "Sources"), source)
                    )
                );
            string screenshots = Path.Combine(mod, "Screenshots");
            if (Directory.Exists(screenshots))
                foreach (string source in QaInventory.Files(screenshots))
                    Add(source, Path.Combine(stage, "Screenshots", Path.GetRelativePath(screenshots, source)));
            if (File.Exists(Path.Combine(mod, "PUBLISHING.md")))
                Add(Path.Combine(mod, "PUBLISHING.md"), Path.Combine(stage, "PUBLISHING.md"));
            long budget = files.Values.Sum() + 2 * QaLedger.MiB;
            ledger.Preflight(budget * 2);
            foreach (var file in files)
                ledger.ReserveFile(file.Key, file.Value);
            try
            {
                using var process = new QaSupervised(
                    ledger,
                    "release-stage",
                    QaBuildCandidate.PowerShell,
                    ledger.Project,
                    new[] {
                    "-NoProfile",
                    "-File",
                    Path.Combine(mod, "package.ps1"),
                    "-Action",
                    "Stage",
                    "-Destination",
                    stage,
                    "-SourceDll",
                    candidate,
                    "-IconPath",
                    icon,
                    "-FFmpegRoot",
                    ffmpeg,
                    "-MaximumBytes",
                    budget.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                );
                int exit = await process.WaitAsync(TimeSpan.FromSeconds(60));
                foreach (var file in files)
                    if (File.Exists(file.Key))
                        ledger.CommitFile(file.Key, "release-stage");
                if (exit != 0)
                    throw new IOException("Controlled package stage failed; retain partial files and logs");
                if (files.Keys.Any(p => !File.Exists(p)))
                    throw new IOException("Controlled package stage omitted an expected file");
            }
            catch
            {
                ledger.RecoveryRequired();
                throw;
            }
            string report = VerificationReport(candidate);
            QaFiles.Write(ledger, Path.Combine(stage, "VERIFICATION.md"), Encoding.UTF8.GetBytes(report));
            ledger.ReserveFile(Path.Combine(stage, "SHA256.json"), 65536);
            ledger.ReserveFile(stage + ".zip", budget);
            try
            {
                using var process = new QaSupervised(
                    ledger,
                    "release-archive",
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
                    }
                );
                int exit = await process.WaitAsync(TimeSpan.FromSeconds(60));
                if (File.Exists(Path.Combine(stage, "SHA256.json")))
                    ledger.CommitFile(Path.Combine(stage, "SHA256.json"), "release-stage");
                if (File.Exists(stage + ".zip"))
                    ledger.CommitFile(stage + ".zip", "release-stage");
                if (exit != 0)
                    throw new IOException("Controlled package archive failed");
            }
            catch
            {
                ledger.RecoveryRequired();
                throw;
            }
            Verify(stage + ".zip", QaFiles.Hash(candidate));
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R14/package-final.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        prepared = true,
                        finalReleaseAccepted = false,
                        version = identity.Version,
                        protocol = identity.Protocol,
                        candidateSha = QaFiles.Hash(candidate),
                        archive = stage + ".zip",
                        archiveSha = QaFiles.Hash(stage + ".zip"),
                        bytes = new FileInfo(stage + ".zip").Length,
                        iconSha = QaFiles.Hash(icon),
                        files = QaInventory.Files(stage).Count(),
                        budget,
                        scope = "Independent ZIP/hash/manifest/icon/legal/private-data validation; no gameplay acceptance, manager GUI import, platform auth or publication claim",
                        utc = DateTime.UtcNow
                    }
                )
            );
            Console.WriteLine("QA_RELEASE_REVIEW_PACKAGE " + stage + ".zip");
        }
        internal static void Verify(string path, string dllHash)
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry("BepInEx/plugins/Rizias-ValheimBoombox/ValheimBoombox.dll") == null
            || zip.Entries.Any(
                e => e.FullName.StartsWith("BepInEx/plugins/ValheimBoombox/", StringComparison.OrdinalIgnoreCase)
            ))
                throw new IOException("Package must contain only the Rizias-ValheimBoombox plugin folder");
            if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != zip.Entries.Count)
                throw new IOException("Duplicate package entries");
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(
                new StreamReader(zip.GetEntry("SHA256.json")!.Open()).ReadToEnd()
            )!;
            const string localIcon = "BepInEx/plugins/Rizias-ValheimBoombox/icon.png";
            if (zip.GetEntry(localIcon) == null || !hashes.TryGetValue(localIcon, out string? localIconHash)
            || localIconHash != hashes["icon.png"])
                throw new IOException("Package must contain identical root and installed-folder icons");
            if (zip.Entries.Count != hashes.Count + 1)
                throw new IOException("Unexpected package files");
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name.StartsWith('/') || name.Contains("..") || Forbidden(name))
                    throw new IOException("Forbidden package path: " + name);
                using var stream = entry.Open();
                string actual = Convert.ToHexString(SHA256.HashData(stream));
                if (name != "SHA256.json" && (!hashes.TryGetValue(name, out string? expected) || actual != expected))
                    throw new IOException("Independent ZIP hash mismatch: " + name);
                if (name == "BepInEx/plugins/Rizias-ValheimBoombox/ValheimBoombox.dll" && actual != dllHash)
                    throw new IOException("Wrong candidate DLL in ZIP");
            }
            using var icon = zip.GetEntry("icon.png")!.Open();
            var header = new byte[24];
            icon.ReadExactly(header);
            if (Convert.ToHexString(header.AsSpan(0, 8)) != "89504E470D0A1A0A"
            || Convert.ToHexString(header.AsSpan(16, 8)) != "0000010000000100")
                throw new IOException("Invalid native icon dimensions");
        }
        internal static bool Forbidden(string name)
        {
            string lower = name.ToLowerInvariant();
            return new[] {
                "qagameplay",
                "qabootstrap",
                "qapreloader",
                "qatransport",
                "runtimeprobe",
                "test-game",
                "test-saves",
                "musiccache",
                "doorstop_config",
                "assembly_valheim.dll",
                "unityengine.",
                "/config/"
            }.Any(lower.Contains)
                || new[] {
                ".wav",
                ".flac",
                ".mp3",
                ".wv",
                ".cue",
                ".fch",
                ".fwl",
                ".db",
                ".cfg",
                ".pdb",
                ".py",
                ".pyc"
                }.Contains(
                Path.GetExtension(lower)
            );
        }
    }
}
