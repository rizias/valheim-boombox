using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxRemoteEffects
    {
        private readonly Transform parent;
        private readonly GameObject model;
        private readonly MusicStrikeTracker tracker = new MusicStrikeTracker();
        private readonly MusicPulse pulse = new MusicPulse();
        private readonly MusicEffectFrame frame = new MusicEffectFrame();
        private readonly MusicMoment moment = new MusicMoment();
        private readonly List<MusicTarget> targets = new List<MusicTarget>();
        private static readonly Action<MusicTarget, float> Ignore = (target, strength) => { };
        private IMusicEffectView view;
        private BoomboxMusicEffect variant;
        private double last = double.NaN;

        internal BoomboxRemoteEffects(Transform parent, GameObject model)
        {
            this.parent = parent;
            this.model = model;
        }

        internal void Present(BoomboxRemoteState state, Player owner, List<MusicStrike> fresh)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            if (!settings.ShowOthersWaves.Value || !settings.ShowVisualEffects.Value || !settings.ShowMusicEffects.Value
            || !state.Active)
            {
                Stop();
                return;
            }
            if (view == null || variant != state.Variant)
            {
                Stop();
                variant = state.Variant;
                view = MusicEffectViews.Create(variant, parent, model, pulse);
            }
            double time = state.Time + Math.Max(0f, Time.realtimeSinceStartup - state.Received);
            if (!double.IsNaN(last) && (time < last || time > last + 1))
            {
                tracker.Reset();
                pulse.Reset();
                view.Hide();
            }
            moment.Time = time;
            moment.Level = state.Level;
            moment.Silent = state.Silent;
            moment.Smooth = state.Smooth;
            moment.Fresh.Clear();
            moment.Fresh.AddRange(fresh);
            Vector3 center = state.Worn && owner != null ? owner.transform.position : parent.position;
            if (owner != null)
                MusicEffectTargets.Collect(owner, center, targets);
            else
                targets.Clear();
            if (view is IMusicStrikeView strike)
                tracker.Update(time, fresh, targets, state.Radius, strike.Reach, center, Ignore);
            else
                pulse.Update(time, state.Level, state.Radius, targets, Ignore);
            frame.Time = time;
            frame.Delta = double.IsNaN(last) ? 0f : (float)Math.Max(0, time - last);
            frame.Center = center;
            frame.Radius = state.Radius;
            frame.Moment = moment;
            frame.Fresh = tracker.Fresh;
            frame.Strikes = tracker.Strikes;
            frame.Targets.Clear();
            foreach (var target in targets)
                if (target.Edge <= state.Radius)
                    frame.Targets.Add(target);
            frame.ApplyLook(settings.Effect, variant);
            view.Update(frame);
            last = time;
        }

        internal void Stop()
        {
            view?.Destroy();
            view = null;
            tracker.Reset();
            pulse.Reset();
            targets.Clear();
            last = double.NaN;
        }
    }
}
