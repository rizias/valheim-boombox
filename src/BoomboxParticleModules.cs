using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal static class BoomboxParticleModules
    {
        private const float CoreSortingFudge = 10f;

        internal static void Shape(ParticleSystem system, BoomboxParticleLayer layer, float scale)
        {
            var shape = system.shape;
            shape.rotation = BoomboxParticleValues.Vector(layer.shapeRotation);
            shape.radius = layer.radius * scale;
            shape.radiusThickness = layer.radiusThickness;
            shape.arc = 360f;
            switch (layer.shape)
            {
                case "point":
                    shape.enabled = false;
                    break;
                case "sphere":
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    break;
                case "circle":
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    break;
                case "cone":
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = layer.coneAngle;
                    break;
                default:
                    throw new InvalidOperationException(
                        layer.name + ": unknown shape " + layer.shape + "; expected point, sphere, circle or cone"
                    );
            }
        }

        internal static void Motion(
            ParticleSystem system,
            BoomboxParticleLayer layer,
            ParticleSystemSimulationSpace space,
            float scale
        )
        {
            if (layer.velocity != null || layer.orbital != null)
            {
                var velocity = system.velocityOverLifetime;
                var linear = BoomboxParticleValues.Vector(layer.velocity) * scale;
                var orbital = BoomboxParticleValues.Vector(layer.orbital);
                velocity.enabled = true;
                velocity.space = space == ParticleSystemSimulationSpace.Local
                    ? ParticleSystemSimulationSpace.Local
                    : ParticleSystemSimulationSpace.World;
                velocity.x = linear.x;
                velocity.y = linear.y;
                velocity.z = linear.z;
                velocity.orbitalX = orbital.x;
                velocity.orbitalY = orbital.y;
                velocity.orbitalZ = orbital.z;
            }
            if (layer.noiseStrength <= 0f)
                return;
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = layer.noiseStrength * scale;
            noise.frequency = layer.noiseFrequency;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        internal static void Rotation(ParticleSystem system, BoomboxParticleLayer layer, bool coin)
        {
            var main = system.main;
            var rotation = system.rotationOverLifetime;
            rotation.enabled = layer.rotationSpeed != null;
            if (!coin)
            {
                main.startRotation = BoomboxParticleValues.Range(layer.startRotation, Mathf.Deg2Rad);
                if (rotation.enabled)
                    rotation.z = Turn(layer, 1f);
                return;
            }
            var axes = BoomboxParticleValues.Vector(layer.rotationAxes);
            var turn = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var still = new ParticleSystem.MinMaxCurve(0f, 0f);
            main.startRotation3D = true;
            main.startRotationX = axes.x != 0f ? turn : still;
            main.startRotationY = axes.y != 0f ? turn : still;
            main.startRotationZ = axes.z != 0f ? turn : still;
            if (!rotation.enabled)
                return;
            rotation.separateAxes = true;
            rotation.x = Turn(layer, axes.x);
            rotation.y = Turn(layer, axes.y);
            rotation.z = Turn(layer, axes.z);
        }

        internal static void Lifetime(ParticleSystem system, BoomboxParticleLayer layer)
        {
            if (layer.colorOverLifetime != null)
            {
                var color = system.colorOverLifetime;
                color.enabled = true;
                color.color = BoomboxParticleValues.Gradient(layer.colorOverLifetime);
            }
            if (layer.sizeOverLifetime == null)
                return;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, BoomboxParticleValues.Curve(layer.sizeOverLifetime));
        }

        internal static void Renderer(ParticleSystem system, BoomboxParticleLayer layer, bool coin, string product)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = BoomboxParticleValues.RenderMode(layer.renderMode);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (coin)
            {
                renderer.SetMeshes(BoomboxParticleProducts.Meshes(product));
                renderer.sharedMaterial = BoomboxParticleProducts.Material(product, layer);
                renderer.alignment = ParticleSystemRenderSpace.World;
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
                    ParticleSystemVertexStream.Position,
                    ParticleSystemVertexStream.Normal,
                    ParticleSystemVertexStream.Tangent,
                    ParticleSystemVertexStream.Color,
                    ParticleSystemVertexStream.UV
                    });
                return;
            }
            renderer.lengthScale = layer.lengthScale;
            renderer.velocityScale = layer.velocityScale;
            renderer.sharedMaterial = BoomboxParticleAssets.Additive(layer.texture, layer.textureGlow);
        }

        internal static void Collision(ParticleSystem system, BoomboxParticleLayer layer, bool coin, string product)
        {
            if (layer.collision == null)
                return;
            var collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = BoomboxParticleAssets.GroundMask;
            collision.enableDynamicColliders = true;
            collision.bounce = layer.collision.bounce;
            collision.dampen = layer.collision.dampen;
            collision.radiusScale = layer.collision.radiusScale * (coin ? BoomboxParticleProducts.Extent(product) : 1f);
        }

        internal static BoomboxParticleCore Core(ParticleSystem glow, BoomboxParticleLayer layer)
        {
            var holder = new GameObject(layer.name + "Core");
            holder.layer = glow.gameObject.layer;
            holder.transform.SetParent(glow.transform, false);
            var core = holder.AddComponent<ParticleSystem>();
            var source = glow.main;
            var main = core.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = source.duration;
            main.maxParticles = source.maxParticles;
            main.simulationSpace = source.simulationSpace;
            if (source.simulationSpace == ParticleSystemSimulationSpace.Custom)
                main.customSimulationSpace = source.customSimulationSpace;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startColor = Color.white;
            var emission = core.emission;
            emission.enabled = false;
            var shape = core.shape;
            shape.enabled = false;
            if (layer.colorOverLifetime != null)
            {
                var color = core.colorOverLifetime;
                color.enabled = true;
                color.color = BoomboxParticleLook.Lighten(BoomboxParticleValues.Gradient(layer.colorOverLifetime));
            }
            if (layer.sizeOverLifetime != null)
            {
                var size = core.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, BoomboxParticleValues.Curve(layer.sizeOverLifetime));
            }
            var sourceRenderer = glow.GetComponent<ParticleSystemRenderer>();
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = sourceRenderer.renderMode;
            renderer.lengthScale = sourceRenderer.lengthScale;
            renderer.velocityScale = sourceRenderer.velocityScale;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sharedMaterial = BoomboxParticleAssets.Core(layer.texture);
            renderer.sortingFudge = sourceRenderer.sortingFudge - CoreSortingFudge;
            return new BoomboxParticleCore(glow, core);
        }
        private static ParticleSystem.MinMaxCurve Turn(BoomboxParticleLayer layer, float weight)
        {
            var range = BoomboxParticleValues.Pair(layer.rotationSpeed) * weight * Mathf.Deg2Rad;
            return new ParticleSystem.MinMaxCurve(range.x, range.y);
        }
    }
}
