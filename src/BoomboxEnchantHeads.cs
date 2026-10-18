using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxEnchantHeads
    {
        private readonly ParticleSystem halos;
        private readonly ParticleSystem sparks;
        private readonly ParticleSystem sparkCores;
        private readonly ParticleSystem notes;
        private readonly ParticleSystem noteCores;
        private readonly Color noteCore = Color.Lerp(
            BoomboxEnchantSystems.Blue,
            Color.white,
            BoomboxParticleLook.CoreLighten
        );
        private ParticleSystem.Particle[] haloBuffer = new ParticleSystem.Particle[0];
        private ParticleSystem.Particle[] sparkBuffer = new ParticleSystem.Particle[0];
        private ParticleSystem.Particle[] sparkCoreBuffer = new ParticleSystem.Particle[0];
        private ParticleSystem.Particle[] noteBuffer = new ParticleSystem.Particle[0];
        private ParticleSystem.Particle[] noteCoreBuffer = new ParticleSystem.Particle[0];
        private int haloCount;
        private int sparkCount;
        private int noteCount;

        internal BoomboxEnchantHeads(Transform root, string suffix)
        {
            halos = BoomboxEnchantSystems.Create(
                root,
                "EnchantStreamGlow" + suffix,
                ParticleSystemSimulationSpace.World,
                1,
                1f,
                BoomboxParticleAssets.Alpha("glow", 1f)
            );
            sparks = BoomboxEnchantSystems.Create(
                root,
                "EnchantStreamSparks" + suffix,
                ParticleSystemSimulationSpace.World,
                1,
                1f,
                BoomboxParticleAssets.Additive("sparkle", .35f)
            );
            sparkCores = BoomboxEnchantSystems.Create(
                root,
                "EnchantStreamSparks" + suffix + "Core",
                ParticleSystemSimulationSpace.World,
                1,
                1f,
                BoomboxParticleAssets.Core("sparkle")
            );
            notes = BoomboxEnchantSystems.Create(
                root,
                "EnchantStreamNotes" + suffix,
                ParticleSystemSimulationSpace.World,
                1,
                1f,
                BoomboxParticleAssets.Additive("note", .3f)
            );
            noteCores = BoomboxEnchantSystems.Create(
                root,
                "EnchantStreamNotes" + suffix + "Core",
                ParticleSystemSimulationSpace.World,
                1,
                1f,
                BoomboxParticleAssets.Core("note")
            );
        }

        internal void Reserve(int count)
        {
            if (haloBuffer.Length >= count)
                return;
            haloBuffer = new ParticleSystem.Particle[count];
            sparkBuffer = new ParticleSystem.Particle[count];
            sparkCoreBuffer = new ParticleSystem.Particle[count];
            noteBuffer = new ParticleSystem.Particle[count];
            noteCoreBuffer = new ParticleSystem.Particle[count];
            foreach (var system in new[] { halos, sparks, sparkCores, notes, noteCores })
                BoomboxEnchantSystems.Grow(system, count);
        }

        internal void Begin() => haloCount = sparkCount = noteCount = 0;

        internal void Add(
            Vector3 position,
            float haloSize,
            float haloAlpha,
            float size,
            bool note,
            float lifetime,
            float rotation
        )
        {
            var blue = BoomboxEnchantSystems.Blue;
            haloBuffer[haloCount++] = Particle(
                position,
                haloSize,
                new Color(blue.r, blue.g, blue.b, haloAlpha),
                lifetime,
                0f
            );
            if (note)
            {
                noteBuffer[noteCount] = Particle(position, size, blue, lifetime, rotation);
                noteCoreBuffer[noteCount++] = Particle(position, size, noteCore, lifetime, rotation);
                return;
            }
            sparkBuffer[sparkCount] = Particle(position, size, Color.white, lifetime, rotation);
            sparkCoreBuffer[sparkCount++] = Particle(position, size, Color.white, lifetime, rotation);
        }

        internal void End()
        {
            halos.SetParticles(haloBuffer, haloCount);
            sparks.SetParticles(sparkBuffer, sparkCount);
            sparkCores.SetParticles(sparkCoreBuffer, sparkCount);
            notes.SetParticles(noteBuffer, noteCount);
            noteCores.SetParticles(noteCoreBuffer, noteCount);
        }

        internal void Clear()
        {
            Begin();
            End();
        }

        private static ParticleSystem.Particle Particle(
            Vector3 position,
            float size,
            Color color,
            float lifetime,
            float rotation
        )
        => new ParticleSystem.Particle
        {
            position = position,
            startSize = size,
            startColor = color,
            startLifetime = lifetime,
            remainingLifetime = lifetime,
            rotation = rotation
        };
    }
}
