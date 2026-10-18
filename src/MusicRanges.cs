using System;

namespace VikingBoombox
{
    public enum MusicRange
    {
        Low, Mid, High
    }

    public struct MusicRanges
    {
        public double Whole, Low, Mid, High;

        public double this[MusicRange range] => range == MusicRange.Low
            ? Low
            : range == MusicRange.Mid
                ? Mid
                : High;

        public MusicRange Loudest => High > Mid && High > Low
            ? MusicRange.High
            : Mid > Low
                ? MusicRange.Mid
                : MusicRange.Low;
    }

    internal sealed class MusicRangeSplitter
    {
        private readonly MusicBiquad low;
        private readonly MusicBiquad midFloor;
        private readonly MusicBiquad midCeiling;
        private readonly MusicBiquad high;
        private MusicRanges sums;
        private int count;

        public MusicRangeSplitter(double sampleRate)
        {
            low = new MusicBiquad(true, MusicAnalysisParams.LowTopHz, sampleRate);
            midFloor = new MusicBiquad(false, MusicAnalysisParams.LowTopHz, sampleRate);
            midCeiling = new MusicBiquad(true, MusicAnalysisParams.HighBottomHz, sampleRate);
            high = new MusicBiquad(false, MusicAnalysisParams.HighBottomHz, sampleRate);
        }

        public void Add(double sample)
        {
            double lowValue = low.Process(sample);
            double midValue = midCeiling.Process(midFloor.Process(sample));
            double highValue = high.Process(sample);
            sums.Whole += sample * sample;
            sums.Low += lowValue * lowValue;
            sums.Mid += midValue * midValue;
            sums.High += highValue * highValue;
            count++;
        }

        public MusicRanges Take()
        {
            int used = Math.Max(1, count);
            var rms = new MusicRanges
            {
                Whole = Math.Sqrt(sums.Whole / used),
                Low = Math.Sqrt(sums.Low / used),
                Mid = Math.Sqrt(sums.Mid / used),
                High = Math.Sqrt(sums.High / used)
            };
            sums = default;
            count = 0;
            return rms;
        }

        public void Reset()
        {
            low.Reset();
            midFloor.Reset();
            midCeiling.Reset();
            high.Reset();
            sums = default;
            count = 0;
        }
    }
}
