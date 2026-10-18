using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VikingBoombox
{
    public sealed partial class MusicLiveStream
    {
        private sealed class DecodeWorker
        {
            public Thread Thread;
            public Process Process;
            public MusicProcess.FeedHandle Feed;
            public Task<string> Diagnostic;
            public bool Quiescent;
        }

        private void Launch(long position)
        {
            generation++;
            launchPosition = position;
            start = end = cursor = requested = position;
            eof = -1;
            launched = true;
            supervise.Set();
            Monitor.PulseAll(gate);
        }

        private void Supervise()
        {
            int observed = 0;
            try
            {
                while (true)
                {
                    int marker;
                    long position;
                    bool stop;
                    DecodeWorker previous;
                    lock (gate)
                    {
                        marker = generation;
                        position = launchPosition;
                        stop = disposed || error != null;
                        previous = worker;
                    }
                    if (previous != null && (stop || observed != marker || !previous.Thread.IsAlive))
                    {
                        if (!Stop(previous))
                        {
                            CleanupFailed(BoomboxText.Get("error.decoder_feed_shutdown_timeout"));
                            supervise.WaitOne(200);
                            continue;
                        }
                        lock (gate)
                        {
                            if (worker == previous)
                                worker = null;
                        }
                    }
                    DecodeWorker pending = null;
                    lock (gate)
                    {
                        if (disposed || error != null)
                            return;
                        if (marker != generation)
                            continue;
                        if (observed != marker)
                        {
                            pending = new DecodeWorker();
                            pending.Thread = new Thread(
                                () => Decode(pending, marker, position)
                            )
                            {
                                IsBackground = true,
                                Name = "Boombox music decoder"
                            };
                            worker = pending;
                            observed = marker;
                            launches++;
                        }
                    }
                    pending?.Thread.Start();
                    supervise.WaitOne();
                }
            }
            catch (Exception failure)
            {
                CleanupFailed(failure.Message);
            }
            finally
            {
                DecodeWorker remaining;
                lock (gate)
                    remaining = worker;
                while (remaining != null && !Stop(remaining))
                {
                    CleanupFailed(BoomboxText.Get("error.supervisor_cleanup_timeout"));
                    supervise.WaitOne(200);
                }
                lock (gate)
                {
                    worker = null;
                    supervisorStopped = true;
                    if (disposed)
                        CloseSignal();
                }
            }
        }

        private bool Stop(DecodeWorker previous)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Process decoder;
                MusicProcess.FeedHandle input;
                Task<string> diagnostic;
                bool quiet;
                lock (gate)
                {
                    decoder = previous.Process;
                    input = previous.Feed;
                    diagnostic = previous.Diagnostic;
                    quiet = previous.Quiescent;
                    Monitor.PulseAll(gate);
                }
                if (decoder != null && !quiet)
                    MusicProcess.Kill(decoder);
                if (!quiet)
                    input?.Close();
                if (previous.Thread.Join(20))
                {
                    if (quiet || decoder == null)
                        return true;
                    if (MusicProcess.Finish(
                        decoder,
                        input,
                        Math.Max(0, MusicProcess.ShutdownMilliseconds - (int)watch.ElapsedMilliseconds),
                        diagnostic
                    ))
                    {
                        decoder.Dispose();
                        lock (gate)
                            previous.Quiescent = true;
                        return true;
                    }
                }
                if (watch.ElapsedMilliseconds >= MusicProcess.ShutdownMilliseconds)
                    return false;
            }
        }

        private void Decode(DecodeWorker current, int marker, long position)
        {
            Process decoder = null;
            MusicProcess.FeedHandle inputFeed = null;
            Task<string> stderr = null;
            try
            {
                lock (gate)
                    if (disposed || marker != generation || error != null)
                        return;
                double at = source.Start + position / (double)(Channels * Frequency);
                string[] arguments = source.Decoding(at, out double from, out Action<Stream> input);
                decoder = MusicProcess.Launch(source.FFmpeg, input != null, arguments);
                lock (gate)
                    current.Process = decoder;
                stderr = Task.Run(() => MusicProcess.Drain(decoder.StandardError, MusicProcess.DiagnosticCharacters));
                MusicProcess.Observe(stderr);
                lock (gate)
                    current.Diagnostic = stderr;
                if (input != null)
                {
                    inputFeed = MusicProcess.Feed(decoder, input);
                    lock (gate)
                        current.Feed = inputFeed;
                }
                lock (gate)
                    if (disposed || marker != generation || error != null)
                        return;
                long skip = (long)Math.Round((at - from) * Frequency) * Channels;
                var output = decoder.StandardOutput.BaseStream;
                var bytes = new byte[65536];
                var samples = new float[bytes.Length / sizeof(float)];
                int carry = 0;
                while (true)
                {
                    int read = output.Read(bytes, carry, bytes.Length - carry);
                    if (read <= 0)
                        break;
                    int total = carry + read, count = total / (sizeof(float) * Channels) * Channels;
                    Buffer.BlockCopy(bytes, 0, samples, 0, count * sizeof(float));
                    carry = total - count * sizeof(float);
                    if (carry > 0)
                        Buffer.BlockCopy(bytes, count * sizeof(float), bytes, 0, carry);
                    if (skip > 0)
                    {
                        int drop = (int)Math.Min(skip, count);
                        Array.Copy(samples, drop, samples, 0, count - drop);
                        count -= drop;
                        skip -= drop;
                    }
                    if (count > 0 && !Store(marker, samples, count))
                        return;
                }
                if (!decoder.WaitForExit(MusicProcess.ShutdownMilliseconds))
                    throw new IOException(BoomboxText.Get("error.decoder_exit_timeout"));
                if (!MusicProcess.Finish(decoder, inputFeed, stderr))
                    throw new IOException(BoomboxText.Get("error.decoder_drain_timeout"));
                if (inputFeed?.Error != null)
                    throw new InvalidDataException(inputFeed.Error.Message);
                string message = stderr.IsFaulted ? stderr.Exception.GetBaseException().Message : stderr.Result.Trim();
                lock (gate)
                {
                    if (disposed || marker != generation)
                        return;
                    if (error == null && decoder.ExitCode != 0)
                        error = string.IsNullOrEmpty(message)
                            ? BoomboxText.Format("error.ffmpeg_exit", decoder.ExitCode)
                            : "ffmpeg: " + message;
                    if (error == null)
                        eof = end;
                    if (error == null && end > position)
                        known = end;
                    Monitor.PulseAll(gate);
                }
            }
            catch (Exception failure)
            {
                Fail(marker, failure.Message);
            }
            finally
            {
                bool quiet = decoder == null || MusicProcess.Finish(decoder, inputFeed, stderr);
                if (quiet)
                    decoder?.Dispose();
                lock (gate)
                    current.Quiescent = quiet;
                if (!quiet)
                    CleanupFailed(BoomboxText.Get("error.decoder_cleanup_timeout"));
                supervise.Set();
            }
        }

        private bool Store(int marker, float[] samples, int count)
        {
            lock (gate)
            {
                long limit = Length * Channels;
                int offset = 0;
                while (offset < count)
                {
                    while (!disposed && error == null && marker == generation && end - cursor >= ahead)
                        Monitor.Wait(gate, 200);
                    if (disposed || error != null || marker != generation)
                        return false;
                    if (end >= limit)
                    {
                        known = eof = end;
                        Monitor.PulseAll(gate);
                        return false;
                    }
                    int n = (int)Math.Min(Math.Min(count - offset, ahead - (end - cursor)), limit - end);
                    int index = (int)(end % ring.Length), first = Math.Min(n, ring.Length - index);
                    Array.Copy(samples, offset, ring, index, first);
                    if (n > first)
                        Array.Copy(samples, offset + first, ring, 0, n - first);
                    end += n;
                    offset += n;
                    start = Math.Max(start, end - ring.Length);
                    Monitor.PulseAll(gate);
                }
                return true;
            }
        }

        private void Fail(int marker, string message)
        {
            lock (gate)
            {
                if (disposed || marker != generation || error != null)
                    return;
                error = message;
                supervise.Set();
                Monitor.PulseAll(gate);
            }
        }

        private void CleanupFailed(string message)
        {
            lock (gate)
            {
                cleanupError = message;
                if (error == null)
                    error = message;
                Monitor.PulseAll(gate);
            }
        }
    }
}
