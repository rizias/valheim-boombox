using System;
using System.Collections.Generic;

namespace VikingBoombox
{
    internal sealed class MusicPulse
    {
        private const double FallHalfLifeSeconds = .15;
        private const float RearmMetres = .1f;
        private readonly HashSet<int> hitThisOpening = new HashSet<int>();
        private double lastTime = double.NaN;
        private float peak;
        private bool canOpen = true;
        private long sequence, opening;
        internal Func<long> NextEvent;

        public float Strength
        {
            get;
            private set;
        }
        public float Radius
        {
            get;
            private set;
        }

        public void Reset()
        {
            Strength = 0f;
            Radius = 0f;
            lastTime = double.NaN;
            peak = 0f;
            canOpen = true;
            hitThisOpening.Clear();
        }

        public void Update(
            double time,
            float level,
            float radiusMax,
            List<MusicTarget> targets,
            Action<MusicTarget, float> hit
        )
        {
            double delta = double.IsNaN(lastTime) ? 0 : Math.Max(0, time - lastTime);
            lastTime = time;
            Strength = level >= Strength
                ? level
                : (float)(level + (Strength - level) * Math.Pow(.5, delta / FallHalfLifeSeconds));
            float previous = Radius;
            Radius = radiusMax * Strength;
            bool grows = Radius > previous;
            TrackOpenings(grows);
            if (!grows)
                return;
            foreach (var target in targets)
            {
                float edge = Math.Max(target.Edge, 0f);
                if (edge > Radius || edge > radiusMax || !hitThisOpening.Add(target.Id))
                    continue;
                var eventTarget = target;
                eventTarget.EventId = opening;
                hit(eventTarget, Strength);
            }
        }

        private void TrackOpenings(bool grows)
        {
            if (!canOpen && Radius <= peak - RearmMetres)
                canOpen = true;
            if (grows && canOpen)
            {
                canOpen = false;
                opening = NextEvent != null ? NextEvent() : ++sequence;
                hitThisOpening.Clear();
                peak = Radius;
            }
            peak = Math.Max(peak, Radius);
        }
    }
}
