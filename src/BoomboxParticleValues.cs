using System;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxParticleValues
    {
        internal static ParticleSystemRenderMode RenderMode(string mode)
        {
            switch (mode)
            {
                case "billboard":
                    return ParticleSystemRenderMode.Billboard;
                case "stretch":
                    return ParticleSystemRenderMode.Stretch;
                case "coin":
                    return ParticleSystemRenderMode.Mesh;
                default:
                    throw new InvalidOperationException(
                        "Unknown renderMode " + mode + "; expected billboard, stretch or coin"
                    );
            }
        }

        internal static ParticleSystemSimulationSpace Space(string space)
        {
            switch (space)
            {
                case "local":
                    return ParticleSystemSimulationSpace.Local;
                case "boombox":
                    return ParticleSystemSimulationSpace.Custom;
                case "world":
                    return ParticleSystemSimulationSpace.World;
                default:
                    throw new InvalidOperationException("Unknown space " + space + "; expected local, boombox or world");
            }
        }

        internal static Vector2 Pair(float[] values)
        {
            if (values == null || values.Length == 0)
                return Vector2.zero;
            if (values.Length > 2)
                throw new InvalidOperationException("Range needs one or two numbers");
            return new Vector2(values[0], values[values.Length - 1]);
        }

        internal static ParticleSystem.MinMaxCurve Range(float[] values, float factor)
        {
            var pair = Pair(values) * factor;
            return pair.x == pair.y
                ? new ParticleSystem.MinMaxCurve(pair.x)
                : new ParticleSystem.MinMaxCurve(pair.x, pair.y);
        }

        internal static float Longest(float[] values) => values == null || values.Length == 0 ? 0f : values.Max();

        internal static Vector3 Vector(float[] values)
        {
            if (values == null)
                return Vector3.zero;
            if (values.Length != 3)
                throw new InvalidOperationException("Vector needs three numbers");
            return new Vector3(values[0], values[1], values[2]);
        }

        internal static AnimationCurve Curve(float[][] keys)
        {
            if (keys.Any(k => k.Length != 2))
                throw new InvalidOperationException("Curve key needs [time, value]");
            return new AnimationCurve(keys.Select(k => new Keyframe(k[0], k[1])).ToArray());
        }

        internal static Gradient Gradient(float[][] keys)
        {
            if (keys.Any(k => k.Length != 5))
                throw new InvalidOperationException("Gradient key needs [time, red, green, blue, alpha]");
            if (keys.Length > 8)
                throw new InvalidOperationException("Unity gradients hold at most 8 keys");
            var gradient = new Gradient();
            gradient.SetKeys(
                keys.Select(k => new GradientColorKey(new Color(k[1], k[2], k[3]), k[0])).ToArray(),
                keys.Select(k => new GradientAlphaKey(k[4], k[0])).ToArray()
            );
            return gradient;
        }
    }
}
