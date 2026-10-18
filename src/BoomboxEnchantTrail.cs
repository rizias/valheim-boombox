using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxEnchantTrail
    {
        private const float Lifetime = .18f;
        private readonly ParticleSystem glow;
        private readonly ParticleSystem core;
        private readonly BoomboxParticleCore mirror;

        internal BoomboxEnchantTrail(Transform root, string name)
        {
            var colors = new[] {
                new GradientColorKey(BoomboxEnchantSystems.Pale, 0f),
                new GradientColorKey(BoomboxEnchantSystems.Blue, .4f),
                new GradientColorKey(BoomboxEnchantSystems.Blue, 1f)
            };
            var alphas = new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(.9f, .4f),
                new GradientAlphaKey(0f, 1f)
            };
            glow = BoomboxEnchantSystems.Create(
                root,
                name,
                ParticleSystemSimulationSpace.World,
                1,
                1f,
                BoomboxParticleAssets.Additive("dot", 1f)
            );
            core = BoomboxEnchantSystems.Create(
                root,
                name + "Core",
                ParticleSystemSimulationSpace.World,
                1,
                1f,
                BoomboxParticleAssets.Core("dot")
            );
            foreach (var system in new[] { glow, core })
            {
                var main = system.main;
                main.startLifetime = Lifetime;
            }
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            BoomboxEnchantSystems.Fade(glow, colors, alphas);
            var coreColor = core.colorOverLifetime;
            coreColor.enabled = true;
            coreColor.color = BoomboxParticleLook.Lighten(gradient);
            var renderer = core.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = glow.GetComponent<ParticleSystemRenderer>().sortingFudge - 10f;
            mirror = new BoomboxParticleCore(glow, core);
        }

        internal void Dot(Vector3 position, Vector3 velocity, float size)
        {
            if (glow.particleCount + 1 > glow.main.maxParticles)
                BoomboxEnchantSystems.Grow(glow, glow.main.maxParticles * 2);
            glow.Emit(
                new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = velocity,
                    startSize = size,
                    applyShapeToPosition = false
                },
                1
            );
        }

        internal void Sync() => mirror.Sync();

        internal void Clear()
        {
            glow.Clear();
            core.Clear();
        }
    }
}
