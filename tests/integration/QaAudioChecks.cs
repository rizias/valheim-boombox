using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaAudioChecks
    {
        private static Type process = null!, source = null!, stream = null!, track = null!;
        private static readonly List<object> results = new();
        private static readonly HashSet<int> decoderIds = new();
        private static string ffmpeg = "", ffprobe = "", fixtures = "";
        private static object? Call(Type type, string method, object? target, params object?[] arguments)
        {
            try
            {
                return type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                ).Single(
                    m => m.Name == method && m.GetParameters().Length == arguments.Length
                ).Invoke(
                    target,
                    arguments
                );
            }
            catch (TargetInvocationException error)
            {
                throw error.InnerException!;
            }
        }
        private static object? Value(
            object target,
            string name
        ) => target.GetType().GetProperty(name)!.GetValue(target);
        private static void Check(bool passed, string name, object? observed = null)
        {
            results.Add(new
            {
                name,
                passed,
                observed
            });
            if (!passed)
                throw new IOException("R11 check failed: " + name + " / " + observed);
        }
        public static async Task Run(QaLedger ledger)
        {
            ledger.Preflight(32 * QaLedger.MiB);
            using var worker = new QaSupervised(
                ledger,
                "audio-checks",
                Environment.ProcessPath!,
                ledger.Project,
                new[] { Assembly.GetExecutingAssembly().Location, ledger.Project, "audio-worker" },
                QaLedger.MiB
            );
            int exit = await worker.WaitAsync(TimeSpan.FromSeconds(55));
            string output = File.ReadAllText(Path.Combine(ledger.Root, "current/audio-checks/stdout.log"));
            string line = output.Split('\n').LastOrDefault(s => s.StartsWith("R11_RESULT=", StringComparison.Ordinal))
                ?? throw new IOException("Audio worker result missing");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R11/audio-checks.json"),
                System.Text.Encoding.UTF8.GetBytes(line.Substring(11).Trim())
            );
            if (exit != 0)
                throw new IOException("Audio checks failed; inspect evidence/R11/audio-checks.json");
        }
        public static int Worker(string project)
        {
            string productPath = Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll");
            bool passed = false;
            string? failure = null;
            var before = Processes();
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    string name = new AssemblyName(args.Name).Name! + ".dll";
                    string path = Path.Combine(QaEnvironment.Managed, name);
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                };
                var product = Assembly.LoadFrom(productPath);
                process = product.GetType("VikingBoombox.MusicProcess")!;
                source = product.GetType("VikingBoombox.MusicLiveSource")!;
                stream = product.GetType("VikingBoombox.MusicLiveStream")!;
                track = product.GetType("VikingBoombox.MusicTrack")!;
                ffmpeg = QaEnvironment.Encoder;
                ffprobe = QaEnvironment.MediaProbe;
                fixtures = Path.Combine(BoomboxQa.QaEnvironment.FixturesRoot, "audio");
                Packets();
                Cancellation();
                StalledFeed();
                Buffers();
                Playback();
                EarlyCancellation();
                Check(!Processes().Except(before).Any(), "No new ffmpeg/ffprobe remains after checks", Processes());
                passed = true;
            }
            catch (Exception error)
            {
                failure = error.ToString();
            }
            Console.WriteLine(
                "R11_RESULT="
                    + JsonSerializer.Serialize(
                    new
                    {
                        passed,
                        failure,
                        sha256 = QaFiles.Hash(productPath),
                        decoderIds,
                        results,
                        scope = "Production PacketSeconds/Run and standalone stream lifecycle/sample checks. Unity callbacks/device reset/native150cycles/12h/AUDIO/PERF acceptance pending."
                    }
                )
            );
            return passed ? 0 : 1;
        }
        private static int[] Processes()
        {
            var ids = new List<int>();
            foreach (string name in new[] { "ffmpeg", "ffprobe" })
                foreach (var item in Process.GetProcessesByName(name))
                    using (item)
                        ids.Add(item.Id);
            return ids.ToArray();
        }
        private static double Packets(
            string path,
            Action<Stream>? input = null,
            string[]? options = null,
            CancellationToken token = default
        )
        => (double)Call(process, "PacketSeconds", null, path, input, options, ffprobe, token)!;
        private static object Locate(string uri, int? subsong = null)
        {
            var item = Activator.CreateInstance(track)!;
            track.GetField("id")!.SetValue(item, "r11");
            track.GetField("source")!.SetValue(item, uri);
            track.GetField("subsong")!.SetValue(item, subsong);
            return Call(source, "Locate", null, item, ffmpeg, ffprobe, CancellationToken.None)!;
        }
        private static void Packets()
        {
            string music = Path.Combine(fixtures, "Музыка с пробелами");
            string longFile = Path.Combine(fixtures, "long/long.flac");
            double seconds = Packets(longFile);
            Check(Math.Abs(seconds - 11220) < .05, "Production PacketSeconds folds long FLAC packet CSV", seconds);
            var zipped = Locate("unpack://zip|0|file://" + Path.Combine(music, "music.zip") + "|inside/album.cue", 2);
            Action<Stream> copy = destination => Call(source, "CopyTo", zipped, destination);
            seconds = Packets("album.flac", copy);
            Check(Math.Abs(seconds - 4) < .05, "Production PacketSeconds ZIP stdin", seconds);
            var wavpack = Locate("file://" + Path.Combine(music, "образ со ссылкой.iso.wv"), 2);
            var embedded = source.GetField(
                "WavPack",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!.GetValue(wavpack)!;
            long offset = (long)embedded.GetType().GetField("Start")!.GetValue(embedded)!;
            string[] options = (string[])Call(embedded.GetType(), "Input", embedded, offset)!;
            seconds = Packets((string)source.GetField("File")!.GetValue(wavpack)!, null, options);
            Check(
                Math.Abs(seconds - 20) < .05,
                "Production PacketSeconds WavPack embedded offset options",
                new
                {
                    offset,
                    seconds
                }
            );
            Action<Stream> raw = destination =>
            {
                using var pcm = File.OpenRead(Path.Combine(music, "source.wav"));
                pcm.Position = 44;
                pcm.CopyTo(destination);
            };
            seconds = Packets("raw", raw, new[] { "-f", "s16le", "-ar", "8000" });
            Check(Math.Abs(seconds - 4) < .05, "Production PacketSeconds unknown-duration raw PCM stdin", seconds);
            bool bounded = false;
            try
            {
                Call(
                    process,
                    "Run",
                    null,
                    ffprobe,
                    CancellationToken.None,
                    null,
                    new[] { "-v", "error", "-show_packets", "-of", "json", longFile }
                );
            }
            catch (InvalidDataException error)
            {
                bounded = error.Message.Contains("size limit", StringComparison.Ordinal);
            }
            Check(bounded, "Oversized real ffprobe metadata is drained and rejected");
            string text = (string)Call(
                process,
                "Run",
                null,
                ffprobe,
                CancellationToken.None,
                null,
                new[] {
                "-v",
                "trace",
                "-show_entries",
                "packet=size",
                "-of",
                "csv=p=0",
                Path.Combine(music, "176 кГц.wv")
                }
            )!;
            Check(text.Length > 0, "Real ffprobe verbose stderr drained without pipe deadlock", text.Length);
        }
        private static void Cancellation()
        {
            using var token = new CancellationTokenSource(150);
            Action<Stream> feed = destination =>
            {
                var block = new byte[8192];
                while (true)
                    destination.Write(block, 0, block.Length);
            };
            var watch = Stopwatch.StartNew();
            bool cancelled = false;
            try
            {
                Packets("pipe", feed, new[] { "-f", "s16le", "-ar", "48000" }, token.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            Check(
                cancelled && watch.ElapsedMilliseconds < 2500,
                "Cancel production probe with continuous stdin feed",
                watch.ElapsedMilliseconds
            );
        }
        private static object Create(
            object live,
            float seconds = 2,
            int minimum = 2048
        ) => Activator.CreateInstance(stream, live, seconds, minimum)!;
        private static void StalledFeed()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var token = new CancellationTokenSource();
            Action<Stream> feed = destination =>
            {
                entered.Set();
                release.Wait();
                destination.Write(new byte[1], 0, 1);
            };
            var task = Task.Run(() => Packets("raw", feed, new[] { "-f", "s16le", "-ar", "48000" }, token.Token));
            try
            {
                Check(entered.Wait(2000), "Controlled stalled input feed entered");
                token.Cancel();
                Thread.Sleep(2300);
                Check(!task.IsCompleted, "Owned preparation Task retains stalled feed until quiescence");
                release.Set();
                bool timeout = false;
                try
                {
                    Check(task.Wait(2000), "Stalled input Task completes after controlled release");
                }
                catch (AggregateException error)
                {
                    timeout = error.GetBaseException() is IOException
                    && error.GetBaseException().Message.Contains("shutdown limit", StringComparison.Ordinal);
                }
                Check(timeout, "Recovered feed timeout is reported instead of claiming successful cancellation");
            }
            finally
            {
                release.Set();
                try
                {
                    task.Wait(2500);
                }
                catch (AggregateException) { }
            }
        }
        private static void Dispose(object audio)
        {
            Call(stream, "Dispose", audio);
            Check(
                Value(audio, "CleanupError") == null,
                "Stream disposed with quiescent owned workers",
                Value(audio, "CleanupError")
            );
            var supervisor = (Thread)stream.GetField(
                "supervisor",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!.GetValue(audio)!;
            Check(!supervisor.IsAlive, "Supervisor joined on Dispose");
            var signal = (AutoResetEvent)stream.GetField(
                "supervise",
                BindingFlags.NonPublic | BindingFlags.Instance
            )!.GetValue(audio)!;
            Check(
                signal.SafeWaitHandle.IsClosed && (bool)Value(audio, "Stopped")!,
                "Owned supervisor signal closed after quiescence"
            );
        }
        private static void Buffers()
        {
            var live = Locate("file://" + Path.Combine(fixtures, "Музыка с пробелами/source.wav"));
            foreach (float seconds in new[] { 2, 0, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var audio = Create(live, seconds);
                try
                {
                    Check(
                        (long)Value(audio, "BufferBytes")! == 1843200,
                        "Default/nonfinite buffer remains bounded",
                        new
                        {
                            seconds = seconds.ToString(CultureInfo.InvariantCulture),
                            bytes = Value(audio, "BufferBytes")
                        }
                    );
                }
                finally
                {
                    Dispose(audio);
                }
            }
            var clamped = Create(live, float.MaxValue);
            try
            {
                Check(
                    (long)Value(clamped, "BufferBytes")! == 4147200,
                    "Eight-second buffer cap before allocation",
                    Value(clamped, "BufferBytes")
                );
            }
            finally
            {
                Dispose(clamped);
            }
            bool rejected = false;
            try
            {
                Create(live, 2, int.MaxValue);
            }
            catch (TargetInvocationException error)
            {
                rejected = error.InnerException is ArgumentOutOfRangeException;
            }
            Check(rejected, "Overflow-size Unity DSP minimum rejected before allocation");
        }
        private static void Ready(object audio)
        {
            var watch = Stopwatch.StartNew();
            while (!(bool)Value(audio, "Ready")!)
            {
                if (Value(audio, "Error") != null)
                    throw new IOException((string)Value(audio, "Error")!);
                if (watch.ElapsedMilliseconds > 5000)
                    throw new TimeoutException("Stream preparation deadline");
                Thread.Sleep(2);
            }
            var current = stream.GetField("worker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(audio);
            if (current?.GetType().GetField("Process")!.GetValue(current) is Process decoder)
                try
                {
                    decoderIds.Add(decoder.Id);
                }
                catch (InvalidOperationException) { }
        }
        private static float[] Reference(string path, double seconds)
        {
            var start = new ProcessStartInfo(
                ffmpeg
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in new[] {
                "-v",
                "error",
                "-i",
                path,
                "-t",
                (seconds + .02).ToString("0.000000", CultureInfo.InvariantCulture),
                "-ac",
                "2",
                "-ar",
                "48000",
                "-f",
                "f32le",
                "pipe:1"
            })
                start.ArgumentList.Add(argument);
            using var decoder = Process.Start(start)!;
            var diagnostic = Task.Run(() => (string)Call(process, "Drain", null, decoder.StandardError, 16384, false)!);
            var bytes = new byte[960 * 2 * sizeof(float)];
            try
            {
                long skip = (long)Math.Round(seconds * 48000) * 2 * sizeof(float);
                var discard = new byte[8192];
                while (skip > 0)
                {
                    int count = (int)Math.Min(skip, discard.Length);
                    decoder.StandardOutput.BaseStream.ReadExactly(discard, 0, count);
                    skip -= count;
                }
                decoder.StandardOutput.BaseStream.ReadExactly(bytes);
                if (!decoder.WaitForExit(2000) || !diagnostic.Wait(2000) || decoder.ExitCode != 0)
                    throw new IOException("Independent reference decoder failed");
                var samples = new float[1920];
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
                return samples;
            }
            catch (Exception error)
            {
                if (!decoder.HasExited)
                    decoder.Kill();
                decoder.WaitForExit(2000);
                diagnostic.Wait(2000);
                throw new IOException(
                    "Independent reference failed: " + (diagnostic.IsCompletedSuccessfully
                        ? diagnostic.Result
                        : "diagnostic timeout"),
                    error
                );
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
        private static void Playback()
        {
            string music = Path.Combine(fixtures, "Музыка с пробелами");
            foreach (var item in new[] {
                ("блоки.wv", (int?)null, "блоки.wv", 0d, 20d),
                ("album.cue", (int?)1, "album.flac", 0d, 2d),
                ("embedded.flac", (int?)2, "album.flac", 2d, 2d),
                ("music.zip", (int?)2, "album.flac", 2d, 2d)
            })
            {
                string uri = item.Item1 == "music.zip"
                    ? "unpack://zip|0|file://" + Path.Combine(music, item.Item1) + "|inside/album.cue"
                    : "file://" + Path.Combine(music, item.Item1);
                var live = Locate(uri, item.Item2);
                Check(
                    Math.Abs((double)source.GetField("Duration")!.GetValue(live)! - item.Item5) < .05,
                    "CUE/WV track duration preserved",
                    item.Item1
                );
                var audio = Create(live, .05f, 2048);
                try
                {
                    foreach (double at in new[] { 0d, item.Item5 - .4, .2, item.Item5 - .5, .1 })
                    {
                        long frame = (long)Math.Round(at * 48000);
                        Call(stream, "Begin", audio, frame);
                        Call(stream, "Seek", audio, frame);
                        var callback = Stopwatch.StartNew();
                        Call(stream, "Read", audio, new float[256]);
                        Check(
                            callback.ElapsedMilliseconds < 150,
                            "PCM callback not blocked by seek shutdown",
                            callback.ElapsedMilliseconds
                        );
                        Ready(audio);
                        var samples = new float[1920];
                        Check(
                            (bool)Call(stream, "ReadAt", audio, samples, frame)!,
                            "Prepared seek samples readable",
                            new
                            {
                                file = item.Item1,
                                at
                            }
                        );
                        float[] expected = Reference(Path.Combine(music, item.Item3), at + item.Item4);
                        double difference = samples.Zip(expected, (a, b) => Math.Abs((double)a - b)).Max();
                        Check(
                            difference < 1e-6,
                            "Samples match independent bounded ffmpeg reference",
                            new
                            {
                                file = item.Item1,
                                at,
                                difference
                            }
                        );
                    }
                    int launches = (int)Value(audio, "Launches")!;
                    for (int repeat = 0; repeat < 10; repeat++)
                        Ready(audio);
                    Check(
                        (int)Value(audio, "Launches")! == launches,
                        "Repeated readiness without action does not relaunch decoder",
                        item.Item1
                    );
                    long last = (long)Math.Round((item.Item5 - .02) * 48000);
                    Call(stream, "Seek", audio, last);
                    Ready(audio);
                    Call(stream, "Read", audio, new float[4096]);
                    var eof = Stopwatch.StartNew();
                    while ((long)Value(audio, "EofFrame")! < 0 && eof.ElapsedMilliseconds < 2000)
                        Thread.Sleep(2);
                    Check(
                        (long)Value(audio, "EofFrame")! == (long)Math.Round(item.Item5 * 48000),
                        "CUE/WV EOF stays at track boundary",
                        new
                        {
                            file = item.Item1,
                            frame = Value(audio, "EofFrame")
                        }
                    );
                }
                finally
                {
                    Dispose(audio);
                }
            }
        }
        private static void EarlyCancellation()
        {
            var live = Locate("file://" + Path.Combine(fixtures, "long/long.flac"));
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var audio = Create(live);
                Call(stream, "Begin", audio, 0L);
                var ignored = Value(audio, "Ready");
                if (attempt % 2 == 0)
                    Thread.Sleep(10);
                else
                {
                    Call(stream, "Seek", audio, 600L * 48000);
                    ignored = Value(audio, "Ready");
                    Call(stream, "Seek", audio, 1200L * 48000);
                    ignored = Value(audio, "Ready");
                }
                var watch = Stopwatch.StartNew();
                Dispose(audio);
                Check(
                    watch.ElapsedMilliseconds < 2250,
                    "Immediate decoder cancellation completed within bound",
                    watch.ElapsedMilliseconds
                );
                var output = new float[] { 1, 1, 1, 1 };
                Call(stream, "Read", audio, output);
                Check(output.All(sample => sample == 0), "Retired PCM callback returns silence");
            }
        }
    }
}
