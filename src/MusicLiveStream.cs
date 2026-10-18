using System;
using System.Diagnostics;
using System.Threading;

namespace VikingBoombox
{
    public sealed partial class MusicLiveStream : IDisposable
    {
        public const int Frequency = 48000;
        public const int Channels = 2;
        public const int MaxFrames = 536870911;
        private const long Unknown = long.MaxValue / Channels;
        private const double HistorySeconds = 2;
        private const double UnityReadSeconds = .8;
        private const double ReachSeconds = 1;
        private const double JitterSeconds = .001;
        private const int UnderrunWaitMilliseconds = 60;
        private const int WaitStepMilliseconds = 10;
        private const int MaximumBufferSeconds = 8;
        public readonly int Frames;
        public readonly bool Looping;
        public readonly long Length;
        private readonly MusicLiveSource source;
        private readonly object gate = new object();
        private readonly float[] ring;
        private readonly long ahead;
        private readonly long prebuffer;
        private long start;
        private long end;
        private long cursor;
        private long requested;
        private long eof = -1;
        private long known = -1;
        private long anchor;
        private int generation;
        private int launches;
        private int underruns;
        private bool launched;
        private bool begun;
        private readonly AutoResetEvent supervise;
        private readonly Thread supervisor;
        private DecodeWorker worker;
        private long launchPosition;
        private string error;
        private string cleanupError;
        private bool signalClosed;
        private bool supervisorStopped;
        private bool disposed;

        public MusicLiveStream(MusicLiveSource source, float bufferSeconds, int minimumFrames)
        {
            this.source = source;
            Length = source.End.HasValue ? FramesOf(source.Duration) : Unknown;
            Looping = Length > MaxFrames;
            Frames = Looping ? MaxFrames : (int)Length;
            if (minimumFrames < 0 || minimumFrames > MaximumBufferSeconds * Frequency)
                throw new ArgumentOutOfRangeException(
                    nameof(minimumFrames),
                    BoomboxText.Get("error.audio_buffer_limit")
                );
            double seconds = bufferSeconds > 0 && !float.IsInfinity(bufferSeconds)
                ? Math.Min(MaximumBufferSeconds, bufferSeconds)
                : 2;
            long minimum = (long)Math.Max(1, minimumFrames) * Channels;
            ahead = Math.Max(minimum, (long)Math.Ceiling(seconds * Frequency) * Channels);
            prebuffer = Math.Min(ahead, Math.Max(minimum, (long)(UnityReadSeconds * Frequency) * Channels));
            ring = new float[checked(ahead + (long)((HistorySeconds + UnityReadSeconds) * Frequency) * Channels)];
            supervise = new AutoResetEvent(false);
            supervisor = new Thread(Supervise) { IsBackground = true, Name = "Boombox music supervisor" };
            try
            {
                supervisor.Start();
            }
            catch
            {
                supervise.Dispose();
                throw;
            }
        }

        public void Begin(long frame)
        {
            lock (gate)
            {
                if (disposed)
                    return;
                Move(frame);
                begun = true;
                Monitor.PulseAll(gate);
            }
        }

        public void Hold()
        {
            lock (gate)
            {
                if (!disposed)
                    begun = false;
            }
        }

        public void Seek(long frame)
        {
            lock (gate)
            {
                if (disposed)
                    return;
                Move(frame);
                Monitor.PulseAll(gate);
            }
        }

        public long Track(int frame)
        {
            lock (gate)
                return anchor = Nearest(frame);
        }

        public int Clip(long frame) => (int)(Looping ? frame % Frames : frame);

        public static long FramesOf(double seconds)
        {
            double frames = Math.Round(seconds * Frequency);
            return frames >= Unknown ? Unknown : (long)Math.Max(1, frames);
        }
        public long BufferBytes => ring.LongLength * sizeof(float);
        public string Error
        {
            get
            {
                lock (gate)
                    return error;
            }
        }
        public string CleanupError
        {
            get
            {
                lock (gate)
                    return cleanupError;
            }
        }
        public bool Stopped
        {
            get
            {
                lock (gate)
                    return supervisorStopped;
            }
        }
        public int Launches
        {
            get
            {
                lock (gate)
                    return launches;
            }
        }
        public int Underruns
        {
            get
            {
                lock (gate)
                    return underruns;
            }
        }
        public long EofFrame
        {
            get
            {
                lock (gate)
                    return eof < 0 ? -1 : eof / Channels;
            }
        }
        public bool EndKnown
        {
            get
            {
                lock (gate)
                    return known >= 0 || Length < Unknown;
            }
        }
        public double Seconds
        {
            get
            {
                lock (gate)
                    return known < 0 ? source.Duration : known / (double)(Channels * Frequency);
            }
        }

        public bool Ready
        {
            get
            {
                lock (gate)
                {
                    if (disposed || error != null)
                        return false;
                    Apply();
                    long need = Math.Min(prebuffer, Length * Channels - cursor);
                    return end - cursor >= need || eof >= 0;
                }
            }
        }

        public void SetPosition(int frame)
        {
            lock (gate)
            {
                if (disposed)
                    return;
                requested = Clamp(Nearest(frame));
                Monitor.PulseAll(gate);
            }
        }

        public void Read(float[] output)
        {
            lock (gate)
            {
                Array.Clear(output, 0, output.Length);
                if (disposed || error != null || !begun)
                    return;
                Apply();
                int marker = generation;
                long lap = (long)Frames * Channels, limit = Math.Min(
                    Length * Channels,
                    (cursor / lap + 1) * lap
                ), deadline = 0;
                int written = 0;
                while (written < output.Length && cursor < limit && !(eof >= 0 && cursor >= eof))
                {
                    if (cursor >= end)
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (deadline == 0)
                            deadline = now + Stopwatch.Frequency * UnderrunWaitMilliseconds / 1000;
                        else if (now >= deadline)
                            break;
                        Monitor.PulseAll(gate);
                        Monitor.Wait(gate, WaitStepMilliseconds);
                        if (disposed || error != null || marker != generation)
                            return;
                        continue;
                    }
                    int count = (int)Math.Min(output.Length - written, end - cursor);
                    Copy(output, written, cursor, count);
                    Advance(count);
                    written += count;
                }
                if (written < output.Length && cursor < limit && !(eof >= 0 && cursor >= eof))
                    underruns++;
                Advance(Math.Min(limit, cursor + output.Length - written) - cursor);
                Monitor.PulseAll(gate);
            }
        }

        private void Advance(long count)
        {
            if (requested == cursor)
                requested += count;
            cursor += count;
        }

        public bool ReadAt(float[] output, long frame) => ReadAt(output, frame, output.Length);

        public bool ReadAt(float[] output, long frame, int count)
        {
            if (count < 0 || count > output.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            lock (gate)
            {
                if (frame < 0 || frame > long.MaxValue / Channels)
                    return false;
                long offset = (long)frame * Channels;
                if (disposed || offset < start || count > end - offset)
                    return false;
                Copy(output, 0, offset, count);
                return true;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                generation++;
                supervise.Set();
                Monitor.PulseAll(gate);
            }
            if (!supervisor.Join(MusicProcess.ShutdownMilliseconds + 250))
                CleanupFailed(BoomboxText.Get("error.supervisor_shutdown_timeout"));
            else
                lock (gate)
                    CloseSignal();
        }

        private void CloseSignal()
        {
            if (!signalClosed)
            {
                signalClosed = true;
                supervise.Dispose();
            }
        }

        private void Move(long frame)
        {
            requested = Clamp(frame);
            anchor = requested / Channels;
        }

        private long Nearest(int frame) => Looping
            ? Math.Max(0, anchor - frame + Frames / 2) / Frames * Frames + frame
            : frame;

        private long Clamp(long frame) => Math.Min(
            Math.Max(0, Math.Min(Length, frame)) * Channels,
            known >= 0
                ? known
                : long.MaxValue
        );

        private void Apply()
        {
            if (!launched)
            {
                if (begun)
                    Launch(requested);
                return;
            }
            if (requested == cursor)
                return;
            long jitter = (long)(JitterSeconds * Frequency) * Channels, reach = (long)(ReachSeconds * Frequency) * Channels;
            if (requested >= start - jitter && requested <= end + reach)
            {
                cursor = Math.Max(requested, start);
                requested = cursor;
                Monitor.PulseAll(gate);
                return;
            }
            Launch(requested);
        }

        private void Copy(float[] target, int targetOffset, long offset, int count)
        {
            int index = (int)(offset % ring.Length), first = Math.Min(count, ring.Length - index);
            Array.Copy(ring, index, target, targetOffset, first);
            if (count > first)
                Array.Copy(ring, 0, target, targetOffset + first, count - first);
        }
    }
}
