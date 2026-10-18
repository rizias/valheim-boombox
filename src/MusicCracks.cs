using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal struct MusicCrackPoint
    {
        public float X, Z, Radial, Width;
    }

    internal struct MusicEmberSpot
    {
        public float X, Z, Radial, Rise;
    }

    internal sealed class MusicCrack
    {
        public double Born;
        public float Strength;
        public float Reach;
        public readonly List<List<MusicCrackPoint>> Lines = new List<List<MusicCrackPoint>>();
        public readonly List<MusicEmberSpot> Embers = new List<MusicEmberSpot>();
        public double FadeFrom = double.NaN;
        public float Shine = 1f;
    }

    internal static class MusicCracks
    {
        public const float StartDistance = 0f;
        public const float MinAim = .25f;
        public const int RandomCount = 3;
        public const float MinGapDegrees = 60f;
        public const float HealSeconds = 4f;
        public const int Max = 64;
        public const int Slots = Max + Max / 4;
        public const float CapFadeSeconds = .3f;
        public const float SilenceFadeSeconds = .3f;
        public const float EmberLife = .6f;
        public const int HitEmbers = 8;
        private const float SegmentMin = .2f, SegmentMax = .4f;
        private const float TurnDegrees = 25f;
        private const float CalmFrom = 1.8f;
        private const float CalmShare = .5f;
        private const int WalkAttempts = 8;
        private const float BranchEvery = .8f;
        private const float BranchMin = .3f, BranchMax = 1f;
        private const float BranchAngleMin = 30f, BranchAngleMax = 60f;
        private const float BranchWidthShare = .7f;
        private const float Width = .03f, MinWidth = .015f, EndWidth = .005f;
        private const float EmberEvery = .5f;
        private const float EmbersPerStrength = 10f;
        private const float EmberRiseMin = .2f, EmberRiseMax = .5f;
        private const float BodyRadius = .25f;
        private const float GlowHalfLife = .3f;
        private const float ScarShade = .25f;
        private const float Smoulder = .3f;
        private const float RingBrightness = 1.4f;
        private const int MaxSteps = 200;
        private static readonly float Turn = TurnDegrees * Mathf.Deg2Rad;
        private static readonly float Approach = 2 * SegmentMax;

        public static MusicCrack Build(
            Vector2 aim,
            bool hit,
            float strength,
            float amount,
            double born,
            System.Random random
        )
        {
            float reach = aim.magnitude;
            var main = Tapered(Steered(aim, random), Math.Max(MinWidth, Width * strength), reach);
            var crack = new MusicCrack { Born = born, Strength = strength, Reach = reach };
            crack.Lines.Add(main);
            crack.Lines.AddRange(Branches(main, reach, random));
            crack.Embers.AddRange(
                EmberSpots(main, MusicAnalysisParams.Round(EmbersPerStrength * strength * amount), random)
            );
            if (hit)
                crack.Embers.AddRange(HitSpots(aim, reach, HitCount(amount), random));
            return crack;
        }

        public static void Shift(MusicCrack crack, Vector3 origin)
        {
            foreach (var line in crack.Lines)
                for (int index = 0; index < line.Count; index++)
                {
                    var point = line[index];
                    point.X += origin.x;
                    point.Z += origin.z;
                    line[index] = point;
                }
            for (int index = 0; index < crack.Embers.Count; index++)
            {
                var spot = crack.Embers[index];
                spot.X += origin.x;
                spot.Z += origin.z;
                crack.Embers[index] = spot;
            }
        }

        public static void CutOffGround(MusicCrack crack, Func<float, float, bool> onGround, int hitEmbers)
        {
            foreach (var line in crack.Lines)
            {
                int keep = line.FindIndex(point => !onGround(point.X, point.Z));
                if (keep >= 0)
                    line.RemoveRange(keep, line.Count - keep);
            }
            crack.Lines.RemoveAll(line => line.Count < 2);
            int along = crack.Embers.Count - hitEmbers;
            for (int index = along - 1; index >= 0; index--)
                if (!onGround(crack.Embers[index].X, crack.Embers[index].Z))
                    crack.Embers.RemoveAt(index);
        }

        public static List<float> FreeAngles(List<float> taken, int count, System.Random random)
        {
            var chosen = new List<float>();
            if (count <= 0)
                return chosen;
            float gap = MinGapDegrees * Mathf.Deg2Rad, turn = 2 * Mathf.PI;
            if (taken.Count == 0)
            {
                float first = (float)random.NextDouble() * turn;
                chosen.Add(first);
                chosen.AddRange(FreeAngles(new List<float> { first }, count - 1, random));
                return chosen;
            }
            var sorted = taken.Select(angle => (angle % turn + turn) % turn).OrderBy(angle => angle).ToList();
            var arcs = new List<(float Start, float Length, int Room)>();
            for (int index = 0; index < sorted.Count; index++)
            {
                float next = index + 1 < sorted.Count ? sorted[index + 1] : sorted[0] + turn;
                float length = next - sorted[index] - 2 * gap;
                arcs.Add((sorted[index] + gap, length, length >= -1e-6f
                    ? Mathf.FloorToInt(length / gap + 1e-6f) + 1
                    : 0));
            }
            for (int index = arcs.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                var swap = arcs[index];
                arcs[index] = arcs[other];
                arcs[other] = swap;
            }
            foreach (var arc in arcs)
            {
                int placed = Math.Min(arc.Room, count - chosen.Count);
                if (placed <= 0)
                    continue;
                float slack = Math.Max(0f, arc.Length - (placed - 1) * gap);
                var offsets = Enumerable.Range(0, placed).Select(_ => (float)random.NextDouble() * slack).OrderBy(value => value).ToList();
                for (int index = 0; index < placed; index++)
                    chosen.Add(((arc.Start + offsets[index] + index * gap) % turn + turn) % turn);
            }
            return chosen;
        }

        public static float FrontRadius(
            double age,
            float reach
        ) => reach * (1f - Mathf.Pow(1f - Mathf.Clamp01((float)age / .15f), 3f));

        public static double Arrival(float radial, float reach) => reach <= 0f
            ? 0
            : .15 * (1 - Math.Pow(Math.Max(0, 1 - Math.Min(radial, reach) / reach), 1.0 / 3));

        public static Vector4 Look(MusicCrack crack, double time, float brightness, float bass)
        {
            double age = time - crack.Born;
            float heal = Mathf.Max(0f, 1f - (float)age / HealSeconds);
            float hot = brightness * crack.Strength * Mathf.Pow(.5f, (float)age / GlowHalfLife);
            float smoulder = Smoulder * brightness * bass * heal / RingBrightness;
            float fade = crack.Shine * CapFade(crack, time);
            return new Vector4(
                FrontRadius(age, crack.Reach),
                Mathf.Max(hot, smoulder) * fade,
                (1f - ScarShade) * heal * fade,
                age < .15
                    ? 1f
                    : 0f
            );
        }

        public static int HitCount(float amount) => MusicAnalysisParams.Round(HitEmbers * amount);

        public static float CapFade(MusicCrack crack, double time) => double.IsNaN(crack.FadeFrom)
            ? 1f
            : Mathf.Max(0f, 1f - (float)(time - crack.FadeFrom) / CapFadeSeconds);

        public static float EmberHeight(double since, float rise)
        {
            if (since < 0 || since > EmberLife)
                return -1f;
            float share = (float)since / EmberLife;
            return 4f * rise * share * (1f - share);
        }

        private static float Uniform(
            System.Random random,
            float low,
            float high
        ) => low + (high - low) * (float)random.NextDouble();

        private static float Wrap(float angle) => Mathf.Atan2(Mathf.Sin(angle), Mathf.Cos(angle));

        private static float Calm(
            float remaining
        ) => Mathf.Clamp(
            CalmShare + (1f - CalmShare) * ((remaining - Approach) / (CalmFrom - Approach)),
            CalmShare,
            1f
        );

        private static List<Vector2> Steered(Vector2 target, System.Random random)
        {
            float distance = target.magnitude;
            var start = distance > 0f ? target / distance * StartDistance : Vector2.zero;
            for (int attempt = 0; attempt < WalkAttempts; attempt++)
            {
                var path = Walk(start, target, random);
                if (path != null)
                    return path;
            }
            int pieces = Math.Max(1, Mathf.CeilToInt((target - start).magnitude / SegmentMax));
            return Enumerable.Range(
                0,
                pieces + 1
            ).Select(index => Vector2.Lerp(
                    start,
                    target,
                    index / (float)pieces
                )).ToList();
        }

        private static List<Vector2> Walk(Vector2 start, Vector2 target, System.Random random)
        {
            var points = new List<Vector2> { start };
            var at = start;
            float heading = Mathf.Atan2(target.x - at.x, target.y - at.y);
            for (int step = 0; step < MaxSteps; step++)
            {
                float remaining = (target - at).magnitude;
                float error = Wrap(Mathf.Atan2(target.x - at.x, target.y - at.y) - heading);
                if (remaining <= SegmentMax)
                {
                    if (Mathf.Abs(error) > Turn)
                        return null;
                    points.Add(target);
                    return points;
                }
                heading += Mathf.Clamp(
                    error + ((float)random.NextDouble() * 2f - 1f) * Turn * Calm(remaining),
                    -Turn,
                    Turn
                );
                float length = remaining <= Approach ? remaining / 2f : Uniform(random, SegmentMin, SegmentMax);
                at += new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * length;
                points.Add(at);
            }
            return null;
        }

        private static List<MusicCrackPoint> Tapered(List<Vector2> path, float startWidth, float reach)
        {
            var along = new float[path.Count];
            for (int index = 1; index < path.Count; index++)
                along[index] = along[index - 1] + (path[index] - path[index - 1]).magnitude;
            float total = Mathf.Max(1e-6f, along[along.Length - 1]);
            return path.Select((point, index) => new MusicCrackPoint
            {
                X = point.x,
                Z = point.y,
                Radial = Mathf.Min(reach, point.magnitude),
                Width = startWidth + (EndWidth - startWidth) * (along[index] / total)
            }).ToList();
        }

        private static IEnumerable<List<MusicCrackPoint>> Branches(
            List<MusicCrackPoint> main,
            float reach,
            System.Random random
        )
        {
            for (int index = 1; index < main.Count - 1; index++)
            {
                var previous = main[index - 1];
                var joint = main[index];
                float segment = new Vector2(joint.X - previous.X, joint.Z - previous.Z).magnitude;
                if (random.NextDouble() >= segment / BranchEvery || new Vector2(joint.X, joint.Z).magnitude >= reach)
                    continue;
                float side = random.NextDouble() < .5 ? -1f : 1f;
                float heading = Mathf.Atan2(
                    joint.X - previous.X,
                    joint.Z - previous.Z
                )
                    + side * Uniform(random, BranchAngleMin, BranchAngleMax) * Mathf.Deg2Rad;
                var path = BranchPath(joint, heading, reach, random);
                if (path.Count > 1)
                    yield return Tapered(path, BranchWidthShare * joint.Width, reach);
            }
        }

        private static List<Vector2> BranchPath(MusicCrackPoint from, float heading, float limit, System.Random random)
        {
            float length = Uniform(random, BranchMin, BranchMax);
            var path = new List<Vector2> { new Vector2(from.X, from.Z) };
            var at = new Vector2(from.X, from.Z);
            float walked = 0f, direction = heading;
            while (walked < length - 1e-6f)
            {
                float step = Mathf.Min(Uniform(random, SegmentMin, SegmentMax), length - walked);
                at += new Vector2(Mathf.Sin(direction), Mathf.Cos(direction)) * step;
                if (at.magnitude > limit)
                    break;
                path.Add(at);
                walked += step;
                direction += ((float)random.NextDouble() * 2f - 1f) * Turn;
            }
            return path;
        }

        private static IEnumerable<MusicEmberSpot> EmberSpots(
            List<MusicCrackPoint> main,
            int most,
            System.Random random
        )
        {
            int made = 0;
            float next = EmberEvery, along = 0f;
            for (int index = 1; index < main.Count && made < most; index++)
            {
                var from = main[index - 1];
                var to = main[index];
                float segment = new Vector2(to.X - from.X, to.Z - from.Z).magnitude;
                while (next <= along + segment && made < most)
                {
                    float share = segment > 0f ? (next - along) / segment : 0f;
                    yield return new MusicEmberSpot
                    {
                        X = from.X + (to.X - from.X) * share,
                        Z = from.Z + (to.Z - from.Z) * share,
                        Radial = from.Radial + (to.Radial - from.Radial) * share,
                        Rise = Uniform(random, EmberRiseMin, EmberRiseMax)
                    };
                    made++;
                    next += EmberEvery;
                }
                along += segment;
            }
        }

        private static IEnumerable<MusicEmberSpot> HitSpots(Vector2 aim, float reach, int count, System.Random random)
        {
            var unit = reach > 0f ? aim / reach : Vector2.up;
            for (int index = 0; index < count; index++)
            {
                float angle = (float)random.NextDouble() * 2f * Mathf.PI;
                float offset = BodyRadius * Mathf.Sqrt((float)random.NextDouble());
                yield return new MusicEmberSpot
                {
                    X = aim.x + unit.x * BodyRadius + Mathf.Sin(angle) * offset,
                    Z = aim.y + unit.y * BodyRadius + Mathf.Cos(angle) * offset,
                    Radial = reach,
                    Rise = Uniform(random, EmberRiseMin, EmberRiseMax)
                };
            }
        }
    }
}
