using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class BoomboxTrophyVisuals
    {
        private sealed class Speaker
        {
            internal Transform Membrane;
            internal Vector3 Position;
            internal Vector3 Scale;
            internal Transform Frame;
        }

        private readonly List<Speaker> speakers = new List<Speaker>();
        private readonly List<Renderer> carvings = new List<Renderer>();
        private MaterialPropertyBlock carvingGlow;

        private void Speakers(Transform item)
        {
            var model = item.GetChild(0);
            foreach (var part in item.GetComponentsInChildren<Transform>(true))
                if (part.name == "MembraneLeft" || part.name == "MembraneRight")
                    speakers.Add(
                        new Speaker
                        {
                            Membrane = part,
                            Position = part.localPosition,
                            Scale = part.localScale,
                            Frame = model
                        }
                    );
            model.gameObject.AddComponent<BoomboxSpeakerSuspension>().Initialize(model);
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                if (renderer.sharedMaterial != null
                    && renderer.sharedMaterial.name == VikingBoomboxVisual.CarvingMaterial)
                    carvings.Add(renderer);
            ApplyGlow(0f);
        }

        internal void ApplyMembranes(float strength)
        {
            if (speakers.Count == 0)
                return;
            var settings = BoomboxPlugin.Instance.Settings;
            float motion = settings.ShowVisualEffects.Value && settings.ShowMembraneMotion.Value
                ? strength * BoomboxSettings.Nonnegative(settings.MembraneMotion.Value)
                : 0f;
            foreach (var speaker in speakers)
            {
                if (!speaker.Membrane)
                    continue;
                var displacement = speaker.Frame.TransformVector(Vector3.forward * (motion * .075f));
                speaker.Membrane.localPosition = speaker.Position
                    + speaker.Membrane.parent.InverseTransformVector(displacement);
                speaker.Membrane.localScale = speaker.Scale * (1f + motion * .08f);
            }
        }

        internal void ApplyGlow(float strength)
        {
            if (carvings.Count == 0)
                return;
            if (carvingGlow == null)
                carvingGlow = new MaterialPropertyBlock();
            var color = VikingBoomboxVisual.CarvingGlow(VikingBoomboxVisual.CarvingBaseGlow, strength);
            foreach (var renderer in carvings)
            {
                if (!renderer)
                    continue;
                renderer.GetPropertyBlock(carvingGlow);
                carvingGlow.SetColor("_EmissionColor", color);
                renderer.SetPropertyBlock(carvingGlow);
            }
        }
    }
}
