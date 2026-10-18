using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomboxQa;

internal static class JoinProofCheck
{
    private static int Main(string[] args)
    {
        string candidate = new string('A', 64), stale = new string('B', 64);
        var checks = new List<string>();
        JsonObject Proof(string scenario) => scenario == "matching-version"
            ? new JsonObject
            {
                ["passed"] = true,
                ["candidateSha"] = candidate,
                ["version"] = "0.2.0",
                ["protocol"] = 5
            }
            : new JsonObject
            {
                ["passed"] = true,
                ["candidateSha"] = candidate,
                ["requiredVersion"] = "0.2.0",
                ["requiredProtocol"] = 5,
                ["receivedProtocol"] = scenario == "incompatible-client"
                    ? 101
                    : -1
            };
        JsonObject Timing(string scenario) => new()
        {
            ["passed"] = true,
            ["candidateSha"] = candidate,
            ["scenario"] = scenario == "matching-version" ? "candidate" : scenario,
            ["seconds"] = 90,
            ["startedUtc"] = "2026-10-18T00:00:00Z",
            ["endedUtc"] = "2026-10-18T00:01:30Z"
        };
        JsonObject Restore() => new()
        {
            ["sharedPreferencesRestored"] = true,
            ["ownedProcessesStopped"] = true,
            ["serverBootstrapRemoved"] = true,
            ["preferencesBefore"] = "original",
            ["preferencesAfter"] = "original"
        };
        void Verify(
            string name,
            string scenario,
            JsonObject proof,
            JsonObject timing,
            JsonObject restore,
            bool expected
        )
        {
            bool accepted;
            try
            {
                QaJoinChecks.ValidateCurrentCase(
                    JsonSerializer.SerializeToElement(proof),
                    JsonSerializer.SerializeToElement(timing),
                    JsonSerializer.SerializeToElement(restore),
                    candidate,
                    scenario,
                    "0.2.0",
                    5
                );
                accepted = true;
            }
            catch (Exception error) when (error is IOException
                || error is KeyNotFoundException
                || error is InvalidOperationException
                || error is FormatException)
            {
                accepted = false;
            }
            if (accepted != expected)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }
        foreach (string scenario in new[] {
            "matching-version",
            "missing-client",
            "incompatible-client",
            "missing-server"
        })
        {
            Verify("accept current " + scenario, scenario, Proof(scenario), Timing(scenario), Restore(), true);
            var proof = Proof(scenario);
            proof[scenario == "matching-version" ? "version" : "requiredVersion"] = "stale-version";
            Verify("reject stale version " + scenario, scenario, proof, Timing(scenario), Restore(), false);
            proof = Proof(scenario);
            proof["candidateSha"] = stale;
            Verify("reject stale case " + scenario, scenario, proof, Timing(scenario), Restore(), false);
            proof = Proof(scenario);
            proof[scenario == "matching-version" ? "protocol" : "requiredProtocol"] = 4;
            Verify("reject stale protocol " + scenario, scenario, proof, Timing(scenario), Restore(), false);
            var timing = Timing(scenario);
            timing["candidateSha"] = stale;
            Verify("reject stale timing " + scenario, scenario, Proof(scenario), timing, Restore(), false);
            timing = Timing(scenario);
            timing["passed"] = false;
            Verify("reject failed current timing " + scenario, scenario, Proof(scenario), timing, Restore(), false);
            var restore = Restore();
            restore["ownedProcessesStopped"] = false;
            Verify(
                "reject incomplete restore " + scenario,
                scenario,
                Proof(scenario),
                Timing(scenario),
                restore,
                false
            );
        }
        var noTiming = Timing("matching-version");
        noTiming.Remove("startedUtc");
        Verify(
            "reject missing current timestamps",
            "matching-version",
            Proof("matching-version"),
            noTiming,
            Restore(),
            false
        );
        var mismatch = Restore();
        mismatch["preferencesAfter"] = "changed";
        Verify(
            "reject preference mismatch",
            "matching-version",
            Proof("matching-version"),
            Timing("matching-version"),
            mismatch,
            false
        );
        Directory.CreateDirectory(args[0]);
        string result = JsonSerializer.Serialize(
            new
            {
                passed = true,
                count = checks.Count,
                checks,
                nativeJoinRun = false
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(Path.Combine(args[0], "result.json"), result);
        Console.WriteLine(result);
        return 0;
    }
}
