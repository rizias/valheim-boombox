using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BoomboxQa
{
    internal static class QaProofIdentity
    {
        internal static string Current => Fingerprint(
            QaFiles.Hash(typeof(QaProofIdentity).Assembly.Location),
            QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")));

        internal static string Fingerprint(string controllerSha, string fixtureSha) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(controllerSha + "\n" + fixtureSha)));

        internal static bool Matches(JsonNode proof, string verifierSha) =>
            proof["verifierSha"]?.GetValue<string>() == verifierSha;

        internal static JsonNode? ReadCompatible(string path, string candidateSha, string verifierSha)
        {
            if (!File.Exists(path))
                return null;
            var proof = JsonNode.Parse(File.ReadAllText(path));
            return proof != null && proof["candidateSha"]?.GetValue<string>() == candidateSha
                && Matches(proof, verifierSha) ? proof : null;
        }

        internal static JsonNode[] PassedPrefix(JsonNode? proof, string[] ids, bool allowPartial = false)
        {
            if (proof?["currentCases"] is not JsonArray cases)
                return Array.Empty<JsonNode>();
            var passed = cases.Where(c => c?["passed"]?.GetValue<bool>() == true
                && ids.Contains(c["id"]!.GetValue<string>()))
                .Select(c => c!).ToArray();
            if ((!allowPartial && passed.Length != ids.Length)
                || !passed.Select(c => c["id"]!.GetValue<string>()).SequenceEqual(ids.Take(passed.Length)))
                return Array.Empty<JsonNode>();
            return passed;
        }
    }
}
