using System;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxMusicTap : MonoBehaviour
    {
        private const float QuietAfterSeconds = 1f;
        private const int Capacity = 65536;
        private readonly object gate = new object();
        private readonly float[] pending = new float[Capacity];
        private readonly float[] draining = new float[Capacity];
        private int count;
        private long dropped;
        private volatile bool listening;
        private float lastDrain;
        private readonly float[] externalWindow = new float[1024];
        private long externalStamp;
        internal volatile float InputGain = 1f;
        public int SampleRate
        {
            get;
            private set;
        }

        private void Awake() => SampleRate = AudioSettings.outputSampleRate;

        internal void PushExternal(float[] samples, int length, int frequency)
        {
            lock (gate)
            {
                SampleRate = frequency;
                int start = MakeRoom(length);
                for (int i = start; i < length; i++)
                    pending[count++] = samples[i] * InputGain;
                int recent = Math.Min(length, externalWindow.Length);
                if (recent < externalWindow.Length)
                    Array.Copy(externalWindow, recent, externalWindow, 0, externalWindow.Length - recent);
                for (int i = 0; i < recent; i++)
                    externalWindow[externalWindow.Length - recent + i] = samples[length - recent + i] * InputGain;
                externalStamp = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }

        internal bool ReadExternal(float[] output)
        {
            lock (gate)
            {
                bool fresh = externalStamp != 0
                    && (System.Diagnostics.Stopwatch.GetTimestamp() - externalStamp)
                    / (double)System.Diagnostics.Stopwatch.Frequency
                    < .15;
                if (fresh)
                    Array.Copy(externalWindow, output, Math.Min(output.Length, externalWindow.Length));
                else
                    Array.Clear(output, 0, output.Length);
                return fresh;
            }
        }

        private int MakeRoom(int frames)
        {
            int start = Math.Max(0, frames - Capacity);
            int incoming = frames - start;
            int discard = Math.Max(0, count + incoming - Capacity);
            if (discard > 0)
            {
                Array.Copy(pending, discard, pending, 0, count - discard);
                count -= discard;
            }
            dropped += start + discard;
            return start;
        }

        private void Update()
        {
            if (!listening || Time.unscaledTime - lastDrain < QuietAfterSeconds)
                return;
            lock (gate)
            {
                listening = false;
                count = 0;
                dropped = 0;
            }
        }

        public double Drain(MusicSoundAnalyzer analyzer, MusicLoudnessWindow loudness, double time)
        {
            lastDrain = Time.unscaledTime;
            listening = true;
            int taken;
            long skipped;
            lock (gate)
            {
                Array.Copy(pending, draining, count);
                taken = count;
                skipped = dropped;
                count = 0;
                dropped = 0;
            }
            if (taken == 0)
                return time;
            if (skipped > 0)
            {
                time += skipped / (double)SampleRate;
                analyzer.Reset();
            }
            analyzer.Push(time, draining, taken);
            loudness.Add(draining, taken);
            return time + taken / (double)SampleRate;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!listening || channels <= 0)
                return;
            int frames = data.Length / channels;
            lock (gate)
            {
                if (!listening)
                    return;
                int start = MakeRoom(frames);
                for (int frame = start; frame < frames; frame++)
                {
                    float mixed = 0;
                    for (int channel = 0; channel < channels; channel++)
                        mixed = (float)(mixed + data[frame * channels + channel] / (double)channels);
                    pending[count++] = mixed;
                }
            }
        }
    }
}
