using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxEnchantStream
    {
        private struct Comet
        {
            internal float Launch;
            internal float Flight;
            internal Vector3 BackBend;
            internal Vector3 ShoulderBend;
            internal float Eased;
            internal float Carry;
            internal bool Moving;
            internal bool Arrived;
            internal bool Note;
            internal bool Right;
        }

        private const float CometsPerSpeaker = 3f;
        private const float Duration = .35f;
        private const float Flight = .3f;
        private const float Stagger = .025f;
        private const float BendJitter = .06f;
        private const float HaloSize = .14f;
        private const float HaloAlpha = .6f;
        private const float SparkSize = .09f;
        private const float NoteSize = .08f;
        private const float HeadLifetime = .1f;
        private const float HeadSpin = 540f;
        private const float TrailSpacing = .025f;
        private const float TrailJitter = .005f;
        private const float TrailDrift = .06f;
        private const float TrailSizeMin = .018f;
        private const float TrailSizeMax = .028f;
        private const float ArrivalSparks = 6f;
        private const float ArrivalSpeed = .9f;
        private readonly BoomboxEnchantHeads heads;
        private readonly BoomboxEnchantTrail trail;
        private Comet[] comets = new Comet[0];
        private bool active;

        internal BoomboxEnchantStream(Transform root, string suffix)
        {
            trail = new BoomboxEnchantTrail(root, "EnchantStream" + suffix);
            heads = new BoomboxEnchantHeads(root, suffix);
        }

        internal void Launch(float now)
        {
            int perSpeaker = BoomboxParticleLook.Count(CometsPerSpeaker);
            if (comets.Length != 2 * perSpeaker)
                comets = new Comet[2 * perSpeaker];
            for (int i = 0; i < comets.Length; i++)
            {
                int order = i % perSpeaker;
                float delay = Mathf.Min(order * Stagger, Duration - Stagger);
                comets[i] = new Comet
                {
                    Launch = now + delay,
                    Flight = Mathf.Min(Flight, Duration - delay),
                    BackBend = Random.insideUnitSphere * BendJitter,
                    ShoulderBend = Random.insideUnitSphere * BendJitter,
                    Note = order == 1,
                    Right = i >= perSpeaker
                };
            }
            heads.Reserve(comets.Length);
            active = true;
        }

        internal void Present(Vector3 left, Vector3 right, BoomboxEnchantRoute route, Vector3 target, float now)
        {
            if (!active)
            {
                trail.Sync();
                return;
            }
            heads.Begin();
            bool flying = false;
            for (int i = 0; i < comets.Length; i++)
            {
                if (comets[i].Arrived)
                    continue;
                flying = true;
                float progress = (now - comets[i].Launch) / comets[i].Flight;
                if (progress < 0f)
                    continue;
                var from = comets[i].Right ? right : left;
                var back = route.Back + comets[i].BackBend;
                var shoulder = route.Shoulder + comets[i].ShoulderBend;
                float eased = progress >= 1f ? 1f : progress * progress * (3f - 2f * progress);
                if (comets[i].Moving)
                    Scatter(ref comets[i], from, back, shoulder, target, eased);
                comets[i].Eased = eased;
                comets[i].Moving = true;
                if (progress >= 1f)
                {
                    comets[i].Arrived = true;
                    Burst(target, ArrivalSparks, ArrivalSpeed);
                    continue;
                }
                var head = BoomboxEnchantRoute.Point(from, back, shoulder, target, eased);
                heads.Add(
                    head,
                    HaloSize,
                    HaloAlpha,
                    comets[i].Note ? NoteSize : SparkSize,
                    comets[i].Note,
                    HeadLifetime,
                    comets[i].Note ? 0f : now * HeadSpin + i * 40f
                );
            }
            heads.End();
            trail.Sync();
            active = flying;
        }

        internal void Burst(Vector3 position, float count, float speed)
        {
            int sparks = BoomboxParticleLook.Count(count);
            for (int k = 0; k < sparks; k++)
                trail.Dot(position, Random.onUnitSphere * speed, Random.Range(TrailSizeMin, TrailSizeMax));
        }

        internal void Clear()
        {
            active = false;
            for (int i = 0; i < comets.Length; i++)
                comets[i].Arrived = true;
            trail.Clear();
            heads.Clear();
        }

        private void Scatter(ref Comet comet, Vector3 from, Vector3 back, Vector3 shoulder, Vector3 target, float eased)
        {
            var previous = BoomboxEnchantRoute.Point(from, back, shoulder, target, comet.Eased);
            float chord = Vector3.Distance(previous, BoomboxEnchantRoute.Point(from, back, shoulder, target, eased));
            int steps = Mathf.Max(1, Mathf.CeilToInt(chord / TrailSpacing));
            for (int step = 1; step <= steps; step++)
            {
                var point = BoomboxEnchantRoute.Point(
                    from,
                    back,
                    shoulder,
                    target,
                    Mathf.Lerp(comet.Eased, eased, (float)step / steps)
                );
                Lay(ref comet, previous, point);
                previous = point;
            }
        }

        private void Lay(ref Comet comet, Vector3 start, Vector3 end)
        {
            float distance = Vector3.Distance(start, end);
            float carried = comet.Carry;
            int count = Mathf.FloorToInt((carried + distance) / TrailSpacing);
            comet.Carry = carried + distance - count * TrailSpacing;
            if (count == 0 || distance <= 0f)
                return;
            for (int k = 1; k <= count; k++)
            {
                var point = Vector3.Lerp(start, end, (k * TrailSpacing - carried) / distance);
                trail.Dot(
                    point + Random.insideUnitSphere * TrailJitter,
                    Random.insideUnitSphere * TrailDrift,
                    Random.Range(TrailSizeMin, TrailSizeMax)
                );
            }
        }
    }
}
