using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator VisibilityCapture()
    {
        yield return AwaitAudio();
        var plugin = BoomboxPlugin.Instance;
        var s = plugin.Settings;
        var frames = new JArray();
        var proof = new JObject { ["candidate"] = productSha, ["frames"] = frames };
        lastCapture = proof;
        bool master = s.ShowVisualEffects.Value,
            enchant = s.ShowEnchantmentVisuals.Value,
            gameplay = s.MusicEnchantment.Value,
            personal = s.PersonalMusicOnly.Value,
            world = s.MusicOnly.Value;
        var offset = s.BuffOffset.Value;
        var anchor = s.BuffAnchor.Value;
        bool hud = s.ShowBuffPanel.Value;
        string language = Localization.instance.GetSelectedLanguage();
        float start = Time.realtimeSinceStartup;
        var oldProperties = new System.Collections.Generic.Dictionary<string, int>(
            audioDeck.State.insertedCassette.properties
        );
        try
        {
            UiParkWindow(true);
            Localization.instance.SetLanguage("Russian");
            s.ShowVisualEffects.Value =
                s.ShowEnchantmentVisuals.Value =
                s.MusicEnchantment.Value =
                s.ShowBuffPanel.Value =
                true;
            s.PersonalMusicOnly.Value = s.MusicOnly.Value = false;
            s.BuffAnchor.Value = BoomboxBuffAnchor.BelowMap;
            s.BuffOffset.Value = new Vector2(0, -80);
            foreach (string color in new[] { "Red", "Green", "Yellow", "Blue", "Purple" })
                audioDeck.State.insertedCassette.properties[color] = 1;
            AccessTools.Method(typeof(BoomboxAuraNetwork), "PublishImmediate").Invoke(null, null);
            yield return new WaitForSecondsRealtime(.5f);
            var info = Hud.instance.m_rootObject.transform.Find("MyLittleUI_Parent");
            Need(info != null, "Actual MyLittleUI not loaded");
            proof["myLittleUI"] = true;
            var buff = GameObject.Find("BoomboxCompactBuffs").GetComponent<RectTransform>();
            var corners = new Vector3[4];
            buff.GetWorldCorners(corners);
            float top = corners.Max(p => p.y);
            var names = new JArray();
            foreach (var g in info.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled)
                    continue;
                if (!g.transform.IsChildOf(info.Find("Winds")) && !g.transform.IsChildOf(info.Find("Clock"))
                && !g.transform.IsChildOf(info.Find("Forecast")))
                    continue;
                g.rectTransform.GetWorldCorners(corners);
                float bottom = corners.Min(p => p.y);
                Need(top < bottom, "Buff HUD overlaps " + g.name);
                names.Add(g.name);
            }
            Need(names.Count > 0, "No actual weather/clock graphics checked");
            proof["avoided"] = names;
            var icons = buff.GetComponentsInChildren<Image>().Where(i => i.name == "BuffIcon").ToArray();
            Need(icons.Length == 5, "Five bonus rows required");
            Need(icons.Count(i => i.sprite.name.StartsWith("Boombox approved")) == 4, "Approved icons not integrated");
            proof["icons"] = new JArray(icons.Select(i => i.sprite.name));
            yield return HudFrame("visibility-hud.jpg", frames);
            Player.m_localPlayer.AddKnownItem(audioDeck.Item);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.3f);
            UiDispatch(UiButton("OpenBonuses"), false);
            yield return new WaitForSecondsRealtime(.25f);
            yield return HudFrame("visibility-bonuses.jpg", frames);
            UiDispatch(UiButton("Tab5"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("EffectPageVisibility"), false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                !UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Any(
                    b => b.gameObject.activeInHierarchy
                        && (b.name == "BrightnessMore" || b.name == "StrikeThresholdMore" || b.name == "AmountMore")
                ),
                "Editor steppers leaked onto visibility page"
            );
            var weapons = typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxEnchantWeapons");
            AccessTools.Method(weapons, "Beat").Invoke(null, new object[] { Player.m_localPlayer.GetCurrentWeapon() });
            var charge = AccessTools.Field(weapons, "Melee").GetValue(null);
            float before = (float)AccessTools.Property(charge.GetType(), "Value").GetValue(charge);
            Need(before > 0, "Weapon charge precondition");
            UiDispatch(UiButton("EnchantVisual"), false);
            yield return new WaitForSecondsRealtime(.15f);
            Need(
                !s.ShowEnchantmentVisuals.Value && s.MusicEnchantment.Value && s.LocalGameplayEnabled,
                "Visual switch changed gameplay"
            );
            Need(
                (float)AccessTools.Property(charge.GetType(), "Value").GetValue(charge) > 0,
                "Hiding enchant visuals cleared charge"
            );
            UiDispatch(UiButton("AllVisuals"), false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                !s.ShowVisualEffects.Value && s.LocalGameplayEnabled && audioDeck.Source.isPlaying,
                "Global visual toggle changed gameplay/audio"
            );
            Need(
                Player.m_localPlayer.GetSEMan().GetStatusEffects().OfType<BoomboxAuraStatus>().Count() == 5,
                "Visual master removed buffs"
            );
            Need(
                (float)AccessTools.Property(charge.GetType(), "Value").GetValue(charge) > 0,
                "Visual master cleared weapon charge"
            );
            proof["chargeKept"] = true;
            proof["buffsKept"] = true;
            proof["musicKept"] = true;
            yield return HudFrame("visibility-settings.jpg", frames);
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            s.ShowVisualEffects.Value = master;
            s.ShowEnchantmentVisuals.Value = enchant;
            s.MusicEnchantment.Value = gameplay;
            s.PersonalMusicOnly.Value = personal;
            s.MusicOnly.Value = world;
            s.BuffOffset.Value = offset;
            s.BuffAnchor.Value = anchor;
            s.ShowBuffPanel.Value = hud;
            Localization.instance.SetLanguage(language);
            UiParkWindow();
            audioDeck.State.insertedCassette.properties = oldProperties;
            AccessTools.Method(typeof(BoomboxAuraNetwork), "PublishImmediate").Invoke(null, null);
        }
    }
}
