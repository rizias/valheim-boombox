using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaAuraConfigCases
    {
        public static async Task Run(QaLedger ledger)
        {
            var cases = new List<object>();
            void Save() => QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R03-R04/runtime-cases.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                        fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                        scope = "Native host/client config and instantiated HUD/tooltip numeric observations; no GUI input, rendered visual acceptance, cold restart or standalone-world override acceptance",
                        cases,
                        utc = DateTime.UtcNow
                    }
                )
            );
            void Record(string id, bool passed, object observed)
            {
                cases.Add(new
                {
                    id,
                    passed,
                    observed,
                    utc = DateTime.UtcNow
                });
                Save();
                Console.WriteLine("QA_AURA_CONFIG " + id + " " + passed);
            }
            void Add(string id, bool passed, object observed)
            {
                Record(id, passed, observed);
                if (!passed)
                    throw new IOException("Configuration requirement failed: " + id);
            }
            try
            {
                foreach (string role in new[] { "client-a", "client-b" })
                    await QaAuraCases.Command(
                        role,
                        "aura-config-set",
                        new
                        {
                            radius = 2f,
                            strength = new[] { 9f, 9f, 9f, 9f, 9f }
                        }
                    );
                var before = await QaAuraCases.Command("client-b", "aura-config-read");
                bool authoritative = before["sample"]!["localRadius"]!.GetValue<double>() == 2
                    && before["sample"]!["hostRadius"]!.GetValue<double>() == 20
                    && before["sample"]!["hostStrength"]!.AsArray().All(s => s!.GetValue<double>() == 1);
                Add("CONFIG-01-host-override-native", authoritative, before);
                if (!authoritative)
                    throw new IOException(
                        "Native host balance did not override deliberately different local client values"
                    );
                string config = Path.Combine(ledger.Root, "profiles/client-b/BepInEx/config/local.valheim.boombox.cfg"),
                    hash = QaFiles.Hash(config);
                DateTime started = DateTime.UtcNow;
                double[] strengths = { .5, 2, 3, 4, .25 };
                await QaAuraCases.Command(
                    "server",
                    "aura-config-set",
                    new
                    {
                        radius = 6f,
                        strength = strengths
                    }
                );
                JsonNode after;
                do
                {
                    after = await QaAuraCases.Command("client-b", "aura-config-read");
                } while (after["sample"]!["hostRadius"]!.GetValue<double>() != 6
                    && (DateTime.UtcNow - started).TotalSeconds < 2);
                double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                bool received = elapsed <= 2
                    && after["sample"]!["hostRadius"]!.GetValue<double>() == 6
                    && after["sample"]!["hostStrength"]!.AsArray().Select(s => s!.GetValue<double>()).SequenceEqual(
                    strengths
                );
                Add(
                    "CONFIG-02-headless-runtime-sync",
                    received,
                    new
                    {
                        expected = strengths,
                        radius = 6,
                        elapsed,
                        before,
                        after
                    }
                );
                Add(
                    "CONFIG-03-client-file-preserved",
                    QaFiles.Hash(config) == hash,
                    new
                    {
                        beforeSha = hash,
                        afterSha = QaFiles.Hash(config),
                        scope = "Exact personal client file unchanged by host sync; personal color/DSP appearance not accepted here"
                    }
                );
                if (!received)
                    throw new IOException("Native host radius/strength update missed two-second bound");
                var resource = await QaAuraCases.Command(
                    "client-b",
                    "sample",
                    new
                    {
                        duration = 5.4,
                        missing = 65
                    }
                );
                double[] expected = { 12.6, 18.9, 25.2 }, actual = new double[3], ticks = new double[3];
                string[] names = { "health", "stamina", "eitr" };
                for (int i = 0; i < 3; i++)
                {
                    ticks[i] = resource["sample"]!["ticks"]![i]!.GetValue<double>();
                    actual[i] = resource["sample"]!["after"]![names[i]]!.GetValue<double>()
                        - resource["sample"]!["before"]![names[i]]!.GetValue<double>();
                }
                bool scaled = Enumerable.Range(0, 3).All(
                    i => ticks[i] >= 1 && ticks[i] <= 2 && Math.Abs(actual[i] - expected[i] * ticks[i]) <= .02
                );
                Add(
                    "CONFIG-02-real-resource-strength",
                    scaled,
                    new
                    {
                        expected,
                        actual,
                        ticks,
                        resource
                    }
                );
                if (!scaled)
                    throw new IOException("Actual resources did not use host strength multipliers");
                foreach (string language in new[] { "Russian", "English" })
                {
                    await QaAuraCases.Command(
                        "client-b",
                        "aura-hud-language",
                        new
                        {
                            language
                        }
                    );
                    await Task.Delay(750);
                    var hud = await QaAuraCases.Command("client-b", "aura-config-read");
                    var effects = hud["sample"]!["effects"]!.AsArray();
                    var rows = hud["sample"]!["hudRows"]!.AsArray();
                    bool unique = effects.Count == 5
                        && effects.Select(e => e!["name"]!.GetValue<string>()).Distinct().Count() == 5
                        && rows.Count == 5
                        && rows.All(r => r!["active"]!.GetValue<bool>())
                        && effects.All(e => !string.IsNullOrEmpty(e!["icon"]?.GetValue<string>()));
                    Add("HUD-01-02-native-rows-" + language, unique, hud);
                    if (!unique)
                        throw new IOException("Native HUD does not contain five unique aura icons");
                }
            }
            catch (Exception failure)
            {
                try
                {
                    Record(
                        "CONFIG-HUD-first-failure",
                        false,
                        new
                        {
                            error = failure.ToString()
                        }
                    );
                }
                catch (Exception recording)
                {
                    throw new AggregateException("Could not record configuration failure", failure, recording);
                }
                throw;
            }
            finally
            {
                foreach (string role in new[] { "server", "client-a", "client-b" })
                    await QaAuraCases.Command(role, "aura-config-restore");
            }
        }
    }
}
