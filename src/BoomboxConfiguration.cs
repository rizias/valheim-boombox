using System;
using BepInEx.Configuration;

namespace VikingBoombox
{
    internal static class BoomboxConfiguration
    {
        internal static void UpdatePreview(ConfigFile config, Action update)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                update();
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
        }

        internal static BoomboxSettings Load(ConfigFile config)
        {
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                foreach (string key in new[] { "PythonPath", "DecoderPath", "FoobarPath", "CacheMaximumMegabytes" })
                    Retire(config, "Music", key);
                foreach (string key in new[] {
                    "ShowWaves",
                    "ShowDistortion",
                    "BeatVisual",
                    "ShowDust",
                    "DustColor",
                    "WaveIntensity",
                    "DustIntensity",
                    "DistortionIntensity",
                    "Color"
                })
                    Retire(config, "Wave appearance", key);
                Retire(config, "Audio", "PrepareNextTrack");
                var settings = new BoomboxSettings(config);
                config.Save();
                return settings;
            }
            finally
            {
                config.SaveOnConfigSet = saveOnSet;
            }
        }

        private static void Retire(ConfigFile config, string section, string key)
        {
            config.Bind(section, key, "");
            config.Remove(new ConfigDefinition(section, key));
        }
    }
}
