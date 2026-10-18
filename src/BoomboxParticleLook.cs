using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxParticleLook
    {
        internal const float Minimum = .25f;
        internal const float Maximum = 3f;
        internal const float Step = .25f;
        internal const float CoreLighten = .35f;

        internal static float Brightness => Clamp(BoomboxPlugin.Instance?.Settings?.ParticleBrightness?.Value ?? 1f);

        internal static float Amount => Clamp(BoomboxPlugin.Instance?.Settings?.ParticleAmount?.Value ?? 1f);

        internal static float Clamp(float value) => float.IsNaN(value) ? 1f : Mathf.Clamp(value, Minimum, Maximum);

        internal static int Count(float baseCount) => Mathf.Max(1, Mathf.RoundToInt(baseCount * Amount));

        internal static Gradient Lighten(Gradient source)
        {
            var colors = source.colorKeys;
            for (int i = 0; i < colors.Length; i++)
                colors[i].color = Color.Lerp(colors[i].color, Color.white, CoreLighten);
            var lighter = new Gradient();
            lighter.SetKeys(colors, source.alphaKeys);
            return lighter;
        }
    }
}
