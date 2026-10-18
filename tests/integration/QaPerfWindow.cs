using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace BoomboxQa
{
    public sealed class QaPerfWindow
    {
        private sealed class Bucket
        {
            public long Count;
            public double Managed, Mono, Private;
            public int[] Maximum = new int[12];
            public void Add(JsonNode row, long memory, int decoders)
            {
                Count++;
                Managed += row["managed"]!.GetValue<long>();
                Mono += row["monoUsed"]!.GetValue<long>();
                Private += memory;
                for (int i = 0; i < 11; i++)
                    Maximum[i] = Math.Max(Maximum[i], row["resources"]![i]!.GetValue<int>());
                Maximum[11] = Math.Max(Maximum[11], decoders);
            }
        }
        private readonly Dictionary<string, Bucket[]> first = new(), last = new();
        public QaPerfWindow()
        {
            foreach (string role in new[] { "server", "client-a", "client-b" })
            {
                first[role] = Enumerable.Range(0, 12).Select(_ => new Bucket()).ToArray();
                last[role] = Enumerable.Range(0, 12).Select(_ => new Bucket()).ToArray();
            }
        }
        public void Add(string role, JsonNode row, JsonNode actor)
        {
            double seconds = actor["seconds"]!.GetValue<double>();
            int step = actor["step"]!.GetValue<int>();
            if (step < 0 || step > 11 || actor["settled"]?.GetValue<bool>() != true || seconds < 1200)
                return;
            var processes = row["processes"]!.AsArray();
            var game = processes.Single(p => p!["kind"]!.GetValue<string>() == "game")!;
            long memory = game["private"]!.GetValue<long>();
            int decoders = processes.Count(p => p!["kind"]!.GetValue<string>() != "game"
                && p["alive"]!.GetValue<bool>());
            if (seconds < 3000)
                first[role][step].Add(row, memory, decoders);
            if (seconds >= 42600 && seconds < 44400)
                last[role][step].Add(row, memory, decoders);
        }
        public object Finish()
        {
            var result = new List<object>();
            foreach (string role in first.Keys)
                for (int step = 0; step < 12; step++)
                {
                    var a = first[role][step];
                    var b = last[role][step];
                    if (a.Count < 25 || b.Count < 25)
                        throw new IOException("Comparable thirty-minute coverage missing: " + role + "/" + step);
                    double managed = b.Managed / b.Count - a.Managed / a.Count, mono = b.Mono / b.Count - a.Mono / a.Count, memory = b.Private / b.Count - a.Private / a.Count;
                    if (managed > 32 * QaLedger.MiB || mono > 32 * QaLedger.MiB || memory > 64 * QaLedger.MiB)
                        throw new IOException("PERF03 memory growth exceeded: " + role + "/" + step);
                    for (int i = 0; i < 12; i++)
                        if (i != 10 && b.Maximum[i] > a.Maximum[i])
                            throw new IOException("Comparable settled resources grew: " + role + "/" + step + "/" + i);
                    result.Add(new
                    {
                        role,
                        step,
                        firstSamples = a.Count,
                        lastSamples = b.Count,
                        managedGrowth = managed,
                        monoGrowth = mono,
                        privateGrowth = memory,
                        firstResources = a.Maximum,
                        lastResources = b.Maximum
                    });
                }
            return result;
        }
    }
}
