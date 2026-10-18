using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxEnchantVisibility : MonoBehaviour
    {
        private ParticleSystem particles;
        private bool visible = true;
        private void Awake()
        {
            particles = GetComponent<ParticleSystem>();
        }
        private void Update()
        {
            bool next = BoomboxPlugin.Instance?.Settings?.DrawEnchantment == true;
            if (visible == next)
                return;
            visible = next;
            if (next)
                particles.Play();
            else
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    internal sealed class BoomboxVisualVisibility
    {
        private readonly RectTransform root;
        private readonly BoomboxSettings settings;
        private readonly TMPro.TMP_Text note;
        private readonly List<Action> refresh = new List<Action>();
        internal BoomboxVisualVisibility(BoomboxSettings settings, RectTransform parent)
        {
            this.settings = settings;
            root = BoomboxNativeUI.Rect("BoomboxVisualVisibility", parent);
            BoomboxNativeUI.Place(root, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            note = BoomboxNativeUI.FixedText("VisualVisibilityHelp", root, 18);
            BoomboxNativeUI.Place(note.rectTransform, 24, 166, 1012, 52);
            note.text = BoomboxText.Get("text.only_visuals_are_hidden_music_weapon_charge");
            Add("AllVisuals", settings.ShowVisualEffects, "visibility.AllVisuals");
            Add("MusicVisual", settings.ShowMusicEffects, "visibility.MusicVisual");
            Add("SpeakerVisual", settings.ShowSpeakerEffects, "visibility.SpeakerVisual");
            Add("MembraneVisual", settings.ShowMembraneMotion, "visibility.MembraneVisual");
            Add("EnchantVisual", settings.ShowEnchantmentVisuals, "visibility.EnchantVisual");
            Add("BodyVisual", settings.ShowBodyGlow, "visibility.BodyVisual");
            Add("CarvingVisual", settings.ShowSigurdGlow, "visibility.CarvingVisual");
            Add("CassetteVisual", settings.ShowCassetteSymbols, "visibility.CassetteVisual");
            Add("DecorationParticlesVisual", settings.DecorationParticles, "visibility.DecorationParticlesVisual");
            Add("DecorationMotionVisual", settings.DecorationMovement, "visibility.DecorationMotionVisual");
            Add("MushroomVisual", settings.ShowDecorationGlow, "visibility.MushroomVisual");
            Add("PatternVisual", settings.PatternPulse, "visibility.PatternVisual");
            Add("RemoteVisual", settings.ShowOthersWaves, "visibility.RemoteVisual");
            Add("ReelVisual", settings.ShowReelMotion, "visibility.ReelVisual");
            var hint = BoomboxNativeUI.FixedText("GameplayVisibilityHint", root, 17);
            BoomboxNativeUI.Place(hint.rectTransform, 24, 664, 1012, 60);
            hint.text =
                BoomboxText.Get("text.disable_gameplay_in_bonuses_device_gameplay_bonus");
            root.gameObject.SetActive(false);
        }
        private void Add(string name, ConfigEntry<bool> entry, string key)
        {
            int i = refresh.Count;
            var button = BoomboxNativeUI.CleanButton(name, root, "", () => entry.Value = !entry.Value);
            BoomboxNativeUI.Place((RectTransform)button.transform, 24 + (i % 2) * 520, 232 + (i / 2) * 58, 488, 40);
            refresh.Add(() => BoomboxNativeUI.Label(
                    button,
                    BoomboxText.Get(key) + ": " + BoomboxText.Get(entry.Value ? "text.on" : "text.off")
                ));
        }
        internal void Show(bool active)
        {
            root.gameObject.SetActive(active);
            if (!active)
                return;
            foreach (var action in refresh)
                action();
            note.text =
                settings.ShowVisualEffects.Value
                    ? BoomboxText.Get("text.only_visuals_are_hidden_music_weapon_charge")
                    : BoomboxText.Get("text.master_switch_visuals_are_hidden_individual_choices");
        }
    }
}
