using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal static class BoomboxBonusLook
    {
        private static readonly Sprite[] icons = new Sprite[5];
        internal static Sprite Icon(int index)
        {
            if (icons[index] != null)
                return icons[index];
            if (index < 4)
                return icons[index] = BoomboxBonusArt.Get(index);
            icons[index] = Player.m_localPlayer?.GetSkills().m_skills.FirstOrDefault(s => s.m_skill
                == Skills.SkillType.Run)?.m_icon;
            return icons[index];
        }
        internal static string Description(int index)
        {
            string[] keys = {
                "text.strengthens_music_waves_not_weapon_damage",
                "text.restores_health_not_maximum_health",
                "text.restores_stamina",
                "text.restores_eitr",
                "text.increases_movement_speed"
            };
            return BoomboxText.Get(keys[index]);
        }
        internal static string Value(BoomboxAuraStatus effect, int index)
        {
            string value = "+" + BoomboxAuraHud.Number(effect?.BonusValue ?? 0f, index);
            if (index > 0 && index < 4 && effect != null)
                value += BoomboxText.Get("text.every") + effect.BonusPeriod.ToString("0.##", BoomboxText.Culture)
                    + BoomboxText.Get("text.s");
            return value;
        }
    }

    internal sealed class BoomboxBonusPanel : IDisposable
    {
        private readonly RectTransform root;
        private readonly TMP_Text heading, summary;
        private readonly TMP_Text[] names = new TMP_Text[5],
            descriptions = new TMP_Text[5],
            values = new TMP_Text[5],
            sources = new TMP_Text[5];
        private readonly Button toggle;
        private readonly Button anchor, reset;
        private readonly TMP_Text layoutHeading;
        private readonly System.Collections.Generic.List<Action> refreshSettings =
            new System.Collections.Generic.List<Action>();
        private readonly BoomboxPlugin plugin;
        private readonly BoomboxAuraStatus[] current = new BoomboxAuraStatus[5];
        private float next;
        internal BoomboxBonusPanel(BoomboxPlugin owner, RectTransform parent, bool embedded = false)
        {
            plugin = owner;
            root = BoomboxNativeUI.Rect("BoomboxBonusPanel", parent);
            BoomboxNativeUI.Place(root, 0, 0, 1060, 760);
            heading = BoomboxNativeUI.FixedText("BonusHeading", root, 24, true);
            BoomboxNativeUI.Place(heading.rectTransform, 24, 120, 1000, 34);
            summary = BoomboxNativeUI.FixedText("BonusSummary", root, 17);
            BoomboxNativeUI.Place(summary.rectTransform, 24, 164, 630, 44);
            for (int i = 0; i < 5; i++)
            {
                float y = 220 + i * 94;
                var icon = BoomboxNativeUI.Image(
                    "BonusIcon" + i,
                    root,
                    BoomboxBonusLook.Icon(i),
                    Color.white
                );
                icon.type = Image.Type.Simple;
                icon.preserveAspect = true;
                BoomboxNativeUI.Place(icon.rectTransform, 28, y + 9, 42, 42);
                names[i] = BoomboxNativeUI.FixedText("BonusName" + i, root, 21, true);
                BoomboxNativeUI.Place(names[i].rectTransform, 88, y + 3, 230, 28);
                descriptions[i] = BoomboxNativeUI.FixedText("BonusDescription" + i, root, 17);
                BoomboxNativeUI.Place(descriptions[i].rectTransform, 88, y + 33, 560, 28);
                values[i] = BoomboxNativeUI.FixedText("BonusValue" + i, root, 19);
                values[i].alignment = TextAlignmentOptions.TopRight;
                BoomboxNativeUI.Place(values[i].rectTransform, 390, y + 3, 260, 30);
                sources[i] = BoomboxNativeUI.FixedText("BonusSources" + i, root, 15);
                sources[i].overflowMode = TextOverflowModes.Ellipsis;
                BoomboxNativeUI.Place(sources[i].rectTransform, 88, y + 64, 562, 24);
            }
            toggle = BoomboxNativeUI.CleanButton(
                "BonusHudToggle",
                root,
                "",
                () => plugin.Settings.ShowBuffPanel.Value = !plugin.Settings.ShowBuffPanel.Value
            );
            layoutHeading = BoomboxNativeUI.FixedText("BonusLayoutHeading", root, 20, true);
            BoomboxNativeUI.Place(layoutHeading.rectTransform, 686, 168, 340, 36);
            BoomboxNativeUI.Place((RectTransform)toggle.transform, 686, 220, 340, 38);
            anchor =
                BoomboxNativeUI.CleanButton(
                "BuffAnchor",
                root,
                "",
                () => plugin.Settings.BuffAnchor.Value =
                    (BoomboxBuffAnchor)(((int)plugin.Settings.BuffAnchor.Value + 1) % 4)
            );
            BoomboxNativeUI.Place((RectTransform)anchor.transform, 686, 274, 340, 38);
            Setting(
                "BuffScale",
                334,
                () => BoomboxText.Get("text.size") + Mathf.RoundToInt(plugin.Settings.BuffScale.Value * 100) + "%",
                d => plugin.Settings.BuffScale.Value = Mathf.Clamp(plugin.Settings.BuffScale.Value + d * .1f, .7f, 1.5f)
            );
            Setting(
                "BuffOffsetX",
                392,
                () => BoomboxText.Get("text.right") + plugin.Settings.BuffOffset.Value.x.ToString(BoomboxText.Culture),
                d => plugin.Settings.BuffOffset.Value = new Vector2(
                    Mathf.Clamp(plugin.Settings.BuffOffset.Value.x + d * 10, -600, 600),
                    plugin.Settings.BuffOffset.Value.y
                )
            );
            Setting(
                "BuffOffsetY",
                450,
                () => BoomboxText.Get("text.down") + plugin.Settings.BuffOffset.Value.y.ToString(BoomboxText.Culture),
                d => plugin.Settings.BuffOffset.Value = new Vector2(
                    plugin.Settings.BuffOffset.Value.x,
                    Mathf.Clamp(plugin.Settings.BuffOffset.Value.y + d * 10, -600, 600)
                )
            );
            reset = BoomboxNativeUI.CleanButton(
            "ResetBuffLayout",
            root,
            "",
            () =>
                {
                    plugin.Settings.BuffAnchor.Value = BoomboxBuffAnchor.BelowMap;
                    plugin.Settings.BuffOffset.Value = Vector2.zero;
                    plugin.Settings.BuffScale.Value = 1;
                }
            );
            BoomboxNativeUI.Place((RectTransform)reset.transform, 686, 516, 340, 38);
            if (embedded)
            {
                heading.gameObject.SetActive(false);
                foreach (RectTransform child in root)
                    child.anchoredPosition += new Vector2(0, -54);
            }
        }
        internal void Show()
        {
            root.gameObject.SetActive(true);
            if (Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + .2f;
            heading.text = BoomboxText.Get("text.bonuses");
            summary.text = BoomboxText.Get("text.active_bonuses_from_your_boombox_and_nearby_devices");
            layoutHeading.text = BoomboxText.Get("text.on_the_game_hud");
            string[] anchors = {
                BoomboxText.Get("text.below_minimap"),
                BoomboxText.Get("text.left_of_minimap"),
                BoomboxText.Get("text.top_left"),
                BoomboxText.Get("text.bottom_right")
            };
            BoomboxNativeUI.Label(anchor, anchors[(int)plugin.Settings.BuffAnchor.Value]);
            BoomboxNativeUI.Label(reset, BoomboxText.Get("text.reset_layout"));
            foreach (var refresh in refreshSettings)
                refresh();
            Array.Clear(current, 0, current.Length);
            if (Player.m_localPlayer != null)
                foreach (var status in Player.m_localPlayer.GetSEMan().GetStatusEffects())
                    if (status is BoomboxAuraStatus aura && aura.BonusIndex >= 0 && aura.BonusIndex < 5)
                        current[aura.BonusIndex] = aura;
            for (int i = 0; i < 5; i++)
            {
                names[i].text = BoomboxAuraHud.Name(i);
                descriptions[i].text = BoomboxBonusLook.Description(i);
                values[i].text = BoomboxBonusLook.Value(current[i], i);
                string detail = current[i]?.GetTooltipString();
                int split = detail?.IndexOf('\n') ?? -1;
                sources[i].text = split < 0
                    ? BoomboxText.Get("text.no_active_source")
                    : detail.Substring(split + 1).Replace("\n", " · ");
            }
            BoomboxNativeUI.Label(
                toggle,
                BoomboxText.Get("text.hud_icons") + (plugin.Settings.ShowBuffPanel.Value
                    ? BoomboxText.Get("text.on")
                    : BoomboxText.Get("text.off"))
            );
        }
        internal void Hide()
        {
            root.gameObject.SetActive(false);
            next = 0;
        }
        private void Setting(string name, float y, Func<string> label, Action<int> change)
        {
            var less = BoomboxNativeUI.CleanButton(
            name + "Less",
            root,
            "−",
            () =>
                {
                    change(-1);
                    next = 0;
                }
            );
            var more = BoomboxNativeUI.CleanButton(
            name + "More",
            root,
            "+",
            () =>
                {
                    change(1);
                    next = 0;
                }
            );
            var text = BoomboxNativeUI.FixedText(name + "Value", root, 17);
            text.alignment = TextAlignmentOptions.Center;
            BoomboxNativeUI.Place((RectTransform)less.transform, 686, y, 34, 34);
            BoomboxNativeUI.Place(text.rectTransform, 726, y, 260, 34);
            BoomboxNativeUI.Place((RectTransform)more.transform, 992, y, 34, 34);
            refreshSettings.Add(() => text.text = label());
        }
        public void Dispose()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
