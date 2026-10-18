using System;
using System.IO;
using UnityEngine;

namespace VikingBoombox
{
    internal static class MusicEffectAssets
    {
        private const string ResourceName = "ValheimBoombox.MusicEffects.bundle";
        private static AssetBundle bundle;

        public static Shader GroundLight => Load("BoomboxGroundLight");
        public static Shader MapGlow => Load("BoomboxMapGlow");
        public static Shader AirShift => Load("BoomboxAirShift");
        public static Shader MapCrack => Load("BoomboxMapCrack");
        public static Shader MapRing => Load("BoomboxMapRing");
        public static Shader WallLight => Load("BoomboxWallLight");
        public static Shader GroundShift => Load("BoomboxGroundShift");

        private static Shader Load(string name)
        {
            if (bundle == null)
            {
                using (var source = typeof(MusicEffectAssets).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (source == null)
                        throw new InvalidOperationException(BoomboxText.Get("text.embedded_music_effect_shader_bundle_is_missing"));
                    using (var bytes = new MemoryStream())
                    {
                        source.CopyTo(bytes);
                        bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                    }
                }
                if (bundle == null)
                    throw new InvalidOperationException(BoomboxText.Get("text.music_effect_shader_bundle_failed_to_load"));
            }
            var shader = bundle.LoadAsset<Shader>(name);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException(BoomboxText.Get("text.shader") + name + BoomboxText.Get("text.is_not_supported"));
            return shader;
        }

        public static void Release()
        {
            if (bundle != null)
                bundle.Unload(true);
            bundle = null;
        }
    }
}
