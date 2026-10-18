using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class MusicEffectFrame
    {
        public double Time;
        public float Delta;
        public float Radius;
        public Vector3 Center;
        public MusicMoment Moment;
        public List<MusicLiveStrike> Fresh;
        public List<MusicLiveStrike> Strikes;
        public readonly List<MusicTarget> Targets = new List<MusicTarget>();
        public float Brightness;
        public Color Color;
        public BoomboxSparkColor ColorMode;
        public float Amount;

        public void ApplyLook(MusicEffectSettings settings, BoomboxMusicEffect variant)
        {
            var look = settings.Variants[variant];
            Brightness = look.Brightness.Value;
            Color = look.Color.Value.linear;
            ColorMode = look.ColorMode?.Value ?? BoomboxSparkColor.Own;
            bool expanded = variant == BoomboxMusicEffect.Sparks || variant == BoomboxMusicEffect.RunesAround;
            Amount = Mathf.Clamp(settings.Amount.Value, .25f, expanded ? 2f : 1f);
        }
    }

    internal interface IMusicEffectView
    {
        void Update(MusicEffectFrame frame);
        void Hide();
        void Destroy();
    }

    internal interface IMusicStrikeView : IMusicEffectView
    {
        MusicReach Reach
        {
            get;
        }
    }

    internal static class MusicOrbit
    {
        public static float Gap(float a, float b)
        {
            float turn = 2 * Mathf.PI;
            float gap = ((a - b) % turn + turn) % turn;
            return Mathf.Min(gap, turn - gap);
        }

        public static bool Visible(
            int index,
            int shown,
            int count
        ) => (index + 1) * shown / count > index * shown / count;
    }

    internal static class MusicEffectColors
    {
        public static Color Linear(
            int hex
        ) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f).linear;

        public static Color Vertex(Color linear) => QualitySettings.activeColorSpace == ColorSpace.Linear
            ? linear
            : linear.gamma;

        public static Texture2D SoftDot()
        {
            const int size = 64;
            var texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false
            )
            {
                name = "Boombox music soft dot",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude / (size / 2f);
                    float alpha = r <= .35f
                        ? 1f - .4f * r / .35f
                        : r < 1f
                            ? .6f * (1f - (r - .35f) / .65f)
                            : 0f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
