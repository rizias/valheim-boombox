using System;
using System.Collections.Generic;

namespace VikingBoombox
{
    internal sealed class BoomboxMembraneMeter
    {
        private const double IntervalSeconds = .02;
        private const double PeakSeconds = 3;
        private const float Silence = .01f;
        private const int WarmFrames = 512;
        private static readonly int StepFrames = MusicAnalysisParams.Round(IntervalSeconds * MusicLiveStream.Frequency);
        private readonly float[] window = new float[1024];
        private readonly float[] wave = new float[1024];
        private readonly float[] block = new float[(WarmFrames + StepFrames) * MusicLiveStream.Channels];
        private readonly float[] swings = new float[(int)Math.Round(PeakSeconds / IntervalSeconds)];
        private readonly double[] lows = new double[MusicLiveStream.Channels];
        private readonly Queue<KeyValuePair<float, float>> heard = new Queue<KeyValuePair<float, float>>();
        private long measured = -1;
        private long swung = -1;
        private int swingsStored;
        private float peak;

        public float Level
        {
            get;
            private set;
        }
        public float Travel
        {
            get;
            private set;
        }

        public void Forget()
        {
            measured = -1;
            swung = -1;
            swingsStored = 0;
            peak = 0f;
            heard.Clear();
            Rest();
        }

        public void Rest()
        {
            Level = 0f;
            Travel = 0f;
        }

        public void Measure(MusicLiveStream stream, long frame, int channels, int frequency, long length)
        {
            long step = (long)Math.Round(IntervalSeconds * frequency), now = frame / step;
            if (now == measured)
                return;
            measured = now;
            long offset = Math.Min(now * step, Math.Max(0, length - window.Length / channels));
            Level = stream.ReadAt(window, offset) ? BoomboxAudioEnvelope.BassRms(window, channels, frequency) : 0f;
        }

        public void MeasureWave(MusicLiveStream stream, long frame)
        {
            const int channels = MusicLiveStream.Channels;
            if (frame < 0)
            {
                Travel = 0f;
                return;
            }
            long now = frame / StepFrames;
            if (swung < 0 || now < swung || now - swung > swings.Length)
            {
                swung = now;
                swingsStored = 0;
            }
            while (swung < now)
            {
                swings[swung % swings.Length] = BlockSwing(stream, swung * StepFrames, StepFrames);
                swingsStored = Math.Min(swingsStored + 1, swings.Length);
                swung++;
            }
            peak = BlockSwing(stream, now * StepFrames, (int)(frame - now * StepFrames + 1));
            for (int index = 0; index < Math.Min(swingsStored, swings.Length - 1); index++)
                peak = Math.Max(peak, swings[(swung - index - 1) % swings.Length]);
            long first = frame - wave.Length / channels + 1;
            if (first < 0 || !stream.ReadAt(wave, first))
            {
                Travel = 0f;
                return;
            }
            Swing(wave, channels, MusicLiveStream.Frequency, 0, wave.Length / channels, out float last, out float rms);
            Travel = Relative(last, rms);
        }

        public void MeasureGameMusic(float now, int frequency)
        {
            if (!BoomboxGameMusic.ReadOutput(window))
            {
                Rest();
                return;
            }
            MeasureWindow(now, frequency, 1f);
        }

        internal void MeasureDesktop(BoomboxMusicTap tap, float now, float gain)
        {
            if (!tap.ReadExternal(window))
            {
                Rest();
                return;
            }
            MeasureWindow(now, tap.SampleRate, gain);
        }

        private void MeasureWindow(float now, int frequency, float gain)
        {
            float largest = Swing(window, 1, frequency, 0, window.Length, out float last, out float rms);
            while (heard.Count > 0 && now - heard.Peek().Key > PeakSeconds)
                heard.Dequeue();
            heard.Enqueue(new KeyValuePair<float, float>(now, largest));
            peak = 0f;
            foreach (var entry in heard)
                peak = Math.Max(peak, entry.Value);
            Level = rms * gain;
            Travel = Math.Min(1f, Relative(last, rms) * gain);
        }

        private float BlockSwing(MusicLiveStream stream, long begin, int frames)
        {
            long first = Math.Max(0, begin - WarmFrames);
            int from = (int)(begin - first);
            if (!stream.ReadAt(block, first, (from + frames) * MusicLiveStream.Channels))
                return 0f;
            return Swing(block, MusicLiveStream.Channels, MusicLiveStream.Frequency, from, from + frames, out _, out _);
        }

        private float Swing(
            float[] samples,
            int channels,
            int frequency,
            int from,
            int to,
            out float last,
            out float rms
        )
        {
            double coefficient = 1.0 - Math.Exp(-2.0 * Math.PI * BoomboxAudioEnvelope.BassHz / frequency);
            Array.Clear(lows, 0, lows.Length);
            double energy = 0, largest = 0, mean = 0;
            for (int frame = 0; frame < to; frame++)
            {
                mean = 0;
                for (int channel = 0; channel < channels; channel++)
                {
                    lows[channel] += coefficient * (samples[frame * channels + channel] - lows[channel]);
                    energy += lows[channel] * lows[channel];
                    mean += lows[channel];
                }
                mean /= channels;
                if (frame >= from)
                    largest = Math.Max(largest, Math.Abs(mean));
            }
            last = (float)mean;
            rms = (float)Math.Sqrt(energy / ((double)to * channels));
            return (float)largest;
        }

        private float Relative(float last, float rms) => rms < Silence || peak <= 0f
            ? 0f
            : Math.Min(1f, Math.Abs(last) / peak);
    }
}
