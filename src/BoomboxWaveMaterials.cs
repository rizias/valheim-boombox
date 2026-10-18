using System;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxWaveMaterials
    {
        private static Material alpha;

        public static Material Alpha
        {
            get
            {
                if (alpha == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader == null || !shader.isSupported)
                        throw new InvalidOperationException(BoomboxText.Get("text.the_music_wave_material_was_not_found"));
                    alpha = new Material(shader) { name = "Boombox alpha template" };
                }
                return alpha;
            }
        }

        public static void Clear()
        {
            if (alpha != null)
                UnityEngine.Object.Destroy(alpha);
            alpha = null;
        }
    }
}
