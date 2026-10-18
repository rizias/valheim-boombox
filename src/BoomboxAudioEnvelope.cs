using System;

namespace VikingBoombox
{
    internal static class BoomboxAudioEnvelope
    {
        public const double BassHz = 180.0;

        public static float BassRms(float[] samples, int channels, int sampleRate)
        {
            if (samples == null || channels <= 0 || sampleRate <= 0 || samples.Length < channels)
                return 0f;
            int frames = samples.Length / channels;
            double coefficient = 1.0 - Math.Exp(-2.0 * Math.PI * BassHz / sampleRate);
            double energy = 0.0;
            for (int channel = 0; channel < channels; channel++)
            {
                double low = 0.0;
                for (int frame = 0; frame < frames; frame++)
                {
                    low += coefficient * (samples[frame * channels + channel] - low);
                    energy += low * low;
                }
            }
            double level = Math.Sqrt(energy / (frames * channels));
            return double.IsNaN(level) || double.IsInfinity(level) ? 0f : (float)level;
        }

        public static float Follow(float current, float target, float deltaSeconds)
        {
            double seconds = target > current ? 0.015 : 0.065;
            return current + (target - current) * (float)(1.0 - Math.Exp(-Math.Max(0f, deltaSeconds) / seconds));
        }
    }
}
