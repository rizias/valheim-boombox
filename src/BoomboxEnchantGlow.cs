using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxEnchantGlow
    {
        private const float HaloInterval = .7f;
        private const float HaloLifetime = 1.4f;
        private const float HaloSize = .8f;
        private const float HaloAlpha = .5f;
        private const float SparkleRate = 60f;
        private const float SparkleLifetimeMin = .4f;
        private const float SparkleLifetimeMax = .8f;
        private const float SparkleSizeMin = .035f;
        private const float SparkleSizeMax = .06f;
        private const float SparkleSpeedMin = .02f;
        private const float SparkleSpeedMax = .05f;
        private readonly Transform anchor;
        private readonly ParticleSystem halo;
        private readonly ParticleSystem sparkles;
        private readonly ParticleSystem cores;
        private readonly BoomboxParticleCore mirror;

        internal BoomboxEnchantGlow(Transform root, string suffix)
        {
            anchor = new GameObject("EnchantGlow" + suffix).transform;
            anchor.SetParent(root, false);
            halo = BoomboxEnchantSystems.Create(
                anchor,
                "EnchantHalo" + suffix,
                ParticleSystemSimulationSpace.Local,
                2,
                HaloInterval,
                BoomboxParticleAssets.Alpha("glow", 1f)
            );
            var haloMain = halo.main;
            haloMain.startLifetime = HaloLifetime;
            var haloEmission = halo.emission;
            haloEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            BoomboxEnchantSystems.Fade(
                halo,
                new[] {
                new GradientColorKey(BoomboxEnchantSystems.Blue, 0f),
                new GradientColorKey(BoomboxEnchantSystems.Blue, 1f)
                },
                new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, .35f),
                new GradientAlphaKey(1f, .65f),
                new GradientAlphaKey(0f, 1f)
                }
            );
            sparkles = BoomboxEnchantSystems.Create(
                anchor,
                "EnchantSparkles" + suffix,
                ParticleSystemSimulationSpace.Local,
                1,
                1f,
                BoomboxParticleAssets.Additive("sparkle", .35f)
            );
            cores = BoomboxEnchantSystems.Create(
                anchor,
                "EnchantSparkles" + suffix + "Core",
                ParticleSystemSimulationSpace.Local,
                1,
                1f,
                BoomboxParticleAssets.Core("sparkle")
            );
            var sparkleMain = sparkles.main;
            sparkleMain.startLifetime = new ParticleSystem.MinMaxCurve(SparkleLifetimeMin, SparkleLifetimeMax);
            sparkleMain.startSize = new ParticleSystem.MinMaxCurve(SparkleSizeMin, SparkleSizeMax);
            sparkleMain.startSpeed = new ParticleSystem.MinMaxCurve(SparkleSpeedMin, SparkleSpeedMax);
            sparkleMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = sparkles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            var colors = new[] {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, .1f),
                new GradientColorKey(BoomboxEnchantSystems.Blue, .5f),
                new GradientColorKey(BoomboxEnchantSystems.Blue, 1f)
            };
            var alphas = new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, .1f),
                new GradientAlphaKey(1f, .5f),
                new GradientAlphaKey(0f, 1f)
            };
            BoomboxEnchantSystems.Fade(sparkles, colors, alphas);
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var coreColor = cores.colorOverLifetime;
            coreColor.enabled = true;
            coreColor.color = BoomboxParticleLook.Lighten(gradient);
            cores.GetComponent<ParticleSystemRenderer>()
                .sortingFudge = sparkles.GetComponent<ParticleSystemRenderer>().sortingFudge - 10f;
            mirror = new BoomboxParticleCore(sparkles, cores);
            Present(new Bounds(), 0f);
        }

        internal void Present(Bounds item, float charge)
        {
            anchor.position = item.center;
            var haloMain = halo.main;
            haloMain.startSize = HaloSize * charge;
            haloMain.startColor = new Color(1f, 1f, 1f, HaloAlpha * charge);
            var haloEmission = halo.emission;
            haloEmission.enabled = charge > 0f;
            var shape = sparkles.shape;
            shape.scale = item.size;
            float rate = SparkleRate * BoomboxParticleLook.Amount;
            BoomboxEnchantSystems.Grow(sparkles, Mathf.CeilToInt(rate * SparkleLifetimeMax));
            var sparkleEmission = sparkles.emission;
            sparkleEmission.rateOverTime = rate * charge;
            mirror.Sync();
        }

        internal void Clear()
        {
            Present(new Bounds(anchor.position, Vector3.zero), 0f);
            halo.Clear();
            sparkles.Clear();
            cores.Clear();
        }
    }
}
