using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal static class BoomboxEnchantSystems
    {
        internal static readonly Color Blue = new Color(.29f, .48f, 1f);
        internal static readonly Color Pale = new Color(.72f, .82f, 1f);

        internal static ParticleSystem Create(
            Transform parent,
            string name,
            ParticleSystemSimulationSpace space,
            int maximum,
            float duration,
            Material material
        )
        {
            var holder = new GameObject(name);
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var system = holder.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = duration;
            main.simulationSpace = space;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = maximum;
            main.startColor = Color.white;
            main.startSpeed = 0f;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            var shape = system.shape;
            shape.enabled = false;
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sharedMaterial = material;
            holder.SetActive(true);
            return system;
        }

        internal static void Fade(ParticleSystem system, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;
        }

        internal static void Grow(ParticleSystem system, int needed)
        {
            var main = system.main;
            if (main.maxParticles < needed)
                main.maxParticles = needed;
        }
    }
}
