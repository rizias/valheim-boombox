using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class MusicSparksView : IMusicStrikeView
    {
        private const int Count = 120;
        private const float Size = .051f;
        private const float FlickerDepth = .8f;
        private const float Flash = 2f;
        private const float Low = .5f;
        private const float High = 1.5f;
        private const float OrbitMin = 1f;
        private const float OrbitMax = 1.5f;
        private const float SlowTurn = 4f;
        private const float FastTurn = 1f;
        private const float PerStrength = 3f;
        private const float FlightSeconds = .15f;
        private const float ReturnSeconds = .5f;
        private const float Scatter = .5f;
        private const float Tilt = .35f;

        private sealed class Flight
        {
            public double Start;
            public Vector3? Target;
            public Color Color;
            public bool Pending;
        }

        private readonly ParticleSystem system;
        private readonly Material material;
        private readonly Texture2D texture;
        private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[Count];
        private readonly float[] angles = new float[Count];
        private readonly float[] tilts = new float[Count];
        private readonly Flight[] flights = new Flight[Count];
        private readonly System.Random random = new System.Random(23);
        private readonly MusicSparkPalette palette = new MusicSparkPalette();
        private readonly HashSet<int> busy = new HashSet<int>();
        private readonly List<KeyValuePair<float, int>> nearest = new List<KeyValuePair<float, int>>();

        public MusicSparksView(Transform parent, MusicViewScene scene = null)
        {
            if (!VikingBoomboxVisual.CanRender)
                return;
            for (int index = 0; index < Count; index++)
            {
                angles[index] = 2 * Mathf.PI * index / Count;
                tilts[index] = (float)random.NextDouble() * 2 * Mathf.PI;
            }
            texture = MusicEffectColors.SoftDot();
            material = BoomboxParticleAssets.Glow(texture);
            system = BoomboxEnchantSystems.Create(
                parent,
                "Boombox music sparks",
                ParticleSystemSimulationSpace.World,
                Count,
                1f,
                material
            );
            scene?.Own(system.gameObject);
            system.gameObject.SetActive(false);
        }

        public MusicReach Reach
        {
            get;
        } = MusicReach.Flight(FlightSeconds, FlightSeconds + ReturnSeconds);

        public void Update(MusicEffectFrame frame)
        {
            if (system == null)
                return;
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
            int shown = Mathf.Clamp(Mathf.RoundToInt(60 * frame.Amount), 1, Count);
            foreach (var strike in frame.Fresh)
                Launch(strike, frame, base_, shown, silent);
            float scale = Mathf.Max(1e-4f, Flash * frame.Brightness);
            material.SetColor("_TintColor", new Color(.5f * scale, .5f * scale, .5f * scale, .5f));
            int written = 0;
            for (int index = 0; index < Count; index++)
            {
                var particle = Place(index, frame, radius, flicker, base_, scale);
                if (Visible(index, shown))
                    particles[written++] = particle;
            }
            system.gameObject.SetActive(!silent);
            if (!silent)
                system.SetParticles(particles, written);
        }

        private static bool Visible(int index, int shown) => MusicOrbit.Visible(index, shown, Count);

        public void Hide()
        {
            if (system != null)
                system.gameObject.SetActive(false);
            Array.Clear(flights, 0, flights.Length);
            palette.Reset();
        }

        public void Destroy()
        {
            if (system != null)
                UnityEngine.Object.Destroy(system.gameObject);
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(texture);
        }

        private void Launch(MusicLiveStrike strike, MusicEffectFrame frame, Color color, int shown, bool silent)
        {
            if (frame.Targets.Count == 0)
            {
                for (int index = 0; index < Count; index++)
                    if (flights[index] == null && Visible(index, shown))
                        flights[index] = new Flight { Start = strike.Time, Color = color };
                return;
            }
            busy.Clear();
            for (int index = 0; index < Count; index++)
                if (flights[index] != null || !Visible(index, shown))
                    busy.Add(index);
            int count = Math.Max(1, MusicAnalysisParams.Round(PerStrength * strike.Strength));
            foreach (var target in frame.Targets)
            {
                var offset = target.Character.transform.position - frame.Center;
                float toward = Mathf.Atan2(offset.x, offset.z);
                nearest.Clear();
                for (int index = 0; index < Count; index++)
                    if (!busy.Contains(index))
                        nearest.Add(new KeyValuePair<float, int>(MusicOrbit.Gap(angles[index], toward), index));
                nearest.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));
                for (int pick = 0; pick < count && pick < nearest.Count; pick++)
                {
                    int index = nearest[pick].Value;
                    busy.Add(index);
                    flights[index] = new Flight
                    {
                        Start = strike.Time,
                        Target = target.Center,
                        Color = color,
                        Pending = silent
                    };
                }
            }
        }

        private ParticleSystem.Particle Place(
            int index,
            MusicEffectFrame frame,
            float radius,
            float flicker,
            Color base_,
            float scale
        )
        {
            float angle = angles[index];
            float lift = Mathf.Sin(angle * 2 + tilts[index]) * Tilt * radius;
            float height = Mathf.Clamp((Low + High) / 2 + lift, Low, High);
            var home = frame.Center + new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
            var flight = flights[index];
            double age = flight == null ? -1 : frame.Time - flight.Start;
            if (flight != null && age > FlightSeconds + ReturnSeconds)
            {
                flights[index] = flight = null;
                age = -1;
            }
            if (flight != null && age < 0)
                flight = null;
            var position = flight == null ? home : Flown(home, frame.Center, flight, (float)age);
            float flash = flight != null && flight.Target == null && age < FlightSeconds ? Flash : 1f;
            float light = flash * (1f - FlickerDepth * flicker * (float)random.NextDouble()) * frame.Brightness;
            var color = MusicEffectColors.Vertex((flight?.Color ?? base_) * (light / scale));
            return new ParticleSystem.Particle
            {
                position = position,
                startSize = Size,
                startColor = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f),
                remainingLifetime = 1f,
                startLifetime = 1f,
            };
        }

        private static Vector3 Flown(Vector3 home, Vector3 center, Flight flight, float age)
        {
            float share = age < FlightSeconds
                ? age / FlightSeconds
                : Mathf.Max(0f, 1f - (age - FlightSeconds) / ReturnSeconds);
            if (flight.Target.HasValue)
                return Vector3.Lerp(home, flight.Target.Value, share);
            var outward = new Vector3(home.x - center.x, 0f, home.z - center.z).normalized * (Scatter * share);
            return home + outward;
        }
    }
}
