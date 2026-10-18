using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class MusicEffectRunner : MonoBehaviour
    {
        private const double JumpSeconds = 1;
        private readonly MusicStrikeTracker tracker = new MusicStrikeTracker();
        private readonly MusicPulse pulse = new MusicPulse();
        private readonly List<MusicTarget> targets = new List<MusicTarget>();
        private readonly MusicEffectFrame frame = new MusicEffectFrame();
        private AudioDeck deck;
        private IMusicEffectView view;
        private BoomboxMusicEffect shown;
        private double lastTime = double.NaN;
        private Action<MusicTarget, float> hit;
        private Action<MusicTarget, float> skip;
        private Player owner;

        public int Hits
        {
            get;
            private set;
        }

        public void Initialize(AudioDeck owner)
        {
            deck = owner;
            tracker.NextEvent = () => BoomboxMusicNetwork.Begin(deck);
            pulse.NextEvent = () => BoomboxMusicNetwork.Begin(deck);
            hit = Hit;
            skip = (target, strength) => { };
        }

        public void Step(MusicMoment moment, bool active)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            var effect = settings.Effect;
            owner = Player.m_localPlayer;
            if (!active || owner == null)
            {
                Stop();
                return;
            }
            if (BoomboxAuraNetwork.Balance == null)
            {
                Stop();
                return;
            }
            if (view == null || shown != effect.Variant.Value)
            {
                Stop();
                shown = effect.Variant.Value;
                view = Create(shown);
            }
            double time = moment.Time;
            if (double.IsNaN(lastTime) || time < lastTime || time > lastTime + JumpSeconds)
            {
                tracker.Reset();
                pulse.Reset();
                view.Hide();
                lastTime = time;
            }
            int tier = deck.State.upgradeTier;
            var center = deck.State.onBack ? owner.transform.position : deck.transform.position;
            float radius = BoomboxAuraNetwork.WaveRadius(tier);
            MusicEffectTargets.Collect(owner, center, targets);
            bool harm = effect.CreatureDamage.Value && settings.LocalGameplayEnabled;
            frame.Center = center;
            if (view is IMusicStrikeView struck)
                tracker.Update(time, moment.Fresh, targets, radius, struck.Reach, center, harm ? hit : skip);
            else
                pulse.Update(time, moment.Level, radius, targets, harm ? hit : skip);
            if (!settings.ShowVisualEffects.Value || !settings.ShowMusicEffects.Value)
            {
                view.Hide();
                lastTime = time;
                return;
            }
            frame.Time = time;
            frame.Delta = (float)Math.Max(0, time - lastTime);
            frame.Radius = radius;
            frame.Moment = moment;
            frame.Fresh = tracker.Fresh;
            frame.Strikes = tracker.Strikes;
            frame.Targets.Clear();
            foreach (var target in targets)
                if (target.Edge <= radius)
                    frame.Targets.Add(target);
            frame.ApplyLook(effect, effect.Variant.Value);
            view.Update(frame);
            lastTime = time;
        }

        private void Hit(MusicTarget target, float strength)
        {
            BoomboxMusicNetwork.Submit(deck, target.Character, target.EventId, strength);
            Hits++;
        }

        private IMusicEffectView Create(BoomboxMusicEffect variant)
        {
            if (!VikingBoomboxVisual.CanRender)
                return MusicHiddenView.Create(variant);
            return MusicEffectViews.Create(variant, transform, deck.Visual, pulse);
        }

        private void Stop()
        {
            tracker.Reset();
            pulse.Reset();
            view?.Destroy();
            view = null;
            lastTime = double.NaN;
        }

        private void OnDestroy() => view?.Destroy();
    }
}
