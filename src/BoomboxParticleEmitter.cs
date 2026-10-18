using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxParticleTuning
    {
        internal float Scale = 1f;
        internal Transform Boombox;
        internal float? BurstInterval;
        internal float? CoinLifetime;
        internal float Amount = 1f;
        internal string Product;
    }

    internal sealed class BoomboxParticleEmitter
    {
        internal readonly GameObject Root;
        internal readonly List<BoomboxParticleImpact> Impacts = new List<BoomboxParticleImpact>();
        internal readonly List<BoomboxParticleCore> Cores = new List<BoomboxParticleCore>();
        internal ParticleSystem Product;
        internal BoomboxParticleLayer ProductLayer;
        internal float Scale = 1f;

        internal BoomboxParticleEmitter(GameObject root)
        {
            Root = root;
        }

        internal void Update()
        {
            foreach (var impact in Impacts)
                impact.Detect();
            foreach (var core in Cores)
                core.Sync();
        }

        internal void Destroy()
        {
            if (Root == null)
                return;
            Root.SetActive(false);
            Object.Destroy(Root);
        }
    }

    internal sealed class BoomboxParticleCore
    {
        private readonly ParticleSystem glow;
        private readonly ParticleSystem core;
        private ParticleSystem.Particle[] particles = new ParticleSystem.Particle[0];

        internal BoomboxParticleCore(ParticleSystem glow, ParticleSystem core)
        {
            this.glow = glow;
            this.core = core;
        }

        internal void Sync()
        {
            if (glow == null || core == null)
                return;
            int maximum = glow.main.maxParticles;
            if (particles.Length < maximum)
                particles = new ParticleSystem.Particle[maximum];
            BoomboxEnchantSystems.Grow(core, maximum);
            core.SetParticles(particles, glow.GetParticles(particles));
        }
    }

    internal sealed class BoomboxParticleImpact
    {
        private readonly ParticleSystem source;
        private readonly int subEmitter;
        private readonly float minimumSpeed;
        private ParticleSystem.Particle[] particles = new ParticleSystem.Particle[0];
        private Dictionary<uint, float> previous = new Dictionary<uint, float>();
        private Dictionary<uint, float> current = new Dictionary<uint, float>();

        internal BoomboxParticleImpact(ParticleSystem source, int subEmitter, float minimumSpeed)
        {
            this.source = source;
            this.subEmitter = subEmitter;
            this.minimumSpeed = minimumSpeed;
        }

        internal void Detect()
        {
            if (source == null)
                return;
            if (particles.Length < source.main.maxParticles)
                particles = new ParticleSystem.Particle[source.main.maxParticles];
            int count = source.GetParticles(particles);
            current.Clear();
            for (int i = 0; i < count; i++)
            {
                uint seed = particles[i].randomSeed;
                float vertical = particles[i].velocity.y;
                if (previous.TryGetValue(seed, out float before)
                    && before <= -minimumSpeed
                    && vertical - before >= minimumSpeed)
                    source.TriggerSubEmitter(subEmitter, ref particles[i]);
                current[seed] = vertical;
            }
            var swap = previous;
            previous = current;
            current = swap;
        }
    }
}
