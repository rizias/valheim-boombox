using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxPlayerIcons
    {
        private const int Size = 64;
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        internal static Sprite Get(string name)
        {
            if (cache.TryGetValue(name, out var sprite) && sprite != null)
                return sprite;
            var texture = new Texture2D(
                Size,
                Size,
                TextureFormat.RGBA32,
                false
            )
            {
                name = "BoomboxPlayerIcon_" + name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var point = new Vector2((x + .5f) / Size * 2f - 1f, (y + .5f) / Size * 2f - 1f);
                    float alpha = Mathf.Clamp01(.5f - Distance(name, point) * Size * .5f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100f);
            sprite.name = texture.name;
            cache[name] = sprite;
            return sprite;
        }

        private static float Distance(string name, Vector2 p)
        {
            switch (name)
            {
                case "previous":
                    return Mathf.Min(
                        Box(p, new Vector2(-.66f, 0), new Vector2(.07f, .52f)),
                        Mathf.Min(
                            Polygon(p, new Vector2(-.56f, 0), new Vector2(-.02f, -.52f), new Vector2(-.02f, .52f)),
                            Polygon(p, new Vector2(.02f, 0), new Vector2(.56f, -.52f), new Vector2(.56f, .52f))
                        )
                    );
                case "next":
                    return Distance("previous", new Vector2(-p.x, p.y));
                case "play":
                    return Polygon(p, new Vector2(-.42f, -.6f), new Vector2(.6f, 0), new Vector2(-.42f, .6f));
                case "pause":
                    return Mathf.Min(
                        Box(p, new Vector2(-.28f, 0), new Vector2(.14f, .58f)),
                        Box(p, new Vector2(.28f, 0), new Vector2(.14f, .58f))
                    );
                case "repeat":
                    return Mathf.Min(
                        Arc(p, Vector2.zero, .54f, .1f, 60f, 345f),
                        Polygon(p, new Vector2(.18f, .22f), new Vector2(.72f, .34f), new Vector2(.18f, .83f))
                    );
                case "sound":
                    return Mathf.Min(
                        Mathf.Min(
                            Box(p, new Vector2(-.55f, 0), new Vector2(.14f, .22f)),
                            Polygon(
                                p,
                                new Vector2(-.44f, -.22f),
                                new Vector2(.02f, -.6f),
                                new Vector2(.02f, .6f),
                                new Vector2(-.44f, .22f)
                            )
                        ),
                        Arc(p, new Vector2(.02f, 0), .42f, .08f, -50f, 50f)
                    );
                case "muted":
                    return Mathf.Min(
                        Mathf.Min(
                            Box(p, new Vector2(-.55f, 0), new Vector2(.14f, .22f)),
                            Polygon(
                                p,
                                new Vector2(-.44f, -.22f),
                                new Vector2(.02f, -.6f),
                                new Vector2(.02f, .6f),
                                new Vector2(-.44f, .22f)
                            )
                        ),
                        Mathf.Min(
                            Box(Turn(p - new Vector2(.43f, 0), 45f), Vector2.zero, new Vector2(.3f, .07f)),
                            Box(Turn(p - new Vector2(.43f, 0), -45f), Vector2.zero, new Vector2(.3f, .07f))
                        )
                    );
                case "minus":
                    return Box(p, Vector2.zero, new Vector2(.55f, .09f));
                case "plus":
                    return Mathf.Min(
                        Box(p, Vector2.zero, new Vector2(.55f, .09f)),
                        Box(p, Vector2.zero, new Vector2(.09f, .55f))
                    );
                case "back":
                    return Distance("play", new Vector2(-p.x, p.y));
                case "up":
                    return Polygon(p, new Vector2(-.6f, -.36f), new Vector2(.6f, -.36f), new Vector2(0f, .5f));
                case "down":
                    return Distance("up", new Vector2(p.x, -p.y));
                case "cross":
                    return Mathf.Min(
                        Box(Turn(p, 45f), Vector2.zero, new Vector2(.6f, .09f)),
                        Box(Turn(p, -45f), Vector2.zero, new Vector2(.6f, .09f))
                    );
                case "check":
                    return Mathf.Min(
                        Box(Turn(p - new Vector2(-.3f, -.1f), -45f), Vector2.zero, new Vector2(.28f, .09f)),
                        Box(Turn(p - new Vector2(.2f, .05f), 49f), Vector2.zero, new Vector2(.46f, .09f))
                    );
                default:
                    return 1f;
            }
        }

        private static Vector2 Turn(Vector2 p, float degrees)
        {
            float radians = -degrees * Mathf.Deg2Rad;
            return new Vector2(
                p.x * Mathf.Cos(radians) - p.y * Mathf.Sin(radians),
                p.x * Mathf.Sin(radians) + p.y * Mathf.Cos(radians)
            );
        }

        private static float Box(Vector2 p, Vector2 center, Vector2 half)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x) - half.x, Mathf.Abs(p.y - center.y) - half.y);
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0);
        }

        private static float Polygon(Vector2 p, params Vector2[] points)
        {
            float distance = float.NegativeInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                var edge = points[(i + 1) % points.Length] - points[i];
                var outward = new Vector2(edge.y, -edge.x).normalized;
                distance = Mathf.Max(distance, Vector2.Dot(p - points[i], outward));
            }
            return distance;
        }

        private static float Arc(Vector2 p, Vector2 center, float radius, float width, float from, float to)
        {
            var local = p - center;
            float angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            if (angle < 0)
                angle += 360f;
            float first = (from + 360f) % 360f, last = (to + 360f) % 360f;
            bool inside = first <= last ? angle >= first && angle <= last : angle >= first || angle <= last;
            if (inside)
                return Mathf.Abs(local.magnitude - radius) - width;
            var start = center + new Vector2(Mathf.Cos(from * Mathf.Deg2Rad), Mathf.Sin(from * Mathf.Deg2Rad)) * radius;
            var end = center + new Vector2(Mathf.Cos(to * Mathf.Deg2Rad), Mathf.Sin(to * Mathf.Deg2Rad)) * radius;
            return Mathf.Min((p - start).magnitude, (p - end).magnitude) - width;
        }
    }
}
