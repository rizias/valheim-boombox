using System;

namespace VikingBoombox
{
    internal static class MusicOnsetTime
    {
        public static double Find(float[] samples, double endSeconds, double sampleRate, double hopEndSeconds)
        {
            double fallback = hopEndSeconds - MusicAnalysisParams.HopSeconds;
            int blockSamples = Math.Max(
                1,
                MusicAnalysisParams.Round(MusicAnalysisParams.OnsetBlockSeconds * sampleRate)
            );
            double startSeconds = hopEndSeconds - MusicAnalysisParams.OnsetBeforeSeconds;
            int firstIndex = samples.Length - MusicAnalysisParams.Round((endSeconds - startSeconds) * sampleRate);
            int blocks = (int)Math.Floor(
                (MusicAnalysisParams.OnsetBeforeSeconds + MusicAnalysisParams.OnsetAfterSeconds) * sampleRate
                    / blockSamples
            );
            if (firstIndex < 0 || firstIndex + blocks * blockSamples > samples.Length)
                return fallback;
            var levels = new double[blocks];
            for (int block = 0; block < blocks; block++)
                levels[block] = BlockDb(samples, firstIndex + block * blockSamples, blockSamples);
            int crossing = Rising(
                levels,
                MusicAnalysisParams.Round(
                    (MusicAnalysisParams.OnsetBeforeSeconds - MusicAnalysisParams.HopSeconds)
                        / MusicAnalysisParams.OnsetBlockSeconds
                )
            );
            return crossing < 0 ? fallback : startSeconds + crossing * blockSamples / sampleRate;
        }

        private static double BlockDb(float[] samples, int first, int length)
        {
            double sum = 0;
            for (int index = first; index < first + length; index++)
                sum += samples[index] * (double)samples[index];
            return 10 * Math.Log10(Math.Max(1e-12, sum / Math.Max(1, length)));
        }

        private static int Rising(double[] values, int fromBlock)
        {
            int peak = fromBlock;
            for (int block = fromBlock; block < values.Length; block++)
                if (values[block] > values[peak])
                    peak = block;
            int floor = 0;
            for (int block = 1; block <= peak; block++)
                if (values[block] < values[floor])
                    floor = block;
            if (values[peak] - values[floor] < MusicAnalysisParams.OnsetMinRiseDb)
                return -1;
            double middle = values[floor] + (values[peak] - values[floor]) / 2;
            for (int block = floor; block <= peak; block++)
                if (values[block] >= middle)
                    return block;
            return -1;
        }
    }
}
