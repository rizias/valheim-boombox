using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaReleaseSource
    {
        public static async Task Run(QaLedger ledger)
        {
            var identity = QaCandidateIdentity.Current;
            string mod = ledger.Project,
                root = QaEnvironment.CandidateRoot,
                final = Path.Combine(root, "release-" + identity.Version),
                stage = Path.Combine(final, "source-stage"),
                zip = Path.Combine(final, $"ValheimBoombox-{identity.Version}-source.zip");
            string extracted = Path.GetFullPath(
                Path.Combine(root, "source build in a long Windows directory with spaces " + new string('x', 166))
            );
            bool reuseArchive = File.Exists(zip)
                && Directory.Exists(stage), reuseExtraction = Directory.Exists(extracted);
            if (reuseExtraction && !reuseArchive)
                throw new IOException("Unrelated public-source extraction exists");
            if (Directory.Exists(stage) && !reuseArchive)
            {
                string exact = ledger.Resolve(stage);
                if (exact != Path.GetFullPath(stage)
                || !exact.StartsWith(
                    Path.GetFullPath(final) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                ))
                    throw new IOException("Source partial cleanup target escaped");
                var prior = QaInventory.Files(exact).ToArray();
                if (prior.Any(p => !ledger.State.Files.ContainsKey(p)))
                    throw new IOException("Unknown source partial file; cleanup denied");
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R14/source-attempt1-partial.json"),
                    JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        reason = "Runtime MusicCache substring rejected actual src/MusicCache.cs; no ZIP/build yet",
                        stage = exact,
                        files = prior.ToDictionary(p => Path.GetRelativePath(exact, p), p => QaFiles.Hash(p)),
                        utc = DateTime.UtcNow
                    })
                );
                foreach (string path in prior)
                    ledger.RemoveOwned(path);
                foreach (string directory in Directory.GetDirectories(exact, "*", SearchOption.AllDirectories)
                    .OrderByDescending(p => p.Length))
                    Directory.Delete(ledger.Resolve(directory), false);
                Directory.Delete(exact, false);
            }
            var source = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string group in new[] { "src", "assets", "Translations" })
                foreach (string path in QaInventory.Files(Path.Combine(mod, group)))
                    if (Path.GetFileName(path) != "valheimboombox_v3_1")
                        source.Add(
                            group + "/" + Path.GetRelativePath(Path.Combine(mod, group), path).Replace('\\', '/'),
                            path
                        );
            foreach (string name in new[] {
                "build.ps1",
                "package.ps1",
                "check-translations.ps1",
                "README.md",
                "PLAY.md",
                "CHANGELOG.md",
                "manifest.json",
                "LICENSE",
                "ASSET-CREDITS.md"
            })
                source.Add(name, Path.Combine(mod, name));
            _ = QaReleaseStage.VerificationReport(Path.Combine(root, "ValheimBoombox.dll"));
            source.Add("VERIFICATION.md", QaReleaseStage.VerificationPath);
            source.Add("icon.png", Path.Combine(mod, "icon.png"));
            source.Add("BUILDING.md", Path.Combine(mod, "BUILDING.md"));
            if (File.Exists(Path.Combine(mod, "PUBLISHING.md")))
                source.Add("PUBLISHING.md", Path.Combine(mod, "PUBLISHING.md"));
            foreach (string shot in QaInventory.Files(Path.Combine(final, $"ValheimBoombox-{identity.Version}/Screenshots")))
                source.Add("Screenshots/" + Path.GetFileName(shot), shot);
            long bytes = source.Values.Sum(p => new FileInfo(p).Length);
            ledger.Preflight(bytes * 3 + 48 * QaLedger.MiB, "stage");
            if (reuseArchive)
            {
                foreach (var file in source)
                    if (QaFiles.Hash(file.Value)
                        != QaFiles.Hash(Path.Combine(stage, file.Key.Replace('/', Path.DirectorySeparatorChar))))
                        throw new IOException("Existing source ZIP stage differs from current input");
            }
            else
            {
                foreach (var file in source)
                {
                    if (ForbiddenSource(file.Key))
                        throw new IOException("Forbidden source file: " + file.Key);
                    if (new[] { ".cs", ".ps1", ".md", ".json" }.Contains(Path.GetExtension(file.Key)))
                    {
                        string text = File.ReadAllText(file.Value, new UTF8Encoding(false, true));
                        if (text.Contains("C:\\Users\\", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("C:/Users/", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("work/rework-qa", StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Private path in public source: " + file.Key);
                    }
                    QaFiles.Copy(
                        ledger,
                        file.Value,
                        Path.Combine(stage, file.Key.Replace('/', Path.DirectorySeparatorChar))
                    );
                }
            }
            var hashes = source.ToDictionary(f => f.Key, f => QaFiles.Hash(f.Value), StringComparer.Ordinal);
            if (!reuseArchive)
            {
                QaFiles.Write(
                    ledger,
                    Path.Combine(stage, "SOURCE-SHA256.json"),
                    JsonSerializer.SerializeToUtf8Bytes(hashes)
                );
                using (var output = new QaQuotaStream(ledger, zip, "release-source", bytes + QaLedger.MiB))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                    foreach (string path in QaInventory.Files(stage).OrderBy(p => p, StringComparer.Ordinal))
                    {
                        var entry = archive.CreateEntry(
                            Path.GetRelativePath(stage, path).Replace('\\', '/'),
                            CompressionLevel.Optimal
                        );
                        using var input = File.OpenRead(path);
                        using var target = entry.Open();
                        input.CopyTo(target, 65536);
                    }
            }
            using (var archive = ZipFile.OpenRead(zip))
            {
                if (archive.Entries.Count != hashes.Count + 1)
                    throw new IOException("Source ZIP file count mismatch");
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.Contains("..")
                        || entry.FullName.StartsWith('/')
                        || ForbiddenSource(entry.FullName))
                        throw new IOException("Source ZIP escaped or contained private data");
                    using var input = entry.Open();
                    string actual = Convert.ToHexString(SHA256.HashData(input));
                    if (entry.FullName != "SOURCE-SHA256.json" && hashes[entry.FullName] != actual)
                        throw new IOException("Source ZIP hash mismatch");
                    string target = ledger.Resolve(
                        Path.Combine(extracted, entry.FullName.Replace('/', Path.DirectorySeparatorChar))
                    );
                    if (!target.StartsWith(extracted + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Source extraction path escaped");
                    if (reuseExtraction)
                    {
                        if (!File.Exists(target) || QaFiles.Hash(target) != actual)
                            throw new IOException("Existing extraction differs from exact source ZIP");
                        continue;
                    }
                    using var unpacked = new QaQuotaStream(ledger, target, "release-source", entry.Length);
                    using var read = entry.Open();
                    read.CopyTo(unpacked, 65536);
                    unpacked.Flush();
                }
            }
            string built = Path.Combine(extracted, "build-output"),
                outputDll = Path.Combine(built, "ValheimBoombox.dll"),
                rsp = Path.Combine(built, "build.rsp");
            ledger.ReserveFile(outputDll, 32 * QaLedger.MiB);
            ledger.ReserveFile(rsp, 512 * 1024);
            string game = QaEnvironment.ClientRoot;
            string launchFailure = Path.Combine(ledger.Root, "current/release-source-build/stderr.log"),
                keptLaunchFailure = Path.Combine(ledger.Root, "evidence/R14/source-long-cwd-first-failure.log");
            if (File.Exists(launchFailure) && !File.Exists(keptLaunchFailure))
                QaFiles.Copy(ledger, launchFailure, keptLaunchFailure);
            using (var compile = new QaSupervised(
                ledger,
                "release-source-build",
                QaBuildCandidate.PowerShell,
                ledger.Project,
                new[] {
                "-NoProfile",
                "-File",
                Path.Combine(extracted, "build.ps1"),
                "-GameManaged",
                Path.Combine(game, "valheim_Data/Managed"),
                "-BepInExCore",
                QaEnvironment.Core,
                "-OutputDirectory",
                built,
                "-DllOnly"
                },
                maximumLogBytes: 256 * 1024
            ))
            {
                int exit = await compile.WaitAsync(TimeSpan.FromSeconds(60));
                if (File.Exists(outputDll))
                    ledger.CommitFile(outputDll, "release-source");
                if (File.Exists(rsp))
                    ledger.CommitFile(rsp, "release-source");
                if (exit != 0)
                {
                    ledger.RecoveryRequired();
                    throw new IOException("Source ZIP long-path build failed");
                }
            }
            foreach (var file in hashes)
                if (QaFiles.Hash(Path.Combine(extracted, file.Key.Replace('/', Path.DirectorySeparatorChar)))
                    != file.Value)
                    throw new IOException("Source input changed during build");
            string candidate = Path.Combine(root, "ValheimBoombox.dll");
            var expectedResources = Resources(candidate);
            var actualResources = Resources(outputDll);
            bool resourcesMatch = expectedResources.Count == actualResources.Count
            && expectedResources.All(p => actualResources.TryGetValue(p.Key, out string? value) && value == p.Value);
            var actualCode = Methods(outputDll);
            bool codeMatch = QaFiles.Hash(candidate) == QaFiles.Hash(outputDll);
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R14/source.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        prepared = true,
                        longPathBuildPassed = true,
                        finalReleaseAccepted = false,
                        archive = zip,
                        archiveSha = QaFiles.Hash(zip),
                        archiveBytes = new FileInfo(zip).Length,
                        sourceFiles = source.Count,
                        sourceBytes = bytes,
                        extractionRoot = extracted,
                        longestSourcePath = hashes.Keys.Max(k => Path.Combine(extracted, k).Length),
                        compilerResponseFile = true,
                        candidateSha = QaFiles.Hash(candidate),
                        rebuiltSha = QaFiles.Hash(outputDll),
                        resourcesMatch,
                        resources = actualResources,
                        codeMatch,
                        methods = actualCode.Count,
                        scope = "Source ZIP independently read/extracted and compiled from the long Windows path against externally installed game dependencies; no game DLLs/adapters/QA/music/saves/config in source archive",
                        utc = DateTime.UtcNow
                    }
                )
            );
            if (!resourcesMatch || !codeMatch)
                throw new IOException(
                    "Deterministic source rebuild differs from the complete candidate DLL; inspect compiler, inputs and metadata"
                );
            Console.WriteLine("QA_PUBLIC_SOURCE_ZIP_BUILD_PASS " + zip);
        }
        private static bool ForbiddenSource(
            string name
        ) => name != "src/MusicCache.cs" && QaReleaseStage.Forbidden(name);
        private static Dictionary<string, string> Resources(string path)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            var data = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress)
                .GetContent();
            var result = new Dictionary<string, string>();
            foreach (var handle in metadata.ManifestResources)
            {
                var resource = metadata.GetManifestResource(handle);
                if (!resource.Implementation.IsNil)
                    throw new IOException("Unexpected external managed resource");
                int offset = checked((int)resource.Offset);
                int length = BitConverter.ToInt32(data.AsSpan(offset, 4));
                result.Add(
                    metadata.GetString(resource.Name),
                    Convert.ToHexString(SHA256.HashData(data.AsSpan(offset + 4, length)))
                );
            }
            return result;
        }
        private static Dictionary<string, string> Methods(string path)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            string Name(TypeDefinitionHandle handle)
            {
                var type = metadata.GetTypeDefinition(handle);
                return type.GetDeclaringType().IsNil
                    ? metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name)
                    : Name(type.GetDeclaringType()) + "+" + metadata.GetString(type.Name);
            }
            var result = new Dictionary<string, string>();
            foreach (var typeHandle in metadata.TypeDefinitions)
                foreach (var methodHandle in metadata.GetTypeDefinition(typeHandle).GetMethods())
                {
                    var method = metadata.GetMethodDefinition(methodHandle);
                    string key = Name(typeHandle) + "::" + metadata.GetString(method.Name) + " "
                        + Convert.ToHexString(metadata.GetBlobBytes(method.Signature));
                    byte[] il = method.RelativeVirtualAddress == 0
                        ? Array.Empty<byte>()
                        : pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? Array.Empty<byte>();
                    result.Add(key, Convert.ToHexString(SHA256.HashData(il)));
                }
            return result;
        }
    }
}
