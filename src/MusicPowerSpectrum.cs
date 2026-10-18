using System;

namespace VikingBoombox
{
    internal sealed class MusicPowerSpectrum
    {
        public readonly int Size;
        public readonly int WindowLength;
        private readonly double[] window;
        private readonly int[] reversed;
        private readonly double[] cos;
        private readonly double[] sin;
        private readonly double[] re;
        private readonly double[] im;

        public MusicPowerSpectrum(int windowLength, int size)
        {
            if (size < 2 || (size & (size - 1)) != 0 || windowLength > size)
                throw new ArgumentException(BoomboxText.Get("text.window") + windowLength + BoomboxText.Get("text.does_not_fit_in_transform") + size);
            Size = size;
            WindowLength = windowLength;
            window = new double[windowLength];
            for (int index = 0; index < windowLength; index++)
                window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / windowLength);
            reversed = BitReversal(size);
            cos = new double[size / 2];
            sin = new double[size / 2];
            for (int index = 0; index < size / 2; index++)
            {
                cos[index] = Math.Cos(-2 * Math.PI * index / size);
                sin[index] = Math.Sin(-2 * Math.PI * index / size);
            }
            re = new double[size];
            im = new double[size];
        }

        public static int WindowSamples(
            double seconds,
            double sampleRate
        ) => MusicAnalysisParams.Round(seconds * sampleRate);

        public static int SizeFor(int samples)
        {
            int size = 2;
            while (size < samples)
                size *= 2;
            return size;
        }

        public void Compute(float[] frame, float[] power)
        {
            for (int index = 0; index < Size; index++)
            {
                int target = reversed[index];
                re[target] = index < WindowLength ? frame[index] * window[index] : 0;
                im[target] = 0;
            }
            for (int span = 2; span <= Size; span *= 2)
            {
                int half = span / 2, step = Size / span;
                for (int start = 0; start < Size; start += span)
                    Butterflies(start, half, step);
            }
            for (int bin = 0; bin <= Size / 2; bin++)
                power[bin] = (float)(re[bin] * re[bin] + im[bin] * im[bin]);
        }

        private void Butterflies(int start, int half, int step)
        {
            for (int offset = 0; offset < half; offset++)
            {
                double c = cos[offset * step], s = sin[offset * step];
                int top = start + offset, bottom = top + half;
                double turnedRe = re[bottom] * c - im[bottom] * s;
                double turnedIm = re[bottom] * s + im[bottom] * c;
                double topRe = re[top], topIm = im[top];
                re[bottom] = topRe - turnedRe;
                im[bottom] = topIm - turnedIm;
                re[top] = topRe + turnedRe;
                im[top] = topIm + turnedIm;
            }
        }

        private static int[] BitReversal(int size)
        {
            int bits = 0;
            while (1 << bits < size)
                bits++;
            var table = new int[size];
            for (int index = 0; index < size; index++)
            {
                int value = 0;
                for (int bit = 0; bit < bits; bit++)
                    value = (value << 1) | ((index >> bit) & 1);
                table[index] = value;
            }
            return table;
        }
    }
}
