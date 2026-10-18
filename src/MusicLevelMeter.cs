using System;
using System.Collections.Generic;

namespace VikingBoombox
{
    internal sealed class MusicLevelMeter
    {
        public const double LoudnessSeconds = .02;
        private const double WindowSeconds = 4;
        private const double QuietFraction = .1;
        private const double MinRangeDb = 6;
        private readonly List<KeyValuePair<double, double>> frames = new List<KeyValuePair<double, double>>();
        private readonly List<double> sorted = new List<double>();

        public void Reset() => frames.Clear();

        public float Push(double time, double db)
        {
            if (double.IsNaN(db) || double.IsInfinity(db) || db < MusicAnalysisParams.SilenceDb)
                return 0f;
            if (frames.Count > 0 && time < frames[frames.Count - 1].Key)
                frames.Clear();
            frames.Add(new KeyValuePair<double, double>(time, db));
            double oldest = time - WindowSeconds;
            int expired = 0;
            while (expired < frames.Count && frames[expired].Key < oldest)
                expired++;
            frames.RemoveRange(0, expired);
            sorted.Clear();
            foreach (var frame in frames)
                sorted.Add(frame.Value);
            sorted.Sort();
            double quiet = sorted[(int)Math.Floor(QuietFraction * (sorted.Count - 1))];
            double loud = sorted[sorted.Count - 1];
            return (float)Math.Min(1, Math.Max(0, (db - quiet) / Math.Max(loud - quiet, MinRangeDb)));
        }

        public static double RmsDb(float[] samples, int count)
        {
            if (count <= 0)
                return double.NegativeInfinity;
            double sum = 0;
            for (int index = 0; index < count; index++)
                sum += samples[index] * (double)samples[index];
            double rms = Math.Sqrt(sum / count);
            return rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity;
        }

        public static int LoudnessSamples(int sampleRate) => MusicAnalysisParams.Round(LoudnessSeconds * sampleRate);
    }

    internal sealed class MusicLoudnessWindow
    {
        private readonly float[] ring;
        private int write;

        public MusicLoudnessWindow(int sampleRate) => ring = new float[MusicLevelMeter.LoudnessSamples(sampleRate)];

        public void Add(float[] samples, int count)
        {
            for (int index = 0; index < count; index++)
            {
                ring[write] = samples[index];
                write = (write + 1) % ring.Length;
            }
        }

        public double Db() => MusicLevelMeter.RmsDb(ring, ring.Length);
    }
}
