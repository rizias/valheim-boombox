using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace VikingBoombox
{
    public enum BoomboxExtraParticles
    {
        None, Coins, Notes
    }

    public sealed class BoomboxParticleSet
    {
        public float referenceBodySize;
        public Dictionary<BoomboxDecorationBonus, string> effects;
        public Dictionary<BoomboxExtraParticles, string> extras;
        public List<BoomboxParticleVariant> variants;
    }

    public sealed class BoomboxParticleVariant
    {
        public string id;
        public List<BoomboxParticleLayer> layers;
    }

    public sealed class BoomboxParticleLayer
    {
        public string name;
        public string renderMode;
        public string texture;
        public float textureGlow;
        public string space;
        public int maxParticles;
        public float rateOverTime;
        public int burstCount;
        public float burstInterval;
        public float[] startLifetime;
        public float[] startSize;
        public float[] startSpeed;
        public float[] startRotation;
        public float[] rotationSpeed;
        public float[] rotationAxes;
        public float gravityModifier;
        public float[] offset;
        public string shape;
        public float radius;
        public float radiusThickness;
        public float coneAngle;
        public float[] shapeRotation;
        public float[] velocity;
        public float[] orbital;
        public float noiseStrength;
        public float noiseFrequency;
        public float lengthScale;
        public float velocityScale;
        public float[][] colorOverLifetime;
        public float[][] sizeOverLifetime;
        public BoomboxParticleCollision collision;
        public BoomboxParticleLayer impact;
        public float impactMinSpeed;
        public float[] coinTint;
        public float[] coinEmission;
        public float? coinMetallic;
        public float? coinSmoothness;
    }

    public sealed class BoomboxParticleCollision
    {
        public float bounce;
        public float dampen;
        public float radiusScale;
    }

    internal static class BoomboxParticleCatalog
    {
        private static BoomboxParticleSet set;

        internal static BoomboxParticleSet Set => set ?? (set = Load());

        internal static BoomboxParticleVariant Variant(
            string id
        ) => Set.variants.FirstOrDefault(v => v.id == id) ?? throw new InvalidDataException(BoomboxText.Get("text.missing_particle_variant") + id);

        internal static BoomboxExtraParticles Extra(BoomboxDecorationPlacement placement)
        => Enum.TryParse(placement.extraParticles, true, out BoomboxExtraParticles chosen)
            ? chosen
            : BoomboxExtraParticles.None;

        internal static string ExtraName(BoomboxExtraParticles extra)
        {
            switch (extra)
            {
                case BoomboxExtraParticles.Coins:
                    return BoomboxText.Get("text.coins");
                case BoomboxExtraParticles.Notes:
                    return BoomboxText.Get("text.notes");
                default:
                    return BoomboxText.Get("text.none");
            }
        }

        private static BoomboxParticleSet Load()
        {
            BoomboxParticleSet loaded;
            using (var stream = typeof(BoomboxParticleCatalog).Assembly.GetManifestResourceStream(
                "ValheimBoombox.PendantParticles.json"
            ))
            {
                if (stream == null)
                    throw new InvalidDataException(BoomboxText.Get("text.the_dll_has_no_pendant_particle_settings"));
                using (var reader = new StreamReader(stream))
                    loaded = JsonConvert.DeserializeObject<BoomboxParticleSet>(
                        reader.ReadToEnd(),
                        new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }
                    );
            }
            var ids = new HashSet<string>(loaded.variants.Select(v => v.id), StringComparer.Ordinal);
            bool effects = BoomboxDecorationCatalog.Effects.All(e => loaded.effects.TryGetValue(e, out var id)
                && ids.Contains(id));
            bool extras = Enum.GetValues(typeof(BoomboxExtraParticles)).Cast<BoomboxExtraParticles>().Where(
                e => e != BoomboxExtraParticles.None
            ).All(
                e => loaded.extras.TryGetValue(e, out var id) && ids.Contains(id)
            );
            if (!effects || !extras || loaded.referenceBodySize <= 0f)
                throw new InvalidDataException(
                    BoomboxText.Get("text.pendant_particle_settings_are_incomplete_variants")
                );
            return loaded;
        }
    }
}
