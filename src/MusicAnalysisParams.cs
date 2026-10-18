using System;

namespace VikingBoombox
{
    internal static class MusicAnalysisParams
    {
        public const double LookAheadSeconds = 0.2;
        public const double HopSeconds = 0.02;
        public const double OnsetWindowSeconds = 2048.0 / 22050.0;
        public const int OnsetMelBands = 128;
        public const double OnsetMelTopHz = 11025;
        public const double OnsetTopDb = 80;
        public const int PeakBeforeHops = 3;
        public const int PeakAfterHops = 2;
        public const int PeakMeanBeforeHops = 50;
        public const int PeakWaitHops = 3;
        public const double StrikeContextSeconds = 4;
        public const double StrikeContextPercentile = 90;
        public const int StrikeContextMinPeaks = 4;
        public const double StrikeThreshold = 0.6;
        public const double StrikeMinGapSeconds = 0.24;
        public const double StrikeMinRiseDb = 0.5;
        public const double StrikeLoudnessWindowSeconds = 0.06;
        public const double StrikeFullLoudness = 1.3;
        public const double LoudnessAverageSeconds = 4;
        public const double LoudnessSmoothSeconds = 0.25;
        public const double LoudnessQuiet = 0.7;
        public const double LoudnessLoud = 1.3;
        public const double LowTopHz = 250;
        public const double HighBottomHz = 4000;
        public const double SilenceDb = -50;
        public const double OnsetHighpassHz = 400;
        public const double OnsetBlockSeconds = 0.002;
        public const double OnsetBeforeSeconds = 0.04;
        public const double OnsetAfterSeconds = 0.02;
        public const double OnsetMinRiseDb = 3;
        public const double KeepSeconds = 3;

        public static int Round(double value) => (int)Math.Floor(value + 0.5);

        public static double LevelOf(
            double relative
        ) => Math.Min(1, Math.Max(0, (relative - LoudnessQuiet) / (LoudnessLoud - LoudnessQuiet)));
    }
}
