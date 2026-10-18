using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using VikingBoombox;

public static class AttackChecks
{
    private const int Frequency = 48000;
    private const int Chunk = 960;
    private static readonly double[] Phases = { 0, .013, .025, .037, .049 };

    public static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string game = BoomboxQa.QaEnvironment.ClientRoot;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string dir in new[] { BoomboxQa.QaEnvironment.Managed, BoomboxQa.QaEnvironment.Core })
            {
                string path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            Run(root);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static float[] Signal(string kind)
    {
        var samples = new float[Frequency * 6 * 2];
        for (int i = 0; i < Frequency * 6; i++)
        {
            double t = i / (double)Frequency, value = 0;
            if (kind == "sustain")
                value = .3 * Math.Sin(2 * Math.PI * 100 * t) * Math.Min(1, t / .2);
            if (kind == "attacks" && t >= .5 && t < 5.5)
            {
                double since = (t - .5) % .5;
                double frequency = ((int)((t - .5) / .5) % 2) == 0 ? 80 : 850;
                value = .7 * Math.Sin(2 * Math.PI * frequency * since) * Math.Min(1, since / .002)
                    * Math.Exp(-since / .045);
            }
            samples[i * 2] = samples[i * 2 + 1] = (short)(value * 32767) / 32768f;
        }
        return samples;
    }

    private static double[] Events(float[] samples, double phase)
    {
        var analyzer = new MusicSoundAnalyzer(Frequency);
        var strikes = new List<MusicStrike>();
        var mono = new float[Chunk];
        int first = (int)Math.Round(phase * Frequency);
        double until = double.NegativeInfinity;
        for (int frame = first; frame < samples.Length / 2; frame += Chunk)
        {
            int count = Math.Min(Chunk, samples.Length / 2 - frame);
            for (int i = 0; i < count; i++)
            {
                float mixed = (float)(0 + samples[(frame + i) * 2] / 2.0);
                mono[i] = (float)(mixed + samples[(frame + i) * 2 + 1] / 2.0);
            }
            analyzer.Push(frame / (double)Frequency, mono, count);
            double settled = (frame + count) / (double)Frequency - MusicSoundAnalyzer.SettledSeconds;
            analyzer.StrikesIn(until, settled, strikes);
            until = Math.Max(until, settled);
        }
        analyzer.StrikesIn(until, double.PositiveInfinity, strikes);
        return strikes.Select(strike => strike.Time).ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string root)
    {
        string folder = Path.Combine(BoomboxQa.QaEnvironment.QaRoot, "current/offline/attack-verification");
        Directory.CreateDirectory(folder);
        var expected = Enumerable.Range(1, 10).Select(i => i * .5).ToArray();
        var results = new List<object>();
        foreach (string kind in new[] { "silence", "sustain", "attacks" })
        {
            var samples = Signal(kind);
            foreach (double phase in Phases)
            {
                var times = Events(samples, phase);
                if (kind == "silence" && times.Length != 0)
                    throw new Exception("Silence gave strikes, analysis start " + phase + " s");
                if (kind == "sustain" && times.Any(t => t > .3))
                    throw new Exception(
                        "Steady sustained note gave repeated strikes, analysis start " + phase + " s: "
                            + string.Join(",", times)
                    );
                if (kind == "attacks"
                    && (times.Length != expected.Length || expected.Where((t, i) => Math.Abs(t - times[i]) > .06).Any()))
                    throw new Exception(
                        "Audible attack timing mismatch, analysis start " + phase + " s: " + string.Join(",", times)
                    );
                results.Add(new
                {
                    kind,
                    phase,
                    times
                });
            }
        }
        File.WriteAllText(
            Path.Combine(folder, "results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(
            "PASS: strike analysis fed like the game, 960-frame chunks from " + Phases.Length
                + " start points: silence, sustained note and 10 alternating bass/snare attacks; strike time error <=60 ms"
        );
    }
}
