using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using VikingBoombox;

public static class StrikeChecks
{
    private const int Rate = 48000;
    private const int Chunk = 1024;
    private const double Tolerance = .02;

    public static int Main(string[] args)
    {
        string root = args[0], music = args[1], ffmpeg = args[2], work = args[3];
        Directory.CreateDirectory(work);
        string script = Path.Combine(root, "tests/offline/stand-strikes.mjs");
        using var tracks = JsonDocument.Parse(Node(script, "tracks"));
        bool passed = true;
        foreach (var track in tracks.RootElement.EnumerateArray())
        {
            string key = track.GetProperty("key").GetString();
            double start = track.GetProperty("startS").GetDouble();
            double? end = track.GetProperty("endS").ValueKind == JsonValueKind.Number
                ? track.GetProperty("endS").GetDouble()
                : null;
            string mono = Path.Combine(work, key.Replace(':', '-') + ".f32");
            File.WriteAllBytes(
                mono,
                MixDown(Decode(ffmpeg, Path.Combine(music, track.GetProperty("path").GetString()), start, end))
            );
            var mod = ModStrikes(mono);
            using var standJson = JsonDocument.Parse(Node(script, "strikes", mono, Rate.ToString()));
            var stand = standJson.RootElement.EnumerateArray().Select(
                item => (Time: item.GetProperty("timeS")
                    .GetDouble(), Strength: item.GetProperty("strength")
                    .GetDouble(), Loudest: item.GetProperty("loudest")
                    .GetString())
            )
                .ToList();
            var comparison = CompareStrikes(stand, mod);
            bool ok = comparison.Passed;
            passed &= ok;
            Console.WriteLine(
                (ok ? "PASS " : "FAIL ") + key + " stand " + stand.Count + " mod " + mod.Count + " paired "
                    + comparison.Matched
                    + " missing "
                    + (stand.Count - comparison.Matched)
                    + " extra "
                    + (mod.Count - comparison.Matched)
                    + " worst_time_ms "
                    + Math.Round(
                    comparison.WorstTime * 1000,
                    3
                )
                    + " worst_strength "
                    + Math.Round(comparison.WorstStrength, 5)
                    + " same_loudest "
                    + comparison.SameLoudest
            );
        }
        Console.WriteLine(passed ? "strike_checks_passed" : "strike_checks_failed");
        return passed ? 0 : 1;
    }

    internal static (bool Passed, int Matched, double WorstTime, double WorstStrength, int SameLoudest) CompareStrikes(
    IReadOnlyList<(double Time, double Strength, string Loudest)> stand, IReadOnlyList<MusicStrike> mod)
    {
        var expected = stand.OrderBy(strike => strike.Time).ToArray();
        var actual = mod.OrderBy(strike => strike.Time).ToArray();
        int s = 0, m = 0, matched = 0, sameLoudest = 0;
        double worstTime = 0, worstStrength = 0;
        while (s < expected.Length && m < actual.Length)
        {
            double delta = actual[m].Time - expected[s].Time;
            if (delta < -Tolerance)
            {
                m++;
                continue;
            }
            if (delta > Tolerance)
            {
                s++;
                continue;
            }
            matched++;
            worstTime = Math.Max(worstTime, Math.Abs(delta));
            worstStrength = Math.Max(worstStrength, Math.Abs(actual[m].Strength - expected[s].Strength));
            if (actual[m].Loudest.ToString().Equals(expected[s].Loudest, StringComparison.OrdinalIgnoreCase))
                sameLoudest++;
            s++;
            m++;
        }
        return (expected.Length > 0 && matched == expected.Length && matched == actual.Length,
        matched, worstTime, worstStrength, sameLoudest);
    }

    private static List<MusicStrike> ModStrikes(string mono)
    {
        var bytes = File.ReadAllBytes(mono);
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        var analyzer = new MusicSoundAnalyzer(Rate);
        var strikes = new List<MusicStrike>();
        var chunk = new float[Chunk];
        double until = double.NegativeInfinity;
        for (int start = 0; start < samples.Length; start += Chunk)
        {
            int count = Math.Min(Chunk, samples.Length - start);
            Array.Copy(samples, start, chunk, 0, count);
            analyzer.Push(start / (double)Rate, chunk, count);
            double settled = (start + count) / (double)Rate - MusicSoundAnalyzer.SettledSeconds;
            analyzer.StrikesIn(until, settled, strikes);
            until = Math.Max(until, settled);
        }
        analyzer.StrikesIn(until, double.PositiveInfinity, strikes);
        return strikes;
    }

    private static byte[] MixDown(float[] stereo)
    {
        var mono = new float[stereo.Length / 2];
        for (int index = 0; index < mono.Length; index++)
        {
            float mixed = (float)(0 + stereo[index * 2] / 2.0);
            mono[index] = (float)(mixed + stereo[index * 2 + 1] / 2.0);
        }
        var bytes = new byte[mono.Length * sizeof(float)];
        Buffer.BlockCopy(mono, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] Decode(string ffmpeg, string file, double start, double? end)
    {
        var info = new ProcessStartInfo(
            ffmpeg
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var arguments = new List<string> {
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostdin",
            "-ss",
            start.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (end.HasValue)
        {
            arguments.Add("-to");
            arguments.Add(end.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        arguments.AddRange(
            new[] { "-i", file, "-map", "0:a:0", "-vn", "-ac", "2", "-ar", Rate.ToString(), "-f", "f32le", "pipe:1" }
        );
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info);
        var error = process.StandardError.ReadToEndAsync();
        using var memory = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(memory);
        process.WaitForExit();
        if (process.ExitCode != 0 || memory.Length == 0)
            throw new InvalidOperationException("decode of " + file + ": " + error.Result);
        var samples = new float[memory.Length / sizeof(float)];
        Buffer.BlockCopy(memory.GetBuffer(), 0, samples, 0, samples.Length * sizeof(float));
        return samples;
    }

    private static string Node(string script, params string[] arguments)
    {
        var info = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        info.ArgumentList.Add(script);
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info);
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("node " + string.Join(" ", arguments) + ": " + error.Result);
        return output;
    }
}
