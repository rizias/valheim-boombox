using BepInEx.Configuration;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxDesktopAudio : MonoBehaviour
    {
        private static ConfigFile configuration;
        private static ConfigEntry<bool> enabledSetting;
        private static ConfigEntry<float> gainSetting;
        private BoomboxMusicTap tap;
        private BoomboxDesktopCapture capture;
        private float retryAt;
        internal static bool Allowed
        {
            get
            {
                Bind();
                return enabledSetting.Value;
            }
            set
            {
                Bind();
                enabledSetting.Value = value;
            }
        }
        internal static float Gain
        {
            get
            {
                Bind();
                return gainSetting.Value;
            }
        }
        private BoomboxMessage status = BoomboxMessage.Raw(null);
        internal string Status
        {
            get => status.ToString();
            private set => status = BoomboxMessage.Raw(value);
        }
        private void SetStatus(string key, params object[] arguments) => status = new BoomboxMessage(key, arguments);

        private static void Bind()
        {
            var config = BoomboxPlugin.Instance.Config;
            if (configuration == config)
                return;
            configuration = config;
            enabledSetting =
                config.Bind(
                "Audio",
                "ComputerAudioEnabled",
                true,
                BoomboxText.Description("text.allow_analysis_of_windows_output_only_while")
            );
            gainSetting = config.Bind(
                "Audio",
                "ComputerAudioSensitivity",
                1f,
                BoomboxText.Description("text.sensitivity_of_the_computer_audio_visual_reaction", new AcceptableValueRange<float>(.1f, 5f))
            );
        }

        internal BoomboxMusicTap Poll(bool wanted)
        {
            if (!wanted || !Allowed)
            {
                capture?.Stop();
                if (capture != null && !capture.Alive)
                {
                    capture.Dispose();
                    capture = null;
                }
                SetStatus("text.computer_audio_reaction_is_off");
                return null;
            }
            if (tap == null)
                tap = gameObject.AddComponent<BoomboxMusicTap>();
            tap.InputGain = Gain;
            if (capture != null && capture.Error != null)
            {
                SetStatus("status.capture_unavailable", capture.Error);
                if (!capture.Alive)
                {
                    capture.Dispose();
                    capture = null;
                    retryAt = Time.unscaledTime + 2f;
                }
                return null;
            }
            if (capture != null && !capture.Alive)
            {
                capture.Dispose();
                capture = null;
            }
            if (capture == null && Time.unscaledTime >= retryAt)
                capture = new BoomboxDesktopCapture(tap);
            if (capture == null || !capture.Ready)
                return null;
            SetStatus("text.computer_audio_reaction_no_duplicate_playback");
            return tap;
        }
        private void OnDisable()
        {
            capture?.Stop();
        }
        internal void ResetDevice()
        {
            capture?.Stop();
            retryAt = 0f;
        }
        private void OnDestroy()
        {
            capture?.Dispose();
            capture = null;
        }
    }
}
