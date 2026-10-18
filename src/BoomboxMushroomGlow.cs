using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxMushroomGlow : MonoBehaviour
    {
        private sealed class Source
        {
            internal Renderer Renderer;
            internal int Index;
            internal Color Emission;
        }

        private readonly List<Source> sources = new List<Source>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private float applied = float.NaN;
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        internal void Initialize()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                var materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                    if (materials[index] && materials[index].HasProperty(Emission))
                        sources.Add(
                            new Source
                            {
                                Renderer = renderer,
                                Index = index,
                                Emission = materials[index].GetColor(Emission)
                            }
                        );
            }
            LateUpdate();
        }

        private void LateUpdate()
        {
            var settings = BoomboxPlugin.Instance?.Settings;
            if (settings == null)
                return;
            float brightness = settings.ShowVisualEffects.Value && settings.ShowDecorationGlow.Value
                ? BoomboxSettings.Nonnegative(settings.YellowMushroomGlow.Value)
                : 0f;
            if (brightness == applied)
                return;
            applied = brightness;
            foreach (var source in sources)
            {
                if (!source.Renderer)
                    continue;
                block.Clear();
                source.Renderer.GetPropertyBlock(block, source.Index);
                var color = source.Emission * brightness;
                color.a = source.Emission.a;
                block.SetColor(Emission, color);
                source.Renderer.SetPropertyBlock(block, source.Index);
            }
        }
    }
}
