using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace VikingBoombox
{
    public enum BoomboxMusicEffect
    {
        Sparks, RunesAround, Strike, FixedCracks, Pulse
    }

    public enum BoomboxSparkColor
    {
        Own, Heat, Bands
    }

    public sealed class MusicEffectVariantSettings
    {
        public readonly ConfigEntry<float> Threshold;
        public readonly ConfigEntry<float> Brightness;
        public readonly ConfigEntry<Color> Color;
        public readonly ConfigEntry<BoomboxSparkColor> ColorMode;
        public readonly bool UsesAmount;

        public MusicEffectVariantSettings(
            ConfigFile config,
            string section,
            Color color,
            bool sparkColor,
            bool usesAmount
        )
        {
            UsesAmount = usesAmount;
            Threshold = config.Bind(
                section,
                "StrikeThreshold",
                (float)MusicAnalysisParams.StrikeThreshold,
                BoomboxText.Description("text.strike_threshold_a_strike_counts_when_it", new AcceptableValueRange<float>(.3f, .9f))
            );
            Brightness = config.Bind(
                section,
                "Brightness",
                1.4f,
                BoomboxText.Description("text.effect_brightness_0_means_the_effect_does_not_glow", new AcceptableValueRange<float>(0f, 3f))
            );
            Color = config.Bind(section, "Color", color, BoomboxText.Description("text.effect_colour"));
            if (sparkColor)
                ColorMode =
                    config.Bind(
                    section,
                    "SparkColor",
                    BoomboxSparkColor.Own,
                    BoomboxText.Description("text.spark_colour_own_the_colour_from_color")
                );
        }
    }

    public sealed class MusicEffectSettings
    {
        private static readonly Color RingColor = new Color(.2f, .8f, 1f, 1f);
        private static readonly Color EmberColor = new Color(1f, 106f / 255f, 26f / 255f, 1f);
        public readonly ConfigEntry<BoomboxMusicEffect> Variant;
        public readonly ConfigEntry<bool> CreatureDamage;
        public readonly ConfigEntry<float> Amount;
        public readonly Dictionary<BoomboxMusicEffect, MusicEffectVariantSettings> Variants =
            new Dictionary<BoomboxMusicEffect, MusicEffectVariantSettings>();

        public MusicEffectSettings(ConfigFile config)
        {
            Variant =
                config.Bind(
                "Music effect",
                "Variant",
                BoomboxMusicEffect.Sparks,
                BoomboxText.Description("text.music_effect_variant_sparks_runesaround_runes_2")
            );
            CreatureDamage =
                config.Bind(
                "Music effect",
                "CreatureDamage",
                true,
                BoomboxText.Description("text.damage_to_creatures_when_off_the_effect")
            );
            Amount = config.Bind(
                "Music effect",
                "Amount",
                1f,
                BoomboxText.Description("text.amount_1_is_normal_up_to_2", new AcceptableValueRange<float>(.25f, 2f))
            );
            Variants[BoomboxMusicEffect.Sparks] = new MusicEffectVariantSettings(
                config,
                "Music effect Sparks",
                RingColor,
                true,
                true
            );
            Variants[BoomboxMusicEffect.RunesAround] = new MusicEffectVariantSettings(
                config,
                "Music effect Runes around",
                RingColor,
                true,
                true
            );
            Variants[BoomboxMusicEffect.Strike] = new MusicEffectVariantSettings(
                config,
                "Music effect Strike",
                RingColor,
                false,
                true
            );
            Variants[BoomboxMusicEffect.FixedCracks] = new MusicEffectVariantSettings(
                config,
                "Music effect Fixed cracks",
                EmberColor,
                false,
                true
            );
            Variants[BoomboxMusicEffect.Pulse] = new MusicEffectVariantSettings(
                config,
                "Music effect Pulse",
                RingColor,
                false,
                false
            );
        }

        public MusicEffectVariantSettings Current => Variants[Variant.Value];
    }
}
