using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaMembraneChecks
    {
        private const int Rate = 48000, Step = 960;
        private static Type stream = null!, source = null!, track = null!, meter = null!;
        private static string ffmpeg = "", ffprobe = "", encoder = "", folder = "";
        private static readonly List<object> results = new();
        private static object? Call(Type type, string name, object? target, params object?[] args)
        {
            try
            {
                return type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance
                ).Single(
                    m => m.Name == name && m.GetParameters().Length == args.Length
                ).Invoke(
                    target,
                    args
                );
            }
            catch (TargetInvocationException error)
            {
                throw error.InnerException!;
            }
        }
        private static object? Value(
            object instance,
            string name
        ) => instance.GetType().GetProperty(name)?.GetValue(instance)
            ?? instance.GetType().GetField(name)?.GetValue(instance);
        private static void Check(bool passed, string name, object? observed = null)
        {
            results.Add(new
            {
                name,
                passed,
                observed
            });
            if (!passed)
                throw new IOException("Membrane check failed: " + name);
        }
        public static async Task Run(QaLedger ledger, bool control)
        {
            ledger.Preflight(16 * QaLedger.MiB);
            string fixtures = Path.Combine(ledger.Root, "current/membrane-fixtures");
            Directory.CreateDirectory(fixtures);
            var files = new[] { "envelope.flac", "quiet.flac", "future.flac" }.Select(n => Path.Combine(fixtures, n))
                .ToArray();
            if (control)
                foreach (string file in files)
                    ledger.ReserveFile(file, 2 * QaLedger.MiB);
            string mode = control ? "control" : "fixed", role = "membrane-" + mode;
            using var worker = new QaSupervised(
                ledger,
                role,
                Environment.ProcessPath!,
                ledger.Project,
                new[] { Assembly.GetExecutingAssembly().Location, ledger.Project, "membrane-worker", mode },
                QaLedger.MiB
            );
            int exit = await worker.WaitAsync(TimeSpan.FromSeconds(55));
            if (control)
                foreach (string file in files)
                {
                    if (File.Exists(file))
                        ledger.CommitFile(file);
                    else
                        ledger.RemoveOwned(file);
                }
            string line = File.ReadAllText(Path.Combine(ledger.Root, "current", role, "stdout.log")).Split('\n').Last(
                s => s.StartsWith("MEMBRANE_RESULT=", StringComparison.Ordinal)
            );
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R11", "membrane-" + mode + ".json"),
                System.Text.Encoding.UTF8.GetBytes(line.Substring(16).Trim())
            );
            if (exit != 0)
                throw new IOException("Addressed membrane worker failed");
            if (!control)
                await CompileLegacy(ledger);
        }
        private static async Task CompileLegacy(QaLedger ledger)
        {
            string output = Path.Combine(QaEnvironment.BinRoot, "membrane-legacy.dll"),
                response = Path.Combine(QaEnvironment.BinRoot, "membrane-legacy.rsp");
            var args = new List<string> {
                "/nologo",
                "/target:library",
                "/nostdlib+",
                "/langversion:latest",
                "/out:" + output,
                "/reference:" + Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")
            };
            args.AddRange(Directory.GetFiles(QaEnvironment.References, "*.dll").Select(p => "/reference:" + p));
            args.AddRange(
                new[] { "NativeMusicChecks.cs", "MembraneChecks.cs", "LongTrackChecks.cs" }.Select(
                    n => Path.Combine(ledger.Project, "tests/offline", n)
                )
            );
            args.Add(Path.Combine(QaEnvironment.SourceRoot(ledger.Project), "QaEnvironment.cs"));
            QaFiles.Write(
                ledger,
                response,
                System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, args.Select(a => "\"" + a + "\"")))
            );
            ledger.ReserveFile(output, QaLedger.MiB);
            using var compile = new QaSupervised(
                ledger,
                "membrane-legacy-build",
                Environment.ProcessPath!,
                ledger.Project,
                new[] { QaEnvironment.Compiler, "@" + response },
                QaLedger.MiB
            );
            int exit = await compile.WaitAsync(TimeSpan.FromSeconds(30));
            if (File.Exists(output))
                ledger.CommitFile(output);
            else
                ledger.RemoveOwned(output);
            if (exit != 0)
                throw new IOException("Literal updated MembraneChecks compile failed");
            Console.WriteLine("QA_LITERAL_MEMBRANE_COMPILE_PASS no legacy runner executed");
            string folder = Path.Combine(ledger.Root, "current/membrane-literal");
            Directory.CreateDirectory(folder);
            string wave = Path.Combine(folder, "membranes.wav");
            ledger.ReserveFile(wave, 44 + 11L * Rate * 4);
            using var worker = new QaSupervised(
                ledger,
                "membrane-literal",
                Environment.ProcessPath!,
                ledger.Project,
                new[] { Assembly.GetExecutingAssembly().Location, ledger.Project, "membrane-literal" },
                QaLedger.MiB
            );
            int ran = await worker.WaitAsync(TimeSpan.FromSeconds(55));
            if (File.Exists(wave))
                ledger.CommitFile(wave);
            else
                ledger.RemoveOwned(wave);
            string line = File.ReadAllText(Path.Combine(folder, "stdout.log")).Split('\n').Last(
                s => s.StartsWith("LITERAL_MEMBRANE_RESULT=", StringComparison.Ordinal)
            );
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R11/membrane-literal.json"),
                System.Text.Encoding.UTF8.GetBytes(line.Substring(24).Trim())
            );
            if (ran != 0)
                throw new IOException("Actual literal Membranes function failed");
        }
        public static int Literal(string project)
        {
            object? observed = null;
            string? error = null;
            bool passed = false;
            int assertions = 0;
            string product = Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll");
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    string path = Path.Combine(QaEnvironment.Managed, new AssemblyName(args.Name).Name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                };
                Assembly.LoadFrom(product);
                var type = Assembly.LoadFrom(Path.Combine(QaEnvironment.BinRoot, "membrane-legacy.dll")).GetType(
                    "NativeMusicChecks"
                )!;
                observed = type.GetMethod("Membranes", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(
                    null,
                    new object[] {
                    QaEnvironment.Encoder,
                    QaEnvironment.MediaProbe,
                    Path.Combine(QaEnvironment.QaRoot, "current/membrane-literal")
                    }
                );
                assertions = (int)type.GetField(
                    "checks",
                    BindingFlags.NonPublic | BindingFlags.Static
                )!.GetValue(null)!;
                passed = true;
            }
            catch (Exception failure)
            {
                error = failure is TargetInvocationException invocation
                    ? invocation.InnerException!.ToString()
                    : failure.ToString();
            }
            Console.WriteLine(
                "LITERAL_MEMBRANE_RESULT="
                    + JsonSerializer.Serialize(
                    new
                    {
                        passed,
                        assertions,
                        error,
                        observed,
                        candidateSha = QaFiles.Hash(product),
                        scope = "Actual updated private NativeMusicChecks.Membranes executed alone with bounded reserved 11s WAV input; old main/prep not executed. GameMusic native case pending."
                    }
                )
            );
            return passed ? 0 : 1;
        }
        public static int Worker(string project, string mode)
        {
            string productPath = Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll");
            bool passed = false;
            string? error = null;
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    string path = Path.Combine(QaEnvironment.Managed, new AssemblyName(args.Name).Name + ".dll");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                };
                var product = Assembly.LoadFrom(productPath);
                stream = product.GetType("VikingBoombox.MusicLiveStream")!;
                source = product.GetType("VikingBoombox.MusicLiveSource")!;
                track = product.GetType("VikingBoombox.MusicTrack")!;
                meter = product.GetType("VikingBoombox.BoomboxMembraneMeter")!;
                ffmpeg = QaEnvironment.Encoder;
                ffprobe = QaEnvironment.MediaProbe;
                folder = Path.Combine(QaEnvironment.QaRoot, "current/membrane-fixtures");
                encoder = QaEnvironment.Encoder;
                if (mode == "control")
                    Fixtures();
                var twin = Twins();
                var observedTwin = new
                {
                    twin.quiet,
                    twin.future,
                    twin.expected,
                    twin.difference
                };
                if (mode == "control")
                    Check(twin.difference > .01, "Old production meter fails future-causality control", observedTwin);
                else
                {
                    Check(
                        twin.difference < 2e-6,
                        "Identical heard past is invariant to different future",
                        observedTwin
                    );
                    Check(
                        Math.Abs(twin.quiet - twin.expected) < 2e-6 && Math.Abs(twin.future - twin.expected) < 2e-6,
                        "Twin production Travel matches independent causal PCM oracle",
                        observedTwin
                    );
                    Cadences();
                    SeekHistory();
                    ReadLimits();
                }
                Check(
                    Process.GetProcessesByName("ffmpeg").Length == 0
                        && Process.GetProcessesByName("ffprobe").Length == 0,
                    "Owned decoders quiescent after checks"
                );
                passed = true;
            }
            catch (Exception failure)
            {
                error = failure.ToString();
            }
            Console.WriteLine(
                "MEMBRANE_RESULT="
                    + JsonSerializer.Serialize(
                    new
                    {
                        passed,
                        mode,
                        error,
                        candidateSha = QaFiles.Hash(productPath),
                        results,
                        fixtures = Directory.GetFiles(folder).Select(
                        p => new
                        {
                            name = Path.GetFileName(
                            p
                        ),
                            bytes = new FileInfo(
                            p
                        ).Length,
                            sha = QaFiles.Hash(
                            p
                        )
                        }
                    )
                            .ToArray(),
                        scope = "Production ffmpeg/MusicLiveStream/MeasureWave causal Travel; independent quantized-PCM closed-form lowpass oracle. GameMusic/native full AUDIO/PERF/12h unverified."
                    }
                )
            );
            return passed ? 0 : 1;
        }
        private static Process Start(params string[] args) => Launch(ffmpeg, args);
        private static Process Launch(string executable, params string[] args)
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            foreach (string arg in args)
                info.ArgumentList.Add(arg);
            return Process.Start(info)!;
        }
        private static Task<string> Diagnostics(Process decoder) => Task.Run(() =>
        {
            var result = new System.Text.StringBuilder();
            var block = new char[4096];
            int count;
            while ((count = decoder.StandardError.Read(block, 0, block.Length)) > 0)
            {
                int keep = Math.Min(count, 16384 - result.Length);
                if (keep > 0)
                    result.Append(block, 0, keep);
            }
            return result.ToString();
        });
        private static void Fixtures()
        {
            double beta = Math.Exp(-2 * Math.PI * 180 / Rate), omega = 2 * Math.PI / 512;
            double gain = (1 - beta) / Math.Sqrt(1 + beta * beta - 2 * beta * Math.Cos(omega)) / Math.Sqrt(2);
            foreach (var item in new[] { ("envelope.flac", 11), ("quiet.flac", 1), ("future.flac", 1) })
            {
                using var decoder = Launch(
                    encoder,
                    "-v",
                    "error",
                    "-f",
                    "s16le",
                    "-ar",
                    "48000",
                    "-ac",
                    "2",
                    "-i",
                    "pipe:0",
                    "-c:a",
                    "flac",
                    "-f",
                    "flac",
                    "pipe:1"
                );
                var diagnostic = Diagnostics(decoder);
                var input = Task.Run(() =>
                {
                    using var writer = new BinaryWriter(decoder.StandardInput.BaseStream);
                    for (int frame = 0; frame < Rate * item.Item2; frame++)
                    {
                        double amplitude = item.Item1 == "envelope.flac"
                            ? frame < Rate / 2
                                ? .8
                                : frame < Rate * 8
                                    ? .1
                                    : frame < Rate * 19 / 2
                                        ? .0115 / gain
                                        : .0095 / gain
                            : item.Item1 == "future.flac" && frame > Rate / 4
                                ? .75
                                : .04;
                        short value = (short)(Math.Sin(2 * Math.PI * frame / 512) * amplitude * 32767);
                        writer.Write(value);
                        writer.Write(value);
                    }
                });
                try
                {
                    using var output = new FileStream(
                        Path.Combine(folder, item.Item1),
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None
                    );
                    var block = new byte[8192];
                    int count;
                    while ((count = decoder.StandardOutput.BaseStream.Read(block, 0, block.Length)) > 0)
                    {
                        if (output.Length + count > 2 * QaLedger.MiB)
                            throw new IOException("Compressed fixture byte cap");
                        output.Write(block, 0, count);
                    }
                    output.Flush(true);
                    decoder.WaitForExit(2000);
                    diagnostic.Wait(2000);
                    try
                    {
                        if (!input.Wait(2000) || decoder.ExitCode != 0)
                            throw new IOException("Fixture encode failed: " + diagnostic.Result);
                    }
                    catch (AggregateException failure)
                    {
                        throw new IOException("Fixture encoder: " + diagnostic.Result, failure);
                    }
                }
                finally
                {
                    if (!decoder.HasExited)
                    {
                        decoder.Kill();
                        decoder.WaitForExit(2000);
                    }
                }
            }
        }
        private static float[] Decode(string file, int frames)
        {
            using var decoder = Start("-v", "error", "-i", file, "-ac", "2", "-ar", "48000", "-f", "f32le", "pipe:1");
            decoder.StandardInput.Close();
            var diagnostic = Diagnostics(decoder);
            var bytes = new byte[checked(frames * 2 * 4)];
            try
            {
                decoder.StandardOutput.BaseStream.ReadExactly(bytes);
                if (!decoder.WaitForExit(2000) || !diagnostic.Wait(2000) || decoder.ExitCode != 0)
                    throw new IOException("Independent decode failed");
                var pcm = new float[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, pcm, 0, bytes.Length);
                return pcm;
            }
            finally
            {
                if (!decoder.HasExited)
                {
                    decoder.Kill();
                    decoder.WaitForExit(2000);
                }
            }
        }
        private static object Open(string name)
        {
            var item = Activator.CreateInstance(track)!;
            track.GetField("id")!.SetValue(item, name);
            track.GetField("source")!.SetValue(item, "file://" + Path.Combine(folder, name));
            var live = Call(source, "Locate", null, item, ffmpeg, ffprobe, CancellationToken.None)!;
            var audio = Activator.CreateInstance(stream, live, 2f, 2048)!;
            Call(stream, "Begin", audio, 0L);
            Ready(audio);
            return audio;
        }
        private static void Ready(object audio)
        {
            var watch = Stopwatch.StartNew();
            while (!(bool)Value(audio, "Ready")!)
            {
                if (Value(audio, "Error") != null)
                    throw new IOException((string)Value(audio, "Error")!);
                if (watch.ElapsedMilliseconds > 5000)
                    throw new TimeoutException("Stream ready");
                Thread.Sleep(2);
            }
        }
        private static void Retire(object audio)
        {
            Call(stream, "Dispose", audio);
            Check(
                (bool)Value(audio, "Stopped")! && Value(audio, "CleanupError") == null,
                "Stream/owned supervisor released"
            );
        }
        private static void Advance(object audio, ref int played, int target)
        {
            while (played < target)
            {
                int frames = Math.Min(2048, target - played);
                Call(stream, "Read", audio, new float[frames * 2]);
                played += frames;
            }
            Ready(audio);
        }
        private static double Travel(object instance, object audio, int frame)
        {
            Call(meter, "MeasureWave", instance, audio, (long)frame);
            return (float)Value(instance, "Travel")!;
        }
        private static (double quiet, double future, double expected, double difference) Twins()
        {
            int frame = Rate / 4;
            var values = new double[2];
            for (int index = 0; index < 2; index++)
            {
                var audio = Open(index == 0 ? "quiet.flac" : "future.flac");
                try
                {
                    var unit = Activator.CreateInstance(meter)!;
                    Call(meter, "Forget", unit);
                    Travel(unit, audio, 0);
                    int played = 0;
                    Advance(audio, ref played, frame);
                    values[index] = Travel(unit, audio, frame);
                }
                finally
                {
                    Retire(audio);
                }
            }
            var oracle = new Oracle(Decode(Path.Combine(folder, "quiet.flac"), Rate));
            return (values[0], values[1], oracle.At(frame, 0), Math.Abs(values[0] - values[1]));
        }
        private static void Cadences()
        {
            var oracle = new Oracle(Decode(Path.Combine(folder, "envelope.flac"), 11 * Rate));
            var common = new Dictionary<int, double>();
            foreach (int fps in new[] { 20, 60, 144 })
            {
                var audio = Open("envelope.flac");
                try
                {
                    var unit = Activator.CreateInstance(meter)!;
                    Call(meter, "Forget", unit);
                    Travel(unit, audio, 0);
                    int played = 0;
                    double worst = 0, fpsDifference = 0, worstLevel = 0;
                    for (int tick = 1; tick <= fps * 43 / 4; tick++)
                    {
                        int frame = (int)Math.Round(tick * Rate / (double)fps);
                        Advance(audio, ref played, frame);
                        Call(meter, "Measure", unit, audio, (long)frame, 2, Rate, (long)Value(audio, "Length")!);
                        double actual = Travel(unit, audio, frame), expected = oracle.At(frame, 0);
                        worst = Math.Max(worst, Math.Abs(actual - expected));
                        worstLevel = Math.Max(
                            worstLevel,
                            Math.Abs(
                                (float)Value(unit, "Level")! - oracle.LevelAt(frame, (long)Value(audio, "Length")!)
                            )
                        );
                        if (frame % (Rate / 4) == 0)
                        {
                            if (common.TryGetValue(frame, out double previous))
                                fpsDifference = Math.Max(fpsDifference, Math.Abs(actual - previous));
                            else
                                common[frame] = actual;
                        }
                    }
                    Check(
                        worst < 3e-6 && fpsDifference < 3e-6,
                        "Independent causal oracle and common-quarter frames at " + fps + "fps",
                        new
                        {
                            worst,
                            fpsDifference
                        }
                    );
                    Check(
                        worstLevel < 3e-6,
                        "Unchanged Level matches independent forward-window PCM RMS at " + fps + "fps",
                        new
                        {
                            worstLevel
                        }
                    );
                    Call(meter, "Forget", unit);
                    Check((float)Value(unit, "Travel")! == 0, "Forget clears waveform history");
                    int frameAt = played;
                    double fresh = Travel(unit, audio, frameAt);
                    Check(
                        Math.Abs(fresh - oracle.At(frameAt, frameAt / Step)) < 3e-6,
                        "Fresh Forget peak starts with current partial bin",
                        fresh
                    );
                }
                finally
                {
                    Retire(audio);
                }
            }
            Check(
                oracle.Rms(8 * Rate + Rate / 4) > .01 && oracle.Rms(10 * Rate) < .01 && oracle.At(10 * Rate, 0) == 0,
                "Quantized fixture crosses independent silence threshold",
                new
                {
                    above = oracle.Rms(8 * Rate + Rate / 4),
                    below = oracle.Rms(10 * Rate)
                }
            );
        }
        private static void SeekHistory()
        {
            var oracle = new Oracle(Decode(Path.Combine(folder, "envelope.flac"), 11 * Rate));
            var audio = Open("envelope.flac");
            try
            {
                var unit = Activator.CreateInstance(meter)!;
                Call(meter, "Forget", unit);
                Travel(unit, audio, 0);
                int played = 0;
                Advance(audio, ref played, Rate * 2 / 5);
                Travel(unit, audio, played);
                int seek = Rate * 34 / 10;
                Call(stream, "Seek", audio, (long)seek);
                Ready(audio);
                Call(meter, "Forget", unit);
                Travel(unit, audio, seek);
                played = seek;
                int frame = Rate * 346 / 100;
                Advance(audio, ref played, frame);
                double actual = Travel(unit, audio, frame), expected = oracle.At(frame, seek / Step + 1);
                Check(
                    actual > .1 && Math.Abs(actual - expected) < 3e-6,
                    "Fresh actual seek clears old loud peak without stale partial bins",
                    new
                    {
                        actual,
                        expected
                    }
                );
            }
            finally
            {
                Retire(audio);
            }
        }
        private static void ReadLimits()
        {
            var audio = Open("quiet.flac");
            try
            {
                int played = 0;
                Advance(audio, ref played, Rate);
                var block = Enumerable.Repeat(17f, 2944).ToArray();
                Check(
                    (bool)Call(stream, "ReadAt", audio, block, (long)Rate - 1, 2)!,
                    "Bounded ReadAt succeeds for last stereo frame at EOF"
                );
                Check(
                    block.Skip(2).All(v => v == 17f) && !(bool)Call(stream, "ReadAt", audio, block, (long)Rate - 1)!,
                    "Bounded ReadAt leaves unused tail and two-arg EOF contract unchanged"
                );
            }
            finally
            {
                Retire(audio);
            }
        }
        private sealed class Oracle
        {
            private readonly double[][] y;
            private readonly double[] powers, bins;
            public Oracle(float[] pcm)
            {
                int frames = pcm.Length / 2;
                y = new[] { new double[frames + 1], new double[frames + 1] };
                powers = new double[1473];
                double beta = Math.Exp(-2 * Math.PI * 180 / Rate);
                powers[0] = 1;
                for (int n = 1; n < powers.Length; n++)
                    powers[n] = powers[n - 1] * beta;
                for (int n = 0; n < frames; n++)
                    for (int c = 0; c < 2; c++)
                        y[c][n + 1] = beta * y[c][n] + (1 - beta) * pcm[n * 2 + c];
                bins = new double[(frames + Step - 1) / Step];
                for (int k = 0; k < bins.Length; k++)
                    bins[k] = Peak(k, Math.Min(frames - 1, (k + 1) * Step - 1));
            }
            private double Filter(int c, int first, int n) => y[c][n + 1] - powers[n - first + 1] * y[c][first];
            private double Peak(int bin, int end)
            {
                int begin = bin * Step, first = Math.Max(0, begin - 512);
                double peak = 0;
                for (int n = begin; n <= end; n++)
                    peak = Math.Max(peak, Math.Abs((Filter(0, first, n) + Filter(1, first, n)) / 2));
                return peak;
            }
            public double At(int frame, int origin)
            {
                if (frame < 511)
                    return 0;
                int first = frame - 511, bin = frame / Step;
                double energy = 0;
                for (int n = first; n <= frame; n++)
                    for (int c = 0; c < 2; c++)
                    {
                        double value = Filter(c, first, n);
                        energy += value * value;
                    }
                double rms = Math.Sqrt(energy / 1024), peak = Peak(bin, frame);
                for (int k = Math.Max(origin, bin - 149); k < bin; k++)
                    peak = Math.Max(peak, bins[k]);
                double last = (Filter(0, first, frame) + Filter(1, first, frame)) / 2;
                return rms < .01 || peak <= 0 ? 0 : Math.Min(1, Math.Abs(last) / peak);
            }
            public double Rms(int frame)
            {
                int first = frame - 511;
                double energy = 0;
                for (int n = first; n <= frame; n++)
                    for (int c = 0; c < 2; c++)
                    {
                        double value = Filter(c, first, n);
                        energy += value * value;
                    }
                return Math.Sqrt(energy / 1024);
            }
            public double LevelAt(int frame, long length)
            {
                long offset = Math.Min((long)(frame / Step) * Step, Math.Max(0, length - 512));
                if (offset < 0 || offset + 512 >= y[0].Length)
                    return 0;
                int first = (int)offset;
                double energy = 0;
                for (int n = first; n < first + 512; n++)
                    for (int c = 0; c < 2; c++)
                    {
                        double value = Filter(c, first, n);
                        energy += value * value;
                    }
                return Math.Sqrt(energy / 1024);
            }
        }
    }
}
