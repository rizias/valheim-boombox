using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BoomboxQa;

internal static class ReleaseReceiptCheck
{
    private static int Main(string[] args)
    {
        string root = args[0], path = Path.Combine(root, "ROOT-REVIEW.json");
        Directory.CreateDirectory(root);
        string candidate = new string('A', 64), install = new string('B', 64), source = new string('C', 64);
        var passed = new List<string>();
        bool Read(out string scope) => QaReleaseStage.VerifiedReceipt(path, candidate, install, source, out scope);
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            passed.Add(name);
        }
        Dictionary<string, object?> Valid() => new()
        {
            ["status"] = "VERIFIED",
            ["candidateSha"] = candidate,
            ["installSha"] = install,
            ["sourceSha"] = source,
            ["acceptedScope"] = "Current three artifacts: scoped functional acceptance"
        };
        void Write(object value) => File.WriteAllText(path, JsonSerializer.Serialize(value));
        Check(!Read(out string missingScope) && missingScope == "", "missing receipt is unaccepted");
        File.WriteAllText(Path.Combine(root, "ROOT-REVIEW.md"), "REVOKED historical candidate " + candidate);
        Check(!Read(out _), "historical markdown hash cannot accept release");
        Write(Valid());
        Check(
            Read(out string scope) && scope == (string)Valid()["acceptedScope"]!,
            "exact VERIFIED receipt accepts only its stated scope"
        );
        foreach (string status in new[] { "REVOKED", "PENDING", "FAILED", "verified", "VERIFIED_RELEASE" })
        {
            var receipt = Valid();
            receipt["status"] = status;
            Write(receipt);
            Check(!Read(out string rejectedScope) && rejectedScope == "", "reject status " + status);
        }
        foreach (string key in new[] { "candidateSha", "installSha", "sourceSha" })
        {
            var receipt = Valid();
            receipt[key] = new string('D', 64);
            Write(receipt);
            Check(!Read(out _), "reject mismatched " + key);
            receipt = Valid();
            receipt.Remove(key);
            Write(receipt);
            Check(!Read(out _), "reject missing " + key);
        }
        foreach (object? invalidScope in new object?[] { "", "  ", null, 12 })
        {
            var receipt = Valid();
            receipt["acceptedScope"] = invalidScope;
            Write(receipt);
            Check(!Read(out _), "reject empty or non-text scope " + JsonSerializer.Serialize(invalidScope));
        }
        var wrongType = Valid();
        wrongType["status"] = true;
        Write(wrongType);
        Check(!Read(out _), "reject non-text status");
        foreach (string text in new[] { "{", "null", "[]", "\"VERIFIED\"" })
        {
            File.WriteAllText(path, text);
            Check(!Read(out _), "reject malformed or non-object receipt " + text);
        }
        string result = JsonSerializer.Serialize(
            new
            {
                passed = true,
                checks = passed,
                count = passed.Count,
                nativeGameStarted = false
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(Path.Combine(root, "result.json"), result);
        Console.WriteLine(result);
        return 0;
    }
}
