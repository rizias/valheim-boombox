using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxParticleTextures
    {
        private const int Size = 64;
        private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(
            StringComparer.Ordinal
        );

        internal static Texture2D Texture(string kind, float glow)
        {
            string key = kind + ":" + glow.ToString("R", CultureInfo.InvariantCulture);
            if (textures.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var texture = new Texture2D(
                Size,
                Size,
                TextureFormat.RGBA32,
                true
            )
            {
                name = "Boombox particle " + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var point = new Vector2((x + .5f) / Size * 2f - 1f, (y + .5f) / Size * 2f - 1f);
                    float distance = Distance(kind, point);
                    float fill = Mathf.Clamp01(.5f - distance * Size * .5f);
                    float edge = Mathf.Clamp01((1f - point.magnitude) * 4f);
                    float halo = glow * Mathf.Exp(-Mathf.Pow(Mathf.Max(distance, 0f) * 3.5f, 2f)) * edge;
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(Mathf.Max(fill, halo) * 255f));
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            textures[key] = texture;
            return texture;
        }

        internal static void Clear()
        {
            foreach (var texture in textures.Values)
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            textures.Clear();
        }

        private static float Distance(string kind, Vector2 p)
        {
            switch (kind)
            {
                case "glow":
                    return p.magnitude;
                case "dot":
                    return p.magnitude - .3f;
                case "cross":
                    return Mathf.Min(
                        Box(p, Vector2.zero, new Vector2(.55f, .17f)),
                        Box(p, Vector2.zero, new Vector2(.17f, .55f))
                    );
                case "sparkle":
                    return Mathf.Min(
                        Polygon(
                            p,
                            new Vector2(-.8f, 0f),
                            new Vector2(0f, -.11f),
                            new Vector2(.8f, 0f),
                            new Vector2(0f, .11f)
                        ),
                        Polygon(
                            p,
                            new Vector2(0f, -.8f),
                            new Vector2(.11f, 0f),
                            new Vector2(0f, .8f),
                            new Vector2(-.11f, 0f)
                        )
                    );
                case "note":
                    return Note(p);
                case "ring":
                    return Mathf.Abs(p.magnitude - .92f) - .08f;
                default:
                    throw new InvalidOperationException(
                        "Unknown texture " + kind + "; expected glow, dot, cross, sparkle, note or ring"
                    );
            }
        }

        private static float Note(Vector2 p)
        {
            var head = Quaternion.Euler(0f, 0f, -25f) * (p - new Vector2(-.2f, -.4f));
            float round = new Vector2(head.x / 1.35f, head.y).magnitude - .2f;
            return Mathf.Min(
                Mathf.Min(round, Box(p, new Vector2(.1f, .12f), new Vector2(.055f, .52f))),
                Segment(p, new Vector2(.13f, .6f), new Vector2(.45f, .22f)) - .075f
            );
        }

        private static float Box(Vector2 p, Vector2 center, Vector2 half)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x) - half.x, Mathf.Abs(p.y - center.y) - half.y);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        private static float Polygon(Vector2 p, params Vector2[] points)
        {
            float inside = float.NegativeInfinity, outside = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                var next = points[(i + 1) % points.Length];
                var edge = next - points[i];
                inside = Mathf.Max(inside, Vector2.Dot(p - points[i], new Vector2(edge.y, -edge.x).normalized));
                outside = Mathf.Min(outside, Segment(p, points[i], next));
            }
            return inside <= 0f ? inside : outside;
        }

        private static float Segment(Vector2 p, Vector2 from, Vector2 to)
        {
            var line = to - from;
            float along = Mathf.Clamp01(Vector2.Dot(p - from, line) / line.sqrMagnitude);
            return (p - from - line * along).magnitude;
        }
    }
}
