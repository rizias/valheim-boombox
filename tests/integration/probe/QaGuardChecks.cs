using System;
using System.IO;
using Newtonsoft.Json.Linq;

public static class QaGuardChecks
{
    public static void Run()
    {
        string file = Path.Combine(BepInEx.Paths.ConfigPath, "qa-byte-check.bin");
        File.WriteAllBytes(file, new byte[] { 17 });
        bool denied = false;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Write))
        {
            try
            {
                stream.SetLength(2L * 1024 * 1024 * 1024 + 1);
            }
            catch (IOException error)
            {
                denied = error.Message.StartsWith("QA write denied:", StringComparison.Ordinal);
            }
            if (!denied || stream.Length != 1)
                throw new IOException("Actual game FileStream bypassed prewrite quota");
        }
        File.Delete(file);
        File.WriteAllBytes(file, new byte[] { 17 });
        string escape = Path.Combine(
            Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT"),
            "..",
            "qa-write-must-not-exist.bin"
        );
        bool copyDenied = false;
        try
        {
            File.Copy(file, escape);
        }
        catch (IOException error)
        {
            copyDenied = error.Message.StartsWith("QA write denied:", StringComparison.Ordinal);
        }
        if (!copyDenied || File.Exists(escape))
            throw new IOException("Two-argument File.Copy escaped QA root");
        File.Delete(file);
        bool escapeDenied = false;
        try
        {
            File.WriteAllBytes(escape, new byte[] { 1 });
        }
        catch (IOException error)
        {
            escapeDenied = error.Message.StartsWith("QA write denied:", StringComparison.Ordinal);
        }
        if (!escapeDenied || File.Exists(escape))
            throw new IOException("Actual game FileStream escaped QA root");
        string target = Path.Combine(BepInEx.Paths.ConfigPath, "qa-replace-check.bin"), temporary = target + ".tmp", backup = target + ".bak";
        File.WriteAllBytes(target, new byte[] { 17 });
        File.WriteAllBytes(temporary, new byte[] { 23, 24 });
        File.Replace(temporary, target, backup);
        bool first = !File.Exists(temporary)
            && File.ReadAllBytes(target).Length == 2
            && File.ReadAllBytes(backup)[0] == 17;
        File.WriteAllBytes(temporary, new byte[] { 31, 32, 33 });
        File.Replace(temporary, target, backup);
        bool second = !File.Exists(temporary)
            && File.ReadAllBytes(target).Length == 3
            && File.ReadAllBytes(backup).Length == 2
            && File.ReadAllBytes(backup)[0] == 23;
        if (!first || !second)
            throw new IOException("Actual owned native File.Replace backup changed contents or retained source");
        File.Delete(target);
        File.Delete(backup);
        var evidence = new JObject
        {
            ["largeSetLengthDeniedBeforeGrowth"] = denied,
            ["outsideRootDeniedBeforeCreate"] = escapeDenied,
            ["twoArgumentCopyDeniedBeforeCreate"] = copyDenied,
            ["ownedDeleteCompleted"] = !File.Exists(file),
            ["nativeReplaceBackup"] = first && second,
            ["replaceFilesCleaned"] = !File.Exists(target) && !File.Exists(backup) && !File.Exists(temporary)
        };
        File.WriteAllText(Path.Combine(BepInEx.Paths.ConfigPath, "qa-guard-checks.json"), evidence.ToString());
    }
}
