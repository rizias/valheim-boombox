using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace VikingBoombox
{
    internal static class MusicProcess
    {
        private static readonly object Starting = new object();
        private static readonly Encoding NoPreamble = new UTF8Encoding(false);
        private const int MetadataCharacters = 1048576;
        internal const int DiagnosticCharacters = 16384;
        internal const int ShutdownMilliseconds = 2000;

        public static string Run(
            string executable,
            CancellationToken cancellation,
            Action<Stream> input,
            params string[] arguments
        )
        => Read(executable, cancellation, input, reader => Drain(reader, MetadataCharacters, true), arguments);

        private static T Read<T>(
            string executable,
            CancellationToken cancellation,
            Action<Stream> input,
            Func<TextReader, T> read,
            params string[] arguments
        )
        {
            cancellation.ThrowIfCancellationRequested();
            var info = new ProcessStartInfo(Resolve(executable), string.Join(" ", arguments.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input != null,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = input != null ? NoPreamble : null
            };
            var process = Start(info);
            {
                FeedHandle feed = null;
                Task<T> stdout = null;
                Task<string> stderr = null;
                bool attempted = false, quiet = false;
                try
                {
                    stdout = Task.Run(() => read(process.StandardOutput));
                    stderr = Task.Run(() => Drain(process.StandardError, DiagnosticCharacters));
                    Observe(stdout);
                    Observe(stderr);
                    if (input != null)
                        feed = Feed(process, input);
                    while (!process.WaitForExit(50))
                    {
                        if (!cancellation.IsCancellationRequested && !stdout.IsFaulted && feed?.Error == null)
                            continue;
                        Kill(process);
                        break;
                    }
                    attempted = true;
                    quiet = Finish(process, feed, stdout, stderr);
                    if (!quiet)
                        throw new IOException(BoomboxText.Get("error.process_stop_timeout"));
                    cancellation.ThrowIfCancellationRequested();
                    if (feed?.Error != null)
                        throw new InvalidDataException(feed.Error.Message);
                    if (stdout.IsFaulted)
                        throw stdout.Exception.GetBaseException();
                    if (stderr.IsFaulted)
                        throw stderr.Exception.GetBaseException();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException(Path.GetFileName(executable) + ": " + stderr.Result.Trim());
                    return stdout.Result;
                }
                finally
                {
                    if (!attempted)
                        quiet = Finish(process, feed, stdout, stderr);
                    if (!quiet)
                    {
                        while (!Finish(process, feed, 0, stdout, stderr))
                            Thread.Sleep(200);
                        process.Dispose();
                        throw new IOException(BoomboxText.Get("error.process_cleanup_timeout"));
                    }
                    process.Dispose();
                }
            }
        }

        public static JObject Probe(
            string path,
            string executable,
            CancellationToken cancellation
        ) => Probe(path, null, null, executable, cancellation);

        public static JObject Probe(
            string path,
            Action<Stream> input,
            string[] options,
            string executable,
            CancellationToken cancellation
        )
        {
            var data = JObject.Parse(Run(
                    executable,
                    cancellation,
                    input,
                    Quiet(options, "-show_format", "-show_streams", "-of", "json", input == null ? path : "pipe:0")
                ));
            if (!(data["streams"] as JArray)?.Any(s => (string)s["codec_type"] == "audio") ?? true)
                throw new InvalidDataException(BoomboxText.Get("text.the_file_contains_no_audio_stream") + path);
            return data;
        }

        public static double PacketSeconds(
            string path,
            Action<Stream> input,
            string[] options,
            string executable,
            CancellationToken cancellation
        )
        {
            return Read(
                executable,
                cancellation,
                input,
                FoldPackets,
                Quiet(
                    options,
                    "-select_streams",
                    "a:0",
                    "-show_entries",
                    "packet=pts_time,duration_time",
                    "-of",
                    "csv=p=0",
                    input == null ? path : "pipe:0"
                )
            );
        }

        private static double FoldPackets(TextReader reader)
        {
            double first = double.NaN, last = double.NegativeInfinity;
            var line = new StringBuilder(128);
            var block = new char[4096];
            bool oversized = false;
            Action fold = () =>
            {
                if (oversized)
                    return;
                string value = line.ToString();
                int comma = value.IndexOf(',');
                if (comma < 0 || !Finite(value.Substring(0, comma), out double pts))
                    return;
                int next = value.IndexOf(',', comma + 1);
                if (!Finite(
                    value.Substring(
                        comma + 1,
                        (next < 0 ? value.Length : next) - comma - 1
                    ),
                    out double duration
                ))
                    duration = 0;
                double finish = pts + duration;
                if (double.IsNaN(finish) || double.IsInfinity(finish))
                    return;
                if (double.IsNaN(first))
                    first = pts;
                last = Math.Max(last, finish);
            };
            int count;
            while ((count = reader.Read(block, 0, block.Length)) > 0)
                for (int index = 0; index < count; index++)
                {
                    if (block[index] == '\n')
                    {
                        fold();
                        line.Clear();
                        oversized = false;
                    }
                    else if (line.Length < 1024)
                        line.Append(block[index]);
                    else
                        oversized = true;
                }
            if (line.Length > 0)
                fold();
            double seconds = last - first;
            return double.IsNaN(first) || double.IsNaN(seconds) || double.IsInfinity(seconds)
                ? 0
                : Math.Max(0, seconds);
        }

        private static bool Finite(
            string text,
            out double value
        ) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value)
        && !double.IsInfinity(value);

        internal static string Drain(TextReader reader, int maximum, bool rejectTruncation = false)
        {
            var result = new StringBuilder(Math.Min(maximum, 4096));
            var block = new char[4096];
            bool truncated = false;
            int count;
            while ((count = reader.Read(block, 0, block.Length)) > 0)
            {
                int keep = Math.Min(count, maximum - result.Length);
                if (keep > 0)
                    result.Append(block, 0, keep);
                if (keep < count)
                    truncated = true;
            }
            if (truncated && rejectTruncation)
                throw new InvalidDataException(BoomboxText.Get("error.metadata_size_limit"));
            return result.ToString();
        }

        private static string[] Quiet(
            string[] options,
            params string[] arguments
        ) => new[] { "-v", "error" }.Concat(options ?? new string[0]).Concat(arguments).ToArray();

        public static Process Launch(string executable, bool input, params string[] arguments)
        {
            var info = new ProcessStartInfo(Resolve(executable), string.Join(" ", arguments.Select(Quote)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = input ? NoPreamble : null
            };
            return Start(info);
        }

        private static Process Start(ProcessStartInfo info)
        {
            lock (Starting)
                return Process.Start(info);
        }

        public static FeedHandle Feed(
            Process process,
            Action<Stream> input
        ) => new FeedHandle(process.StandardInput.BaseStream, input);

        internal static void Observe(Task task) => task.ContinueWith(
        done =>
            {
                var ignored = done.Exception;
            },
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default
        );

        internal static bool Finish(Process process, FeedHandle feed, params Task[] readers)
        => Finish(process, feed, ShutdownMilliseconds, readers);

        internal static bool Finish(Process process, FeedHandle feed, int milliseconds, params Task[] readers)
        {
            var watch = Stopwatch.StartNew();
            Kill(process);
            feed?.Close();
            bool stopped;
            try
            {
                stopped = process.WaitForExit(milliseconds);
            }
            catch (InvalidOperationException)
            {
                stopped = true;
            }
            bool fed = feed == null || feed.Join(Math.Max(0, milliseconds - (int)watch.ElapsedMilliseconds));
            bool drained = true;
            foreach (Task reader in readers)
            {
                if (reader == null)
                    continue;
                try
                {
                    drained &= reader.Wait(Math.Max(0, milliseconds - (int)watch.ElapsedMilliseconds));
                }
                catch (AggregateException) { }
            }
            return stopped && fed && drained;
        }

        public static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static string Resolve(string value)
        {
            string resolved = value;
            if (!File.Exists(resolved))
                resolved = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(p => Path.Combine(
                        p.Trim('"'),
                        value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? value
                        : value + ".exe"
                    ))
                .FirstOrDefault(File.Exists) ?? value;
            string shim = Path.ChangeExtension(resolved, ".shim");
            if (File.Exists(shim))
            {
                var match = Regex.Match(File.ReadAllText(shim), "^path\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline);
                if (match.Success && File.Exists(match.Groups[1].Value))
                    resolved = match.Groups[1].Value;
            }
            return resolved;
        }

        internal sealed class FeedHandle
        {
            private readonly FeedPipe stdin;
            private readonly Thread thread;
            private Exception error;
            public Exception Error => Volatile.Read(ref error);
            public FeedHandle(Stream pipe, Action<Stream> input)
            {
                stdin = new FeedPipe(pipe);
                thread = new Thread(() =>
                {
                    try
                    {
                        input(stdin);
                    }
                    catch (Exception) when (stdin.Closed) { }
                    catch (Exception failure)
                    {
                        Volatile.Write(ref error, failure);
                    }
                    finally
                    {
                        stdin.Dispose();
                    }
                })
                {
                    IsBackground = true,
                    Name = "Boombox music feed"
                };
                thread.Start();
            }
            public void Close() => stdin.Dispose();
            public bool Join(int milliseconds) => thread.Join(milliseconds);
        }

        private sealed class FeedPipe : Stream
        {
            private readonly Stream pipe;
            public FeedPipe(Stream pipe)
            {
                this.pipe = pipe;
            }
            private int closed;
            private int disposed;
            public bool Closed => Volatile.Read(ref closed) != 0;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Flush() => Guard(pipe.Flush);
            public override void Write(
                byte[] buffer,
                int offset,
                int count
            ) => Guard(() => pipe.Write(buffer, offset, count));

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Exchange(ref closed, 1);
                    if (Interlocked.Exchange(ref disposed, 1) == 0)
                        try
                        {
                            pipe.Dispose();
                        }
                        catch (IOException) { }
                }
                base.Dispose(disposing);
            }

            private void Guard(Action write)
            {
                if (Closed)
                    throw new ObjectDisposedException(nameof(FeedPipe));
                try
                {
                    write();
                }
                catch (Exception error) when (error is IOException || error is ObjectDisposedException)
                {
                    Interlocked.Exchange(ref closed, 1);
                    throw;
                }
            }
        }

        private static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                text.Append(c);
                slashes = 0;
            }
            return text.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
}
