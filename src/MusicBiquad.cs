using System;

namespace VikingBoombox
{
    internal sealed class MusicBiquad
    {
        private readonly double b0, b1, b2, a1, a2;
        private double x1, x2, y1, y2;

        public MusicBiquad(bool lowpass, double frequencyHz, double sampleRate)
        {
            double angle = 2 * Math.PI * frequencyHz / sampleRate;
            double cos = Math.Cos(angle);
            double alpha = Math.Sin(angle) / Math.Sqrt(2);
            double norm = 1 + alpha;
            double edge = lowpass ? (1 - cos) / 2 : (1 + cos) / 2;
            b0 = edge / norm;
            b1 = (lowpass ? 2 * edge : -2 * edge) / norm;
            b2 = edge / norm;
            a1 = -2 * cos / norm;
            a2 = (1 - alpha) / norm;
        }

        public double Process(double input)
        {
            double output = b0 * input + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = input;
            y2 = y1;
            y1 = output;
            return output;
        }

        public void Reset() => x1 = x2 = y1 = y2 = 0;
    }
}
