using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxPendantParticleButtons
    {
        private const float Top = 658f;
        private const float Width = 200f;
        private const float Height = 32f;
        private const float FontSize = 14f;
        private readonly Button effect;
        private readonly Button extra;


        internal BoomboxPendantParticleButtons(
            BoomboxPlugin plugin,
            RectTransform window,
            Func<int> selectedSlot,
            Action changed
        )
        {
            effect = Create(
            "PendantEffectParticles",
            window,
            24f,
            () =>
                {
                    BoomboxTrophyStorage.ToggleEffectParticles(plugin.Current, selectedSlot());
                    changed();
                }
            );
            extra = Create(
            "PendantExtraParticles",
            window,
            224f,
            () =>
                {
                    BoomboxTrophyStorage.NextExtraParticles(plugin.Current, selectedSlot());
                    changed();
                }
            );
        }

        internal void Refresh(BoomboxDecorationPlacement placement)
        {
            bool shown = placement != null;
            effect.gameObject.SetActive(shown);
            extra.gameObject.SetActive(shown);
            if (!shown)
                return;
            BoomboxNativeUI.Label(
                effect,
                BoomboxText.Get("text.particles") + (placement.effectParticles ? BoomboxText.Get("text.on") : BoomboxText.Get("text.off"))
            );
            BoomboxNativeUI.Label(
                extra,
                BoomboxText.Get("text.extra_particles")
                    + BoomboxParticleCatalog.ExtraName(BoomboxParticleCatalog.Extra(placement))
            );
        }

        private static Button Create(string name, RectTransform window, float x, Action action)
        {
            var button = BoomboxNativeUI.CleanButton(name, window, "", action);
            button.GetComponentInChildren<TMP_Text>().fontSize = FontSize;
            BoomboxNativeUI.Place((RectTransform)button.transform, x, Top, Width, Height);
            return button;
        }
    }
}
