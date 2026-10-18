using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxGameplayPanel : IDisposable
    {
        private readonly RectTransform root;
        private readonly BoomboxPlugin plugin;
        private readonly List<Action> refresh = new List<Action>();
        private readonly TMP_Text status;
        internal BoomboxGameplayPanel(BoomboxPlugin owner, RectTransform parent)
        {
            plugin = owner;
            root = BoomboxNativeUI.Rect("BoomboxGameplayPanel", parent);
            BoomboxNativeUI.Place(root, 0, 0, 1060, 760);
            Toggle(
                "PersonalMusicOnly",
                220,
                owner.Settings.PersonalMusicOnly,
                () => BoomboxText.Get("text.music_only"),
                () => BoomboxText.Get("text.when_enabled_keeps_audio_and_visuals_but"),
                true
            );
            Toggle(
                "CreatureDamage",
                296,
                owner.Settings.Effect.CreatureDamage,
                () => BoomboxText.Get("text.wave_damage_and_enemy_pushes"),
                () => BoomboxText.Get("text.damage_and_knockback_to_creatures_from_music_waves")
            );
            Toggle(
                "MusicEnchantment",
                372,
                owner.Settings.MusicEnchantment,
                () => BoomboxText.Get("text.weapon_and_shield_enchantment"),
                () => BoomboxText.Get("text.charges_the_carrier_s_weapons_staves_bow")
            );
            Toggle(
                "BoatHelp",
                448,
                owner.Settings.BoatHelp,
                () => BoomboxText.Get("text.boat_assistance"),
                () => BoomboxText.Get("text.assists_the_boat_while_the_carrier_is_at_the_helm")
            );
            Toggle(
                "PushEnvironment",
                524,
                owner.Settings.PushEnvironment,
                () => BoomboxText.Get("text.world_item_pushes"),
                () => BoomboxText.Get("text.physical_impulses_on_nearby_loose_objects")
            );
            status = BoomboxNativeUI.FixedText("GameplayModeStatus", root, 17);
            BoomboxNativeUI.Place(status.rectTransform, 24, 618, 1000, 116);
        }

        private void Toggle(
            string name,
            float y,
            ConfigEntry<bool> entry,
            Func<string> label,
            Func<string> description,
            bool master = false
        )
        {
            var button = BoomboxNativeUI.CleanButton(
            name,
            root,
            "",
            () =>
                {
                    entry.Value = !entry.Value;
                    if (!plugin.Settings.LocalGameplayEnabled || !plugin.Settings.MusicEnchantment.Value)
                    {
                        BoomboxEnchantment.ResetGameplay();
                    }
                    BoomboxAuraNetwork.PublishImmediate();
                }
            );
            BoomboxNativeUI.Place((RectTransform)button.transform, 24, y, 455, 38);
            var text = BoomboxNativeUI.FixedText(name + "Description", root, 17);
            BoomboxNativeUI.Place(text.rectTransform, 505, y, 525, 64);
            refresh.Add(() =>
            {
                string state = !master && !plugin.Settings.LocalGameplayEnabled
                    ? BoomboxText.Get("text.off_by_mode")
                    : entry.Value
                        ? BoomboxText.Get("text.on")
                        : BoomboxText.Get("text.off");
                BoomboxNativeUI.Label(button, label() + ": " + state);
                text.text = description();
                button.interactable = master || plugin.Settings.LocalGameplayEnabled;
            });
        }
        internal void Show()
        {
            root.gameObject.SetActive(true);
            foreach (var update in refresh)
                update();
            status.text =
                plugin.Settings.LocalGameplayEnabled
                    ? BoomboxText.Get("text.individual_switches_affect_your_device_the_world")
                    : BoomboxText.Get("text.your_device_s_gameplay_effects_are_disabled");
            status.text +=
                "\n" + BoomboxText.Get("text.bonuses_from_other_devices_still_follow_world");
            if (BoomboxAuraNetwork.Balance?.MusicOnly == true)
                status.text += "\n" + BoomboxText.Get("text.the_host_has_disabled_gameplay_effects_for_everyone");
        }
        internal void Hide() => root.gameObject.SetActive(false);
        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
