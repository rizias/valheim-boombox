using System;
using System.Collections.Generic;

namespace VikingBoombox
{
    internal sealed class MusicRelativeMeter
    {
        private static readonly int AverageHops = MusicAnalysisParams.Round(MusicAnalysisParams.LoudnessAverageSeconds
            / MusicAnalysisParams.HopSeconds);
        private const double SmoothSpanHops = MusicAnalysisParams.LoudnessSmoothSeconds / MusicAnalysisParams.HopSeconds;
        private static readonly int SmoothHops = (int)Math.Ceiling(SmoothSpanHops);
        private readonly List<double> squares = new List<double>();
        private readonly List<double> relativeSquares = new List<double>();

        public double Push(double rms, out double smooth)
        {
            Remember(squares, rms * rms, AverageHops);
            double sum = 0;
            foreach (double value in squares)
                sum += value;
            double average = Math.Sqrt(squares.Count == 0 ? 0 : sum / squares.Count);
            double instant = average > 0 ? rms / average : 0;
            Remember(relativeSquares, instant * instant, SmoothHops);
            smooth = Math.Sqrt(MeanOfLast(relativeSquares, SmoothSpanHops));
            return instant;
        }

        public void Reset()
        {
            squares.Clear();
            relativeSquares.Clear();
        }

        private static double MeanOfLast(List<double> values, double spanHops)
        {
            double sum = 0, weights = 0;
            for (int back = 0; back < values.Count && back < spanHops; back++)
            {
                double weight = Math.Min(1, spanHops - back);
                sum += weight * values[values.Count - 1 - back];
                weights += weight;
            }
            return weights > 0 ? sum / weights : 0;
        }

        private static void Remember(List<double> values, double value, int keep)
        {
            values.Add(value);
            if (values.Count > keep)
                values.RemoveAt(0);
        }
    }

    internal sealed class MusicRangeMeters
    {
        private readonly MusicRelativeMeter whole = new MusicRelativeMeter();
        private readonly MusicRelativeMeter low = new MusicRelativeMeter();
        private readonly MusicRelativeMeter mid = new MusicRelativeMeter();
        private readonly MusicRelativeMeter high = new MusicRelativeMeter();

        public MusicRanges Push(MusicRanges rms, out MusicRanges smooth)
        {
            var instant = new MusicRanges
            {
                Whole = whole.Push(rms.Whole, out smooth.Whole),
                Low = low.Push(rms.Low, out smooth.Low),
                Mid = mid.Push(rms.Mid, out smooth.Mid),
                High = high.Push(rms.High, out smooth.High),
            };
            return instant;
        }

        public void Reset()
        {
            whole.Reset();
            low.Reset();
            mid.Reset();
            high.Reset();
        }
    }
}
