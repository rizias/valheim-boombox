using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class MusicFixedCracksView : IMusicStrikeView
    {
        private const float Travel = .15f;
        private const int EmberCapacity = 1024;
        private const float EmberSize = .03f;
        private const float FlickerDepth = .8f;
        private readonly MusicGroundLight ground;
        private readonly List<MusicCrack> cracks = new List<MusicCrack>();
        private readonly List<MusicCrack> waiting = new List<MusicCrack>();
        private readonly System.Random random = new System.Random(97);
        private readonly ParticleSystem embers;
        private readonly Material emberMaterial;
        private readonly Texture2D emberTexture;
        private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[EmberCapacity];

        public MusicFixedCracksView(Transform parent, MusicViewScene scene = null)
        {
            ground = new MusicGroundLight(19f, 2048, 96, scene);
            emberTexture = MusicEffectColors.SoftDot();
            emberMaterial = BoomboxParticleAssets.Glow(emberTexture);
            embers = BoomboxEnchantSystems.Create(
                parent,
                "Boombox music crack embers",
                ParticleSystemSimulationSpace.World,
                EmberCapacity,
                1f,
                emberMaterial
            );
            scene?.Own(embers.gameObject);
        }

        public MusicReach Reach
        {
            get;
        } = MusicReach.AnchoredFlight(Travel, Travel);

        public void Update(MusicEffectFrame frame)
        {
            bool silent = frame.Moment.Silent;
            foreach (var crack in cracks)
                crack.Shine = silent
                    ? Mathf.Max(0f, crack.Shine - frame.Delta / MusicCracks.SilenceFadeSeconds)
                    : Mathf.Min(1f, crack.Shine + frame.Delta / MusicCracks.SilenceFadeSeconds);
            cracks.RemoveAll(crack => crack.Shine <= 0f);
            foreach (var strike in frame.Fresh)
                waiting.AddRange(Split(strike, frame));
            if (!silent)
            {
                cracks.AddRange(waiting);
                waiting.Clear();
            }
            Cap(frame.Time);
            float bass = (float)MusicAnalysisParams.LevelOf(frame.Moment.Smooth.Low);
            ground.Begin(frame.Center);
            foreach (var crack in cracks)
            {
                var look = MusicCracks.Look(crack, frame.Time, frame.Brightness, bass);
                foreach (var line in crack.Lines)
                    ground.Crack(line, look, frame.Color);
            }
            ground.End(frame.Center, cracks.Count > 0);
            DrawEmbers(frame);
        }

        public void Hide()
        {
            cracks.Clear();
            waiting.Clear();
            ground.Hide();
            embers.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            ground.Destroy();
            if (embers != null)
                Object.Destroy(embers.gameObject);
            Object.Destroy(emberMaterial);
            Object.Destroy(emberTexture);
        }

        private IEnumerable<MusicCrack> Split(MusicLiveStrike strike, MusicEffectFrame frame)
        {
            var aims = new List<(Vector2 Aim, bool Hit)>();
            foreach (var target in strike.Targets ?? frame.Targets)
            {
                if (target.Edge > frame.Radius || target.Edge < MusicCracks.MinAim)
                    continue;
                var offset = target.Character.transform.position - strike.Origin;
                var flat = new Vector2(offset.x, offset.z);
                if (flat.sqrMagnitude <= 0f)
                    continue;
                aims.Add((flat.normalized * target.Edge, true));
            }
            var taken = aims.Select(aim => Mathf.Atan2(aim.Aim.x, aim.Aim.y)).ToList();
            foreach (float angle in MusicCracks.FreeAngles(taken, MusicCracks.RandomCount, random))
                aims.Add((new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * frame.Radius, false));
            foreach (var aim in aims)
            {
                var crack = MusicCracks.Build(aim.Aim, aim.Hit, strike.Strength, frame.Amount, strike.Time, random);
                MusicCracks.Shift(crack, strike.Origin);
                MusicCracks.CutOffGround(
                    crack,
                    (x, z) => ground.GroundAt(new Vector3(x, 0f, z)) > MusicGroundLight.NoGround,
                    aim.Hit ? MusicCracks.HitCount(frame.Amount) : 0
                );
                if (crack.Lines.Count > 0 || crack.Embers.Count > 0)
                    yield return crack;
            }
        }

        private void Cap(double time)
        {
            cracks.RemoveAll(
                crack => time - crack.Born > MusicCracks.HealSeconds || MusicCracks.CapFade(crack, time) <= 0f
            );
            var active = cracks.Where(crack => double.IsNaN(crack.FadeFrom)).ToList();
            for (int index = 0; index < active.Count - MusicCracks.Max; index++)
                active[index].FadeFrom = time;
            if (cracks.Count > MusicCracks.Slots)
                cracks.RemoveRange(0, cracks.Count - MusicCracks.Slots);
        }

        private void DrawEmbers(MusicEffectFrame frame)
        {
            float flicker = (float)MusicAnalysisParams.LevelOf(frame.Moment.Smooth.High);
            float scale = Mathf.Max(1e-4f, frame.Brightness);
            emberMaterial.SetColor("_TintColor", new Color(.5f * scale, .5f * scale, .5f * scale, .5f));
            int shown = 0;
            foreach (var crack in cracks)
            {
                float fade = crack.Shine * MusicCracks.CapFade(crack, frame.Time);
                foreach (var spot in crack.Embers)
                {
                    if (shown >= EmberCapacity)
                        break;
                    double since = frame.Time - crack.Born - MusicCracks.Arrival(spot.Radial, crack.Reach);
                    float height = MusicCracks.EmberHeight(since, spot.Rise);
                    if (height < 0f)
                        continue;
                    float light = frame.Brightness * crack.Strength * (1f - (float)since / MusicCracks.EmberLife)
                        * (1f - FlickerDepth * flicker * (float)random.NextDouble())
                        * fade;
                    float groundY = ground.GroundAt(new Vector3(spot.X, 0f, spot.Z));
                    if (groundY <= MusicGroundLight.NoGround)
                        groundY = frame.Center.y;
                    var color = MusicEffectColors.Vertex(frame.Color * (light / scale));
                    particles[shown++] = new ParticleSystem.Particle
                    {
                        position = new Vector3(spot.X, groundY + height, spot.Z),
                        startSize = EmberSize,
                        startColor = new Color(
                            Mathf.Clamp01(color.r),
                            Mathf.Clamp01(color.g),
                            Mathf.Clamp01(color.b),
                            1f
                        ),
                        remainingLifetime = 1f,
                        startLifetime = 1f
                    };
                }
            }
            embers.gameObject.SetActive(shown > 0);
            if (shown > 0)
                embers.SetParticles(particles, shown);
        }
    }
}
