using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingBoombox
{
    public struct MusicStrike
    {
        public double Time;
        public float Strength;
        public MusicRange Loudest;
    }

    internal struct MusicHopFeatures
    {
        public double Time;
        public double Flux;
        public double RmsDb;
        public MusicRanges Instant;
    }

    internal sealed class MusicStrikePicker
    {
        private static readonly int ContextHops = MusicAnalysisParams.Round(MusicAnalysisParams.StrikeContextSeconds
            / MusicAnalysisParams.HopSeconds);
        private static readonly int LoudnessHops = MusicAnalysisParams.Round(
            MusicAnalysisParams.StrikeLoudnessWindowSeconds / MusicAnalysisParams.HopSeconds
        );
        public static readonly int LookaheadHops = Math.Max(MusicAnalysisParams.PeakAfterHops, LoudnessHops - 1);
        private static readonly int KeepHops = MusicAnalysisParams.PeakMeanBeforeHops + LookaheadHops
            + MusicAnalysisParams.PeakBeforeHops
            + 8;
        private static readonly int WindowHops = (int)Math.Ceiling(MusicAnalysisParams.OnsetWindowSeconds
            / MusicAnalysisParams.HopSeconds);
        private readonly List<MusicHopFeatures> hops = new List<MusicHopFeatures>();
        private readonly List<KeyValuePair<int, double>> peaks = new List<KeyValuePair<int, double>>();
        private int firstHop;
        private int lastPeak = int.MinValue / 2;
        private double lastStrike = double.NegativeInfinity;
        public double Threshold = MusicAnalysisParams.StrikeThreshold;

        public void Reset()
        {
            hops.Clear();
            peaks.Clear();
            firstHop = 0;
            lastPeak = int.MinValue / 2;
            lastStrike = double.NegativeInfinity;
        }

        public bool Push(MusicHopFeatures hop, Func<double, double> refine, out MusicStrike strike)
        {
            strike = default;
            hops.Add(hop);
            if (hops.Count > KeepHops)
            {
                hops.RemoveAt(0);
                firstHop++;
            }
            int candidate = firstHop + hops.Count - 1 - LookaheadHops;
            return candidate >= 0 && IsPeak(candidate) && Decide(candidate, refine, out strike);
        }

        public static double Percentile(List<double> values, double share)
        {
            var sorted = values.OrderBy(value => value).ToList();
            double position = (sorted.Count - 1) * (share / 100);
            int lower = (int)Math.Floor(position);
            double low = sorted[lower], high = sorted[Math.Min(sorted.Count - 1, lower + 1)];
            return low + (high - low) * (position - lower);
        }

        private bool Has(int hop) => hop - firstHop >= 0 && hop - firstHop < hops.Count;

        private double Flux(int hop, double missing) => Has(hop) ? hops[hop - firstHop].Flux : missing;

        private bool IsPeak(int hop)
        {
            double flux = Flux(hop, 0);
            if (flux <= 0 || hop <= lastPeak + MusicAnalysisParams.PeakWaitHops)
                return false;
            for (int other = hop - MusicAnalysisParams.PeakBeforeHops; other <= hop + MusicAnalysisParams.PeakAfterHops; other++)
                if (Flux(other, double.NegativeInfinity) > flux)
                    return false;
            return flux >= MeanAround(hop);
        }

        private double MeanAround(int hop)
        {
            double sum = 0;
            int count = 0;
            for (int other = Math.Max(
                firstHop,
                hop - MusicAnalysisParams.PeakMeanBeforeHops
            ); other <= hop + MusicAnalysisParams.PeakAfterHops; other++)
            {
                if (AfterSilence(other))
                    continue;
                sum += Flux(other, 0);
                count++;
            }
            return count > 0 ? sum / count : 0;
        }

        private bool Decide(int hop, Func<double, double> refine, out MusicStrike strike)
        {
            strike = default;
            lastPeak = hop;
            double flux = Flux(hop, 0);
            double reference = Reference(hop, flux);
            int start = hop - firstHop, following = Math.Min(LoudnessHops, hops.Count - start);
            bool audible = following > 0 && hops[start].RmsDb > MusicAnalysisParams.SilenceDb;
            if (flux < MusicAnalysisParams.StrikeMinRiseDb || flux < Threshold * reference || !audible)
                return false;
            double time = refine(hops[start].Time);
            if (time - lastStrike < MusicAnalysisParams.StrikeMinGapSeconds - 1e-9)
                return false;
            lastStrike = time;
            var peak = new MusicRanges();
            for (int index = start; index < start + following; index++)
            {
                var instant = hops[index].Instant;
                peak.Whole = Math.Max(peak.Whole, instant.Whole);
                peak.Low = Math.Max(peak.Low, instant.Low);
                peak.Mid = Math.Max(peak.Mid, instant.Mid);
                peak.High = Math.Max(peak.High, instant.High);
            }
            strike = new MusicStrike
            {
                Time = time,
                Strength = (float)Math.Min(1, peak.Whole / MusicAnalysisParams.StrikeFullLoudness),
                Loudest = peak.Loudest
            };
            return true;
        }

        private double Reference(int hop, double flux)
        {
            peaks.RemoveAll(peak => peak.Key < hop - ContextHops);
            var context = peaks.Select(peak => peak.Value).ToList();
            if (!AfterSilence(hop))
                peaks.Add(new KeyValuePair<int, double>(hop, flux));
            return context.Count >= MusicAnalysisParams.StrikeContextMinPeaks
                ? Percentile(context, MusicAnalysisParams.StrikeContextPercentile)
                : flux;
        }

        private bool AfterSilence(int hop)
        {
            for (int other = hop - WindowHops; other < hop; other++)
                if (other < 0
                || (Has(other)
                    ? hops[other - firstHop].RmsDb
                    : MusicAnalysisParams.SilenceDb) < MusicAnalysisParams.SilenceDb)
                    return true;
            return false;
        }
    }
}
