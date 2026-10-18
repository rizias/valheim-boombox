using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxParticleBuilder
    {
        internal static BoomboxParticleEmitter Build(
            BoomboxParticleVariant variant,
            string role,
            Transform anchor,
            BoomboxParticleTuning tuning
        )
        {
            var root = new GameObject(role + ":" + variant.id);
            root.layer = anchor.gameObject.layer;
            root.transform.SetParent(anchor, false);
            var emitter = new BoomboxParticleEmitter(root) { Scale = tuning.Scale };
            try
            {
                foreach (var layer in variant.layers)
                    Layer(emitter, root.transform, layer, tuning, false);
            }
            catch
            {
                emitter.Destroy();
                throw;
            }
            return emitter;
        }

        private static ParticleSystem Layer(
            BoomboxParticleEmitter emitter,
            Transform parent,
            BoomboxParticleLayer layer,
            BoomboxParticleTuning tuning,
            bool impact
        )
        {
            bool coin = BoomboxParticleValues.RenderMode(layer.renderMode) == ParticleSystemRenderMode.Mesh;
            var space = BoomboxParticleValues.Space(layer.space);
            if ((impact || layer.collision != null) && space != ParticleSystemSimulationSpace.World)
                throw new InvalidOperationException(layer.name + ": impact and collision layers must use world space");
            float interval = !impact && layer.burstCount > 0 && tuning.BurstInterval.HasValue
                ? tuning.BurstInterval.Value
                : layer.burstInterval;
            var lifetime = coin && tuning.CoinLifetime.HasValue
                ? new[] { tuning.CoinLifetime.Value }
                : layer.startLifetime;
            if (!impact && layer.burstCount > 0 && interval <= 0f)
                throw new InvalidOperationException(layer.name + ": burstCount needs a positive burstInterval");
            var holder = new GameObject(layer.name);
            holder.SetActive(false);
            holder.layer = parent.gameObject.layer;
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = impact
                ? Vector3.zero
                : BoomboxParticleValues.Vector(layer.offset) * tuning.Scale;
            var system = holder.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = interval > 0f ? interval : 5f;
            main.maxParticles = Maximum(layer, interval, lifetime, tuning, impact);
            main.simulationSpace = space;
            if (space == ParticleSystemSimulationSpace.Custom)
                main.customSimulationSpace = tuning.Boombox;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startColor = Color.white;
            main.startLifetime = BoomboxParticleValues.Range(lifetime, 1f);
            main.startSize = BoomboxParticleValues.Range(
                layer.startSize,
                (coin ? CoinSize(tuning.Product) : 1f) * tuning.Scale
            );
            main.startSpeed = BoomboxParticleValues.Range(layer.startSpeed, tuning.Scale);
            main.gravityModifier = layer.gravityModifier;
            float amount = Halo(layer) ? 1f : tuning.Amount;
            var emission = system.emission;
            emission.rateOverTime = layer.rateOverTime * amount;
            if (layer.burstCount > 0)
                emission.SetBursts(Bursts(layer.burstCount * amount));
            BoomboxParticleModules.Shape(system, layer, tuning.Scale);
            BoomboxParticleModules.Motion(system, layer, space, tuning.Scale);
            BoomboxParticleModules.Rotation(system, layer, coin);
            BoomboxParticleModules.Lifetime(system, layer);
            BoomboxParticleModules.Renderer(system, layer, coin, tuning.Product);
            BoomboxParticleModules.Collision(system, layer, coin, tuning.Product);
            if (!coin)
                emitter.Cores.Add(BoomboxParticleModules.Core(system, layer));
            else if (!impact && tuning.Product != null)
            {
                emitter.Product = system;
                emitter.ProductLayer = layer;
            }
            if (layer.impact != null)
            {
                if (layer.collision == null || layer.impactMinSpeed <= 0f)
                    throw new InvalidOperationException(layer.name
                        + ": impact needs collision and a positive impactMinSpeed");
                var child = Layer(emitter, holder.transform, layer.impact, tuning, true);
                var impacts = system.subEmitters;
                impacts.enabled = true;
                impacts.AddSubEmitter(
                    child,
                    ParticleSystemSubEmitterType.Manual,
                    ParticleSystemSubEmitterProperties.InheritNothing
                );
                emitter.Impacts.Add(
                    new BoomboxParticleImpact(system, impacts.subEmittersCount - 1, layer.impactMinSpeed)
                );
            }
            holder.SetActive(true);
            return system;
        }

        internal static void Retarget(BoomboxParticleEmitter emitter, string product)
        {
            if (emitter.Product == null)
                return;
            BoomboxParticleModules.Renderer(emitter.Product, emitter.ProductLayer, true, product);
            BoomboxParticleModules.Collision(emitter.Product, emitter.ProductLayer, true, product);
            var main = emitter.Product.main;
            main.startSize = BoomboxParticleValues.Range(
                emitter.ProductLayer.startSize,
                CoinSize(product) * emitter.Scale
            );
        }

        private static float CoinSize(
            string product
        ) => BoomboxDecorationCatalog.ProductSize(product) / BoomboxParticleProducts.Extent(product);

        private static bool Halo(BoomboxParticleLayer layer) => layer.texture == "glow";

        private static ParticleSystem.Burst[] Bursts(float count)
        {
            int whole = Mathf.FloorToInt(count);
            float rest = count - whole;
            var bursts = new List<ParticleSystem.Burst>();
            if (whole > 0)
                bursts.Add(new ParticleSystem.Burst(0f, (short)whole));
            if (rest > .001f)
                bursts.Add(new ParticleSystem.Burst(0f, (short)1) { probability = rest });
            return bursts.ToArray();
        }

        private static int Maximum(
            BoomboxParticleLayer layer,
            float interval,
            float[] lifetime,
            BoomboxParticleTuning tuning,
            bool impact
        )
        {
            float amount = Halo(layer) ? 1f : tuning.Amount;
            int maximum = layer.maxParticles;
            if (!impact && layer.burstCount > 0 && tuning.BurstInterval.HasValue)
                maximum = Math.Max(
                    maximum,
                    layer.burstCount * (Mathf.CeilToInt(BoomboxParticleValues.Longest(lifetime) / interval) + 1)
                );
            return Mathf.CeilToInt(maximum * amount);
        }
    }
}
