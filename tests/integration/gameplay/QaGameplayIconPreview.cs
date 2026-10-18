using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator IconPreview()
    {
        yield return AwaitAudio();
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        var sprites = new List<Sprite>();
        var textures = new List<Texture2D>();
        var frames = new JArray();
        var proof = new JObject
        {
            ["frames"] = frames,
            ["previewOnly"] = true,
            ["method"] = "Temporary sprites in the isolated live game; no product asset/profile installation"
        };
        lastCapture = proof;
        string input = Path.Combine(BoomboxQa.QaEnvironment.FixturesRoot, "icon-approval-preview");
        var cache = (Sprite[])AccessTools.Field(
            typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxBonusLook"),
            "icons"
        )
            .GetValue(null);
        var oldCache = (Sprite[])cache.Clone();
        var cassette = BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab);
        var spare = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
        spare.m_customData["boombox.qa.fixture"] = "ICON_PREVIEW";
        var oldDevice = audioDeck.Item.m_shared.m_icons;
        var oldTape = cassette.m_shared.m_icons;
        var cassetteCache = AccessTools.Field(typeof(BoomboxItemPrefabs), "cassetteIcon");
        var oldBasic = (Sprite)cassetteCache.GetValue(null);
        bool oldTimeOn = EnvMan.instance.m_debugTimeOfDay, oldBuff = plugin.Settings.ShowBuffPanel.Value;
        float oldTime = EnvMan.instance.m_debugTime;
        string language = Localization.instance.GetSelectedLanguage();
        var oldAnchor = plugin.Settings.BuffAnchor.Value;
        var oldScale = plugin.Settings.BuffScale.Value;
        var oldOffset = plugin.Settings.BuffOffset.Value;
        var oldProperties = new Dictionary<string, int>(audioDeck.State.insertedCassette.properties);
        var changes = new List<KeyValuePair<Image, Sprite>>();
        try
        {
            foreach (string name in new[] { "wave", "health", "stamina", "eitr", "cassette", "boombox" })
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Need(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(Path.Combine(input, name + ".png"))),
                    "Preview PNG invalid"
                );
                texture.filterMode = FilterMode.Bilinear;
                textures.Add(texture);
                Need(texture.width == texture.height, "Preview asset must use a square canvas: " + name);
                Need(texture.GetPixel(0, 0).a < .01f, "Preview background is not transparent: " + name);
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(.5f, .5f)
                );
                sprite.name = "Preview_" + name;
                sprites.Add(sprite);
            }
            UiParkWindow(true);
            Localization.instance.SetLanguage("Russian");
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            plugin.Settings.ShowBuffPanel.Value = true;
            foreach (string color in new[] { "Red", "Green", "Yellow", "Blue", "Purple" })
                audioDeck.State.insertedCassette.properties[color] = 1;
            for (int i = 0; i < 4; i++)
                cache[i] = sprites[i];
            audioDeck.Item.m_shared.m_icons = new[] { sprites[5] };
            cassette.m_shared.m_icons = new[] { sprites[4] };
            cassetteCache.SetValue(null, sprites[4]);
            Need(cassette.GetIcon() == sprites[4], "Cassette GetIcon still overrides the preview");
            Need(player.GetInventory().AddItem(cassette), "No free QA inventory slot");
            Need(player.GetInventory().AddItem(spare), "No free QA comparison slot");
            player.AddKnownItem(audioDeck.Item);
            yield return new WaitForSecondsRealtime(.5f);
            var hud = GameObject.Find("BoomboxCompactBuffs");
            Need(hud != null, "Preview HUD absent");
            for (int i = 0; i < 4; i++)
            {
                var image = hud.transform.Find("BuffRow" + i).GetComponentsInChildren<Image>(true).Single(x => x.name == "BuffIcon");
                changes.Add(new KeyValuePair<Image, Sprite>(image, image.sprite));
                image.sprite = sprites[i];
            }
            float started = Time.realtimeSinceStartup;
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.3f);
            yield return HudFrame("icon-preview-inventory.png", frames);
            UiDispatch(UiButton("OpenBonuses"), false);
            yield return new WaitForSecondsRealtime(.3f);
            yield return HudFrame("icon-preview-bonuses.png", frames);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            yield return new WaitForSecondsRealtime(.3f);
            var valueLabels = hud.GetComponentsInChildren<TMPro.TMP_Text>()
                .Where(t => t.name.StartsWith("BuffValue"))
                .ToArray();
            Need(valueLabels.Length == 5 && valueLabels.All(t => t.text.Contains("+")), "Compact HUD values missing");
            proof["hudValues"] = new JArray(valueLabels.Select(t => t.text));
            var positions = new JArray();
            proof["anchors"] = positions;
            foreach (BoomboxBuffAnchor anchor in Enum.GetValues(typeof(BoomboxBuffAnchor)))
            {
                plugin.Settings.BuffAnchor.Value = anchor;
                yield return new WaitForSecondsRealtime(.25f);
                positions.Add(
                    new JObject
                    {
                        ["anchor"] = anchor.ToString(),
                        ["position"] = hud.GetComponent<RectTransform>().anchoredPosition.ToString()
                    }
                );
            }
            plugin.Settings.BuffAnchor.Value = BoomboxBuffAnchor.BelowMap;
            yield return new WaitForSecondsRealtime(.25f);
            var map = Minimap.instance.m_mapImageSmall.rectTransform;
            var corners = new Vector3[4];
            map.GetWorldCorners(corners);
            float bottom = corners[0].y;
            hud.GetComponent<RectTransform>().GetWorldCorners(corners);
            Need(corners[1].y < bottom, "Below-map HUD overlaps minimap");
            yield return HudFrame("icon-preview-world.png", frames);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.15f);
            UiDispatch(UiButton("BoomboxMusicButton"), false);
            yield return new WaitForSecondsRealtime(.15f);
            UiDispatch(UiButton("Tab7"), false);
            yield return new WaitForSecondsRealtime(.15f);
            Need(
                UiButton("BuffAnchor").transform.IsChildOf(GameObject.Find("BoomboxBonusPanel").transform),
                "HUD placement controls are outside Bonuses"
            );
            UiDispatch(UiButton("BuffScaleMore"), false);
            UiDispatch(UiButton("BuffOffsetXMore"), false);
            UiDispatch(UiButton("BuffOffsetYMore"), false);
            Need(
                plugin.Settings.BuffScale.Value > 1 && plugin.Settings.BuffOffset.Value == new Vector2(10, 10),
                "HUD settings controls failed"
            );
            UiDispatch(UiButton("ResetBuffLayout"), false);
            Need(
                plugin.Settings.BuffScale.Value == 1 && plugin.Settings.BuffOffset.Value == Vector2.zero,
                "HUD layout reset failed"
            );
            yield return HudFrame("icon-preview-settings.png", frames);
            UiDispatch(UiButton("Tab5"), false);
            yield return new WaitForSecondsRealtime(.15f);
            Need(
                !UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Any(
                    b => b.name == "BuffAnchor" && b.gameObject.activeInHierarchy
                ),
                "Effects still contains bonus layout controls"
            );
            yield return HudFrame("icon-preview-effects.png", frames);
            proof["seconds"] = Time.realtimeSinceStartup - started;
            proof["hudIconPixels"] = 36 * Screen.height / 1080f * plugin.Settings.BuffScale.Value;
            proof["width"] = Screen.width;
            proof["height"] = Screen.height;
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
            AccessTools.Method(menu.GetType(), "Dispose").Invoke(menu, null);
            Array.Copy(oldCache, cache, oldCache.Length);
            foreach (var change in changes)
                if (change.Key != null)
                    change.Key.sprite = change.Value;
            audioDeck.Item.m_shared.m_icons = oldDevice;
            cassette.m_shared.m_icons = oldTape;
            player.GetInventory().RemoveItem(cassette);
            player.GetInventory().RemoveItem(spare);
            cassetteCache.SetValue(null, oldBasic);
            EnvMan.instance.m_debugTimeOfDay = oldTimeOn;
            EnvMan.instance.m_debugTime = oldTime;
            plugin.Settings.ShowBuffPanel.Value = oldBuff;
            Localization.instance.SetLanguage(language);
            plugin.Settings.BuffAnchor.Value = oldAnchor;
            plugin.Settings.BuffScale.Value = oldScale;
            plugin.Settings.BuffOffset.Value = oldOffset;
            audioDeck.State.insertedCassette.properties = oldProperties;
            foreach (var sprite in sprites)
                UnityEngine.Object.Destroy(sprite);
            foreach (var texture in textures)
                UnityEngine.Object.Destroy(texture);
            UiParkWindow();
        }
    }
}
