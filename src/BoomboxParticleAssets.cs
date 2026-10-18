using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxParticleAssets
    {
        private const string AdditiveShader = "Legacy Shaders/Particles/Additive";
        private const string AlphaShader = "Legacy Shaders/Particles/Alpha Blended";
        private static readonly Dictionary<string, Material> templates = new Dictionary<string, Material>(
            StringComparer.Ordinal
        );
        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(
            StringComparer.Ordinal
        );
        private static readonly Dictionary<BoomboxParticleLayer, Material> coins =
            new Dictionary<BoomboxParticleLayer, Material>();
        private static float appliedBrightness = 1f;

        internal static int GroundMask => LayerMask.GetMask(
            "Default",
            "static_solid",
            "Default_small",
            "piece",
            "terrain",
            "vehicle"
        );

        internal static Material Additive(string texture, float glow) => Particles(AdditiveShader, texture, glow);

        internal static Material Alpha(string texture, float glow) => Particles(AlphaShader, texture, glow);

        internal static Material Core(string texture) => Particles(AlphaShader, texture, 0f);

        internal static void Refresh()
        {
            float brightness = BoomboxParticleLook.Brightness;
            if (brightness == appliedBrightness)
                return;
            appliedBrightness = brightness;
            foreach (var pair in materials)
                if (pair.Value != null)
                    Tint(pair.Value, pair.Key.StartsWith(AlphaShader, StringComparison.Ordinal), brightness);
        }

        private static Material Particles(string shader, string texture, float glow)
        {
            string key = shader + ":" + texture + ":" + glow.ToString("R", CultureInfo.InvariantCulture);
            if (materials.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var material = new Material(Template(shader)) { name = "Boombox particles " + key };
            material.mainTexture = BoomboxParticleTextures.Texture(texture, glow);
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
            material.SetFloat("_InvFade", 100f);
            Tint(material, shader == AlphaShader, BoomboxParticleLook.Brightness);
            materials[key] = material;
            return material;
        }

        internal static Material Soft(Texture texture)
        {
            var material = new Material(
                Template(AlphaShader)
            )
            {
                name = "Boombox soft particles",
                mainTexture = texture
            };
            material.SetFloat("_InvFade", 100f);
            material.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f));
            return material;
        }

        internal static Material GhostCore(Texture texture)
        {
            var material = new Material(
                Template(AlphaShader)
            )
            {
                name = "Boombox ghost core",
                mainTexture = texture != null
                    ? texture
                    : Texture2D.whiteTexture
            };
            material.SetFloat("_InvFade", 100f);
            return material;
        }

        internal static Material Glow(Texture texture)
        {
            var material = new Material(
                Template(AdditiveShader)
            )
            {
                name = "Boombox music glow",
                mainTexture = texture
            };
            material.SetFloat("_InvFade", 100f);
            return material;
        }

        internal static Material Ghost()
        {
            var material = new Material(
                Template(AdditiveShader)
            )
            {
                name = "Boombox ghost",
                mainTexture = Texture2D.whiteTexture
            };
            material.SetFloat("_InvFade", 100f);
            return material;
        }

        private static Material Template(string shader)
        {
            if (!templates.TryGetValue(shader, out var template) || template == null)
                templates[shader] = template = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(
                    m => m != null && m.shader != null && m.shader.isSupported && m.shader.name == shader
                );
            if (template == null)
                throw new InvalidOperationException(BoomboxText.Get("text.game_material_with_shader") + shader + BoomboxText.Get("text.was_not_found"));
            return template;
        }

        private static void Tint(Material material, bool alpha, float brightness)
        => material.SetColor(
            "_TintColor",
            new Color(
                .5f * brightness,
                .5f * brightness,
                .5f * brightness,
                alpha
                    ? .5f * Mathf.Min(1f, brightness)
                    : .5f
            )
        );

        internal static Material Coin(BoomboxParticleLayer layer)
        {
            if (coins.TryGetValue(layer, out var cached) && cached != null)
                return cached;
            var material = new Material(BoomboxParticleCoins.Template) { name = "Boombox coin " + layer.name };
            if (layer.coinTint != null)
                material.SetColor(Property(material, layer, "_Color"), Opaque(layer.coinTint));
            if (layer.coinMetallic.HasValue)
            {
                material.DisableKeyword("_METALLICGLOSSMAP");
                material.SetFloat(Property(material, layer, "_Metallic"), layer.coinMetallic.Value);
            }
            if (layer.coinSmoothness.HasValue)
            {
                material.SetFloat(Property(material, layer, "_Glossiness"), layer.coinSmoothness.Value);
                material.SetFloat(Property(material, layer, "_GlossMapScale"), layer.coinSmoothness.Value);
            }
            if (layer.coinEmission != null)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.SetColor(Property(material, layer, "_EmissionColor"), Opaque(layer.coinEmission));
            }
            coins[layer] = material;
            return material;
        }

        internal static void Clear()
        {
            foreach (var material in materials.Values.Concat(coins.Values))
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            materials.Clear();
            coins.Clear();
            templates.Clear();
            appliedBrightness = 1f;
            BoomboxParticleTextures.Clear();
            BoomboxParticleCoins.Clear();
            BoomboxParticleProducts.Clear();
        }

        private static string Property(Material material, BoomboxParticleLayer layer, string property)
        {
            if (!material.HasProperty(property))
                throw new InvalidOperationException(
                    layer.name + ": coin shader " + material.shader.name + " has no " + property
                );
            return property;
        }

        private static Color Opaque(float[] values)
        {
            var rgb = BoomboxParticleValues.Vector(values);
            return new Color(rgb.x, rgb.y, rgb.z, 1f);
        }
    }
}
