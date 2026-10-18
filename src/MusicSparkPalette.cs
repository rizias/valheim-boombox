using UnityEngine;

namespace VikingBoombox
{
    internal sealed class MusicSparkPalette
    {
        private const double BandBlendSeconds = .3;
        private static readonly Color[] Heat = {
            MusicEffectColors.Linear(0x8a1c00),
            MusicEffectColors.Linear(0xff7a1a),
            MusicEffectColors.Linear(0xfff0c0)
        };
        private static readonly Color[] Bands = {
            MusicEffectColors.Linear(0xff2a1a),
            MusicEffectColors.Linear(0xffd21a),
            MusicEffectColors.Linear(0x2a5aff)
        };
        private Color band;
        private Color from;
        private MusicRange? current;
        private double changed = double.NegativeInfinity;
        public Color Current
        {
            get;
            private set;
        }

        public Color Update(BoomboxSparkColor mode, Color own, MusicRanges smooth, double time)
        {
            var bands = UpdateBands(smooth, time);
            Current = mode == BoomboxSparkColor.Heat
                ? HeatColor((float)MusicAnalysisParams.LevelOf(smooth.Whole))
                : mode == BoomboxSparkColor.Bands
                    ? bands
                    : own;
            return Current;
        }

        public void Reset()
        {
            current = null;
            changed = double.NegativeInfinity;
        }

        private Color UpdateBands(MusicRanges smooth, double time)
        {
            if (smooth.Low + smooth.Mid + smooth.High > 0)
            {
                var loudest = smooth.Loudest;
                if (loudest != current)
                {
                    from = current == null ? Bands[(int)loudest] : band;
                    current = loudest;
                    changed = time;
                }
            }
            float share = Mathf.Clamp01((float)((time - changed) / BandBlendSeconds));
            band = Color.Lerp(from, Bands[(int)(current ?? MusicRange.Low)], share);
            return band;
        }

        private static Color HeatColor(float level)
        {
            float share = Mathf.Clamp01(level) * (Heat.Length - 1);
            int index = Mathf.Min(Heat.Length - 2, Mathf.FloorToInt(share));
            return Color.Lerp(Heat[index], Heat[index + 1], share - index);
        }
    }
}
