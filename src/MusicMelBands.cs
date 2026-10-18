using System;

namespace VikingBoombox
{
    internal sealed class MusicMelBand
    {
        public int FirstBin;
        public float[] Weights;
    }

    internal static class MusicMelBands
    {
        private const double LinearStepHz = 200.0 / 3;
        private const double LogStartHz = 1000;
        private const double LogStartMel = LogStartHz / LinearStepHz;
        private static readonly double LogStep = Math.Log(6.4) / 27;

        public static double HzToMel(double hz) => hz < LogStartHz
            ? hz / LinearStepHz
            : LogStartMel + Math.Log(hz / LogStartHz) / LogStep;

        public static double MelToHz(double mel) => mel < LogStartMel
            ? mel * LinearStepHz
            : LogStartHz * Math.Exp(LogStep * (mel - LogStartMel));

        public static MusicMelBand[] Create(double sampleRate, int fftSize, int bandCount, double topHz)
        {
            double topMel = HzToMel(Math.Min(topHz, sampleRate / 2));
            var edges = new double[bandCount + 2];
            for (int index = 0; index < edges.Length; index++)
                edges[index] = MelToHz(topMel * index / (bandCount + 1));
            double binHz = sampleRate / fftSize;
            int bins = fftSize / 2 + 1;
            var bands = new MusicMelBand[bandCount];
            for (int band = 0; band < bandCount; band++)
                bands[band] = Weights(edges[band], edges[band + 1], edges[band + 2], binHz, bins);
            return bands;
        }

        public static void Apply(float[] power, MusicMelBand[] bands, float[] output)
        {
            for (int index = 0; index < bands.Length; index++)
            {
                var band = bands[index];
                double sum = 0;
                for (int offset = 0; offset < band.Weights.Length; offset++)
                    sum += band.Weights[offset] * (double)power[band.FirstBin + offset];
                output[index] = (float)sum;
            }
        }

        private static MusicMelBand Weights(double lowerHz, double centerHz, double upperHz, double binHz, int bins)
        {
            double scale = 2 / (upperHz - lowerHz);
            int firstBin = Math.Max(0, (int)Math.Ceiling(lowerHz / binHz));
            int lastBin = Math.Min(bins - 1, (int)Math.Floor(upperHz / binHz));
            var weights = new float[Math.Max(0, lastBin - firstBin + 1)];
            for (int bin = firstBin; bin <= lastBin; bin++)
            {
                double hz = bin * binHz;
                double rising = (hz - lowerHz) / (centerHz - lowerHz);
                double falling = (upperHz - hz) / (upperHz - centerHz);
                weights[bin - firstBin] = (float)(Math.Max(0, Math.Min(rising, falling)) * scale);
            }
            return new MusicMelBand { FirstBin = firstBin, Weights = weights };
        }
    }
}
