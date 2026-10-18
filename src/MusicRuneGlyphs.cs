using UnityEngine;

namespace VikingBoombox
{
    internal static class MusicRuneGlyphs
    {
        public static readonly Vector2[][][] Strokes =
        {
            new[]
            {
                new[] { V(0f, .04f), V(0f, .97f) },
                new[] {
                    V(0f, .34f),
                    V(.28f, .43f),
                    V(.41f, .55f),
                    V(.5f, .64f),
                    V(.56f, .72f),
                    V(.6f, .81f),
                    V(.62f, .89f),
                    V(.63f, .97f)
                },
                new[] { V(0f, .61f), V(.14f, .66f), V(.25f, .72f), V(.33f, .81f), V(.37f, .89f), V(.39f, .97f) },
            },
            new[]
            {
                new[] {
                    V(0f, .04f),
                    V(0f, .95f),
                    V(.14f, .95f),
                    V(.28f, .9f),
                    V(.4f, .81f),
                    V(.45f, .72f),
                    V(.48f, .6f),
                    V(.48f, .04f)
                },
            },
            new[]
            {
                new[] { V(0f, .04f), V(0f, .97f) },
                new[] {
                    V(0f, .83f),
                    V(.17f, .8f),
                    V(.28f, .73f),
                    V(.35f, .64f),
                    V(.37f, .55f),
                    V(.37f, .47f),
                    V(.34f, .38f),
                    V(.26f, .29f),
                    V(.1f, .22f),
                    V(0f, .19f)
                },
            },
            new[]
            {
                new[] { V(0f, .04f), V(0f, .97f) },
                new[] { V(-.24f, .74f), V(.24f, .55f) },
                new[] { V(-.24f, .47f), V(.24f, .28f) },
            },
            new[]
            {
                new[] { V(0f, .04f), V(0f, .97f), V(.43f, .71f), V(.2f, .48f), V(.45f, .02f) },
            },
            new[]
            {
                new[] { V(0f, .04f), V(0f, .97f) },
                new[] {
                    V(0f, .48f),
                    V(.17f, .54f),
                    V(.31f, .64f),
                    V(.39f, .72f),
                    V(.43f, .81f),
                    V(.46f, .89f),
                    V(.46f, .97f)
                },
            },
            new[]
            {
                new[] { V(0f, .04f), V(0f, .97f) },
                new[] { V(-.24f, .73f), V(.24f, .29f) },
                new[] { V(.24f, .73f), V(-.24f, .29f) },
            },
            new[]
            {
                new[] { V(0f, .04f), V(0f, .97f) },
                new[] { V(-.24f, .63f), V(.24f, .38f) },
            },
        };

        public static readonly float[] Centers = CentersOf();

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static float[] CentersOf()
        {
            var centers = new float[Strokes.Length];
            for (int glyph = 0; glyph < Strokes.Length; glyph++)
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (var stroke in Strokes[glyph])
                    foreach (var point in stroke)
                    {
                        min = Mathf.Min(min, point.x);
                        max = Mathf.Max(max, point.x);
                    }
                centers[glyph] = (min + max) / 2f;
            }
            return centers;
        }
    }
}
