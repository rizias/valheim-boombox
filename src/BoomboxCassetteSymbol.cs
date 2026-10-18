using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxCassetteSymbol : MonoBehaviour
    {
        private Renderer[] renderers;
        private Color[] colors;
        private Material[] materials;
        private Vector3 velocity;
        private float strength;
        private float elapsed;
        private string property;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

        public static void Emit(VikingBoomboxVisualState model, RecordedCassette cassette, float strength)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            if (cassette == null || !settings.ShowVisualEffects.Value || !settings.ShowCassetteSymbols.Value
            || settings.CassetteSymbolIntensity.Value <= 0f)
                return;
            var active = BoomboxItemData.Properties.Where(p => cassette.Rank(p) > 0).ToArray();
            for (int i = 0; i < active.Length; i++)
            {
                var root = new GameObject("Boombox cassette property " + active[i]);
                try
                {
                    root.transform.SetPositionAndRotation(
                        model.DisplayAnchor.position + model.transform.forward * 0.04f
                            + model.transform.right * ((i - (active.Length - 1) * 0.5f) * 0.04f),
                        model.transform.rotation
                    );
                    var symbol = BoomboxItemPrefabs.CreatePropertySymbol(active[i], root.transform);
                    if (symbol == null)
                    {
                        Destroy(root);
                        continue;
                    }
                    root.transform.localScale = Vector3.one * 2f;
                    var effect = root.AddComponent<BoomboxCassetteSymbol>();
                    effect.property = active[i];
                    effect.strength = Mathf.Clamp01(strength);
                    effect.velocity = model.transform.forward * 0.24f + Vector3.up * 0.16f;
                    effect.renderers = symbol.GetComponentsInChildren<Renderer>(true);
                    effect.colors = effect.renderers.Select(r => r.sharedMaterial.HasProperty("_Color")
                        ? r.sharedMaterial.color
                        : Color.white).ToArray();
                    effect.materials = new Material[effect.renderers.Length];
                    for (int rendererIndex = 0; rendererIndex < effect.renderers.Length; rendererIndex++)
                    {
                        var material = new Material(
                            BoomboxWaveMaterials.Alpha
                        )
                        {
                            name = "Boombox cassette symbol",
                            color = effect.colors[rendererIndex]
                        };
                        effect.materials[rendererIndex] = material;
                        effect.renderers[rendererIndex].sharedMaterial = material;
                    }
                }
                catch
                {
                    Destroy(root);
                    throw;
                }
            }
        }

        private void Update()
        {
            var settings = BoomboxPlugin.Instance.Settings;
            if (!settings.ShowVisualEffects.Value || !settings.ShowCassetteSymbols.Value
            || settings.CassetteSymbolIntensity.Value <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            float delta = Time.timeScale <= 0f || AudioListener.pause ? 0f : Time.deltaTime;
            elapsed += delta;
            float remaining = Mathf.Clamp01(1f - elapsed / 0.45f);
            transform.position += velocity * delta;
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(properties);
                var configured = settings.CassetteColors[property].Value;
                Color original = configured.a > 0f ? configured : colors[i];
                Color color = original * BoomboxSettings.Nonnegative(settings.CassetteSymbolIntensity.Value);
                color.a = original.a * strength * remaining;
                properties.SetColor("_Color", color);
                renderers[i].SetPropertyBlock(properties);
            }
            if (remaining <= 0f)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (materials == null)
                return;
            foreach (var material in materials)
                if (material != null)
                    Destroy(material);
        }
    }
}
