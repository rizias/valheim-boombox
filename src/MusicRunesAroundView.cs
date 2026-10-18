using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal sealed class MusicRunesAroundView : IMusicStrikeView
    {
        private const int Count = 16;
        private const float Height = .15f;
        private const float FlashHeight = .45f;
        private const float LineShare = 1f / 15f;
        private const float HaloWidths = 3f;
        private const float RestShare = .5f;
        private const float FlickerDepth = .8f;
        private const float Flash = 2f;
        private const float Low = .5f;
        private const float High = 1.5f;
        private const float OrbitMin = 1f;
        private const float OrbitMax = 1.5f;
        private const float SlowTurn = 4f;
        private const float FastTurn = 1f;
        private const float FlightSeconds = .15f;
        private const float ReturnSeconds = .5f;
        private const float Scatter = .5f;
        private const float Tilt = .35f;

        private sealed class Flight
        {
            public double Start;
            public Vector3 From;
            public Vector3? Target;
            public float Strength;
            public Color Color;
            public bool Pending;
        }

        private readonly GameObject holder;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly Texture2D texture;
        private readonly float[] angles = new float[Count];
        private readonly float[] tilts = new float[Count];
        private readonly Flight[] flights = new Flight[Count];
        private readonly Vector3[] positions = new Vector3[Count];
        private readonly System.Random random = new System.Random(29);
        private readonly MusicSparkPalette palette = new MusicSparkPalette();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> indices = new List<int>();
        private readonly HashSet<int> used = new HashSet<int>();

        private readonly MusicViewScene scene;
        public MusicRunesAroundView(MusicViewScene scene = null)
        {
            this.scene = scene;
            for (int index = 0; index < Count; index++)
            {
                angles[index] = 2 * Mathf.PI * index / Count;
                tilts[index] = (float)random.NextDouble() * 2 * Mathf.PI;
            }
            texture = SoftLine();
            material = BoomboxParticleAssets.Glow(texture);
            mesh = new Mesh { name = "Boombox music runes", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            holder = new GameObject("Boombox music runes");
            scene?.Own(holder);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            holder.SetActive(false);
        }

        public MusicReach Reach
        {
            get;
        } = MusicReach.Flight(FlightSeconds, FlightSeconds + ReturnSeconds);

        public void Update(MusicEffectFrame frame)
        {
            bool silent = frame.Moment.Silent;
            var base_ = silent
                ? palette.Current
                : palette.Update(frame.ColorMode, frame.Color, frame.Moment.Smooth, frame.Time);
            if (!silent)
                foreach (var flight in flights)
                    if (flight != null && flight.Pending)
                    {
                        flight.Color = base_;
                        flight.Pending = false;
                    }
            var smooth = frame.Moment.Smooth;
            float radius = OrbitMin + (OrbitMax - OrbitMin) * (float)MusicAnalysisParams.LevelOf(smooth.Low);
            float turn = SlowTurn - (SlowTurn - FastTurn) * (float)MusicAnalysisParams.LevelOf(smooth.Mid);
            float flicker = (float)MusicAnalysisParams.LevelOf(smooth.High);
            for (int index = 0; index < Count; index++)
                angles[index] += 2 * Mathf.PI * frame.Delta / turn;
            int shown = Mathf.Clamp(Mathf.RoundToInt(8 * frame.Amount), 1, Count);
            foreach (var strike in frame.Fresh)
                Launch(strike, frame, radius, base_, shown, silent);
            float scale = Mathf.Max(1e-4f, Flash * frame.Brightness);
            material.SetColor("_TintColor", new Color(.5f * scale, .5f * scale, .5f * scale, .5f));
            var viewCamera = scene?.Camera ?? Camera.main;
            var view = viewCamera != null ? viewCamera.transform : null;
            var right = view != null ? view.right : Vector3.right;
            var up = view != null ? view.up : Vector3.up;
            var forward = view != null ? view.forward : Vector3.forward;
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            indices.Clear();
            for (int index = 0; index < Count; index++)
            {
                Place(index, frame, radius, flicker, base_, out float size, out float light, out Color color);
                if (!MusicOrbit.Visible(index, shown, Count))
                    continue;
                var tint = MusicEffectColors.Vertex(color * (light / scale));
                Draw(
                    index,
                    positions[index],
                    size,
                    right,
                    up,
                    forward,
                    new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), 1f)
                );
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            holder.SetActive(!silent);
        }

        public void Hide()
        {
            holder.SetActive(false);
            Array.Clear(flights, 0, flights.Length);
            palette.Reset();
        }

        public void Destroy()
        {
            if (holder != null)
                UnityEngine.Object.Destroy(holder);
            UnityEngine.Object.Destroy(mesh);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(texture);
        }

        private Vector3 Home(int index, Vector3 center, float radius)
        {
            float angle = angles[index];
            float lift = Mathf.Sin(angle * 2 + tilts[index]) * Tilt * radius;
            float height = Mathf.Clamp((Low + High) / 2 + lift, Low, High);
            return center + new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
        }

        private void Launch(
            MusicLiveStrike strike,
            MusicEffectFrame frame,
            float radius,
            Color color,
            int shown,
            bool silent
        )
        {
            if (frame.Targets.Count == 0)
            {
                for (int index = 0; index < Count; index++)
                    if (flights[index] == null && MusicOrbit.Visible(index, shown, Count))
                        flights[index] = new Flight
                        {
                            Start = strike.Time,
                            From = Home(index, frame.Center, radius),
                            Strength = strike.Strength,
                            Color = color,
                            Pending = silent
                        };
                return;
            }
            used.Clear();
            foreach (var target in frame.Targets)
            {
                var offset = target.Character.transform.position - frame.Center;
                float toward = Mathf.Atan2(offset.x, offset.z);
                int pick = Nearest(toward, shown, index => flights[index] == null);
                if (pick < 0)
                    pick = Nearest(
                        toward,
                        shown,
                        index => flights[index] != null && strike.Time - flights[index].Start >= FlightSeconds
                    );
                if (pick < 0)
                    continue;
                used.Add(pick);
                var from = flights[pick] == null ? Home(pick, frame.Center, radius) : positions[pick];
                flights[pick] = new Flight
                {
                    Start = strike.Time,
                    From = from,
                    Target = target.Center,
                    Strength = strike.Strength,
                    Color = color,
                    Pending = silent
                };
            }
        }

        private int Nearest(float toward, int shown, Func<int, bool> allowed)
        {
            int best = -1;
            float gap = float.MaxValue;
            for (int index = 0; index < Count; index++)
            {
                if (used.Contains(index) || !MusicOrbit.Visible(index, shown, Count) || !allowed(index))
                    continue;
                float candidate = MusicOrbit.Gap(angles[index], toward);
                if (candidate < gap)
                {
                    gap = candidate;
                    best = index;
                }
            }
            return best;
        }

        private void Place(
            int index,
            MusicEffectFrame frame,
            float radius,
            float flicker,
            Color base_,
            out float size,
            out float light,
            out Color color
        )
        {
            var home = Home(index, frame.Center, radius);
            float rest = RestShare * frame.Brightness * (1f - FlickerDepth * flicker * (float)random.NextDouble());
            var flight = flights[index];
            double age = flight == null ? -1 : frame.Time - flight.Start;
            if (flight != null && age > FlightSeconds + ReturnSeconds)
            {
                flights[index] = flight = null;
                age = -1;
            }
            size = Height;
            light = rest;
            color = base_;
            if (flight == null || age < 0)
            {
                positions[index] = home;
                return;
            }
            color = flight.Color;
            float share = (float)age < FlightSeconds
                ? (float)age / FlightSeconds
                : Mathf.Max(0f, 1f - ((float)age - FlightSeconds) / ReturnSeconds);
            if (!flight.Target.HasValue)
            {
                var outward = new Vector3(
                    home.x - frame.Center.x,
                    0f,
                    home.z - frame.Center.z
                ).normalized
                    * (Scatter * share);
                positions[index] = home + outward;
                if (age < FlightSeconds)
                    light = rest * Flash;
                return;
            }
            if (age < FlightSeconds)
            {
                positions[index] = Vector3.Lerp(flight.From, flight.Target.Value, share);
                return;
            }
            float back = 1f - share;
            positions[index] = Vector3.Lerp(flight.Target.Value, home, back);
            size = Mathf.Lerp(FlashHeight, Height, back);
            light = Mathf.Lerp(frame.Brightness * Mathf.Clamp01(flight.Strength), rest, back);
        }

        private void Draw(
            int glyph,
            Vector3 center,
            float size,
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            Color color
        )
        {
            glyph %= MusicRuneGlyphs.Strokes.Length;
            float width = size * LineShare;
            float shift = MusicRuneGlyphs.Centers[glyph];
            foreach (var stroke in MusicRuneGlyphs.Strokes[glyph])
                for (int point = 0; point + 1 < stroke.Length; point++)
                {
                    var a = center + right * ((stroke[point].x - shift) * size) + up * ((stroke[point].y - .5f) * size);
                    var b = center + right * ((stroke[point + 1].x - shift) * size)
                        + up * ((stroke[point + 1].y - .5f) * size);
                    var along = (b - a).normalized;
                    var side = Vector3.Cross(along, forward).normalized * (width * HaloWidths / 2f);
                    a -= along * (width / 2f);
                    b += along * (width / 2f);
                    int first = vertices.Count;
                    vertices.Add(a - side);
                    vertices.Add(a + side);
                    vertices.Add(b + side);
                    vertices.Add(b - side);
                    uvs.Add(new Vector2(.5f, 0f));
                    uvs.Add(new Vector2(.5f, 1f));
                    uvs.Add(new Vector2(.5f, 1f));
                    uvs.Add(new Vector2(.5f, 0f));
                    for (int corner = 0; corner < 4; corner++)
                        colors.Add(color);
                    indices.Add(first);
                    indices.Add(first + 1);
                    indices.Add(first + 2);
                    indices.Add(first);
                    indices.Add(first + 2);
                    indices.Add(first + 3);
                }
        }

        private static Texture2D SoftLine()
        {
            const int size = 64;
            var texture = new Texture2D(
                4,
                size,
                TextureFormat.RGBA32,
                false
            )
            {
                name = "Boombox music soft line",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[4 * size];
            for (int y = 0; y < size; y++)
            {
                float offset = Mathf.Abs((y + .5f) / size - .5f);
                float core = 1f / (2f * HaloWidths);
                float alpha = offset <= core ? 1f : .35f * Mathf.Clamp01(1f - (offset - core) / (.5f - core));
                for (int x = 0; x < 4; x++)
                    pixels[y * 4 + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
