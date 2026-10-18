using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CaseLanguageFallback()
    {
        var plugin = BoomboxPlugin.Instance;
        string original = Localization.instance.GetSelectedLanguage();
        var frames = new JArray();
        var proof = new JObject
        {
            ["case"] = "L-05",
            ["scope"] = "German game language with English fallback for mod item text and actual menu labels.",
            ["frames"] = frames
        };
        lastCapture = proof;
        try
        {
            yield return CaseReadyInventory();
            CaseDevice("L05", "Green", 1);
            string[] keys = {
                "$boombox_name",
                "$boombox_description",
                "$boombox_cassette_archive",
                "$boombox_cassette_description",
                "$boombox_property_green",
                "$boombox_tutorial_assembly_text"
            };
            Localization.instance.SetLanguage("English");
            var expected = keys.Select(k => Localization.instance.Localize(k)).ToArray();
            string bonuses = "Bonuses";
            Localization.instance.SetLanguage("German");
            yield return null;
            var actual = keys.Select(k => Localization.instance.Localize(k)).ToArray();
            Need(
                expected.SequenceEqual(actual) && actual.All(t => !t.Contains("$boombox_")),
                "L05 item/tutorial fallback differs from English"
            );
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.6f);
            Need((bool)UiDispatch(UiButton("BoomboxMusicButton"), false)["hitsTarget"], "L05 menu button missed");
            yield return new WaitForSecondsRealtime(.4f);
            proof["menuBonuses"] = UiButton("Tab7").GetComponentInChildren<TMPro.TMP_Text>().text;
            Need((string)proof["menuBonuses"] == bonuses, "L05 actual bonuses label is not English");
            Need(
                UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None).Where(
                    t => t.gameObject.activeInHierarchy
                ).All(
                    t => !t.text.Contains("$boombox_")
                ),
                "L05 raw mod localization key visible"
            );
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "capture"));
            yield return UiFrame("L05-German-English-fallback.png", frames);
            proof["expected"] = new JArray(expected);
            proof["actual"] = new JArray(actual);
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Localization.instance.SetLanguage(original);
            Freeze(Player.m_localPlayer, false);
        }
    }

    private IEnumerator CaseUiScale()
    {
        var plugin = BoomboxPlugin.Instance;
        string language = Localization.instance.GetSelectedLanguage();
        float scale = (float)AccessTools.Field(typeof(GuiScaler), "m_largeGuiScale").GetValue(null);
        bool panel = plugin.Settings.ShowPlayerPanel.Value;
        var frames = new JArray();
        var controls = new JArray();
        var proof =
            new JObject
            {
                ["case"] = "C-16",
                ["scope"] =
                    "Native GuiScaler at 0.85/1/1.15, Russian and English, inventory/player and library. Native backbuffer captures require subsequent visual review.",
                ["frames"] = frames,
                ["controls"] = controls
            };
        lastCapture = proof;
        try
        {
            yield return CaseReadyInventory();
            CaseDevice("C16", "Red", 2);
            plugin.Settings.ShowPlayerPanel.Value = true;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "capture"));
            foreach (string locale in new[] { "Russian", "English" })
                foreach (float value in new[] { .85f, 1f, 1.15f })
                {
                    Localization.instance.SetLanguage(locale);
                    GuiScaler.SetScale(value);
                    plugin.CloseMenu();
                    InventoryGui.instance.Show(null, 1);
                    yield return new WaitForSecondsRealtime(.5f);
                    Canvas.ForceUpdateCanvases();
                    yield return UiFrame(
                        "C16-" + locale + "-"
                            + value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                            + "-inventory.png",
                        frames
                    );
                    var open = UiButton("BoomboxMusicButton");
                    var click = UiDispatch(open, false);
                    Need((bool)click["hitsTarget"], "C16 player menu button unreachable");
                    yield return new WaitForSecondsRealtime(.25f);
                    var tab = UiDispatch(UiButton("Tab7"), false);
                    Need((bool)tab["hitsTarget"], "C16 bonuses tab unreachable");
                    yield return new WaitForSecondsRealtime(.2f);
                    string name = "C16-" + locale + "-"
                        + value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        + ".png";
                    yield return UiFrame(name, frames);
                    var rect = UiButton("Close");
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    controls.Add(new JObject
                    {
                        ["language"] = locale,
                        ["scale"] = value,
                        ["closeCorners"] = new JArray(corners.Select(p => p.ToString())),
                        ["closeHit"] = UiRaycast(rect, false).gameObject?.name
                    });
                    var close = UiDispatch(rect, false);
                    Need((bool)close["hitsTarget"], "C16 close unreachable");
                    yield return null;
                }
            proof["actionsPassed"] = true;
            proof["visualReviewRequired"] = true;
            proof["passed"] = false;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            GuiScaler.SetScale(scale);
            Localization.instance.SetLanguage(language);
            plugin.Settings.ShowPlayerPanel.Value = panel;
            Freeze(Player.m_localPlayer, false);
        }
    }
}
