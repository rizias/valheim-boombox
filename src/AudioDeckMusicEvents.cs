using System;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class AudioDeck
    {
        private readonly MusicEffectFeed feed = new MusicEffectFeed();
        private MusicEffectRunner effect;
        public MusicEffectRunner Effect => effect;
        public MusicMoment Moment => feed.Moment;

        private static double Threshold => BoomboxPlugin.Instance.Settings.Effect.Current.Threshold.Value;

        private void PresentMusic(bool active)
        {
            if (active)
                foreach (var strike in feed.Moment.Fresh)
                    OnBeat(strike.Strength, strike.Time);
            effect.Step(feed.Moment, active);
            BoomboxRemoteNetwork.Observe(this, active);
        }

        internal void OnBeat(float strength, double timestamp)
        {
            bass = Mathf.Max(bass, strength);
            membranePushUntil = Math.Max(membranePushUntil, timestamp + MembranePushSeconds);
            LastBeatTime = timestamp;
            LastBeatStrength = strength;
            Vector3 origin = transform.position + transform.up * 0.2f;
            EmittedWaves++;
            BassWave.Emit(origin, State.upgradeTier, strength);
            VisualPresenter?.OnMusicEvent(strength);
            try
            {
                var model = Visual.GetComponent<VikingBoomboxVisualState>();
                BoomboxSpeakerPulse.Emit(model, strength);
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance.LogError(
                    BoomboxText.Get("text.speaker_effect_unavailable_the_music_pulse_was") + error.Message
                );
            }
        }
    }
}
