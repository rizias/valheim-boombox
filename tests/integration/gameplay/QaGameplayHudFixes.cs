using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CaseUserFixes()
    {
        var plugin = BoomboxPlugin.Instance;
        var settings = plugin.Settings;
        var player = Player.m_localPlayer;
        var position = player.transform.position;
        var rotation = player.transform.rotation;
        var inputMode = UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior;
        var roots = new System.Collections.Generic.List<GameObject>();
        var entries = new BepInEx.Configuration.ConfigEntryBase[] {
            settings.ShowVisualEffects, settings.DecorationParticles, settings.DecorationMovement,
            settings.DecorationMusicMovement, settings.MusicEnchantment, settings.BoatHelp,
            settings.MusicOnly, settings.ShowPlayerPanel, settings.PlayerPanelPosition
        };
        var values = entries.Select(e => e.BoxedValue).ToArray();
        var cases = new JArray();
        var steps = new JArray();
        var proof = new JObject { ["case"] = "UserFixes", ["cases"] = cases, ["steps"] = steps };
        lastCapture = proof;
        float start = Time.realtimeSinceStartup;
        void Click(RectTransform rect)
        {
            var result = UiDispatch(rect, false);
            steps.Add(result);
            Need((bool)result["hitsTarget"], "UserFixes pointer missed " + rect.name);
        }
        try
        {
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior =
                UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            var device = CaseDevice("UserFixes", "Green", 1);
            var fresh = BoomboxItemData.Read(device);
            string id = fresh.deck.id;
            string cassetteId = fresh.deck.insertedCassette.id;
            string[] builtIns = { BoomboxItemData.DevicePrefab, BoomboxItemData.ArchivePrefab };
            Need(builtIns.All(fresh.deck.unlockedDecorationPrefabs.Contains), "Fresh device lacks built-in designs");
            var legacy = JObject.Parse(device.m_customData[BoomboxItemData.Key]);
            legacy["deck"]["unlockedDecorationPrefabs"] = new JArray("MushroomYellow");
            device.m_customData[BoomboxItemData.Key] = legacy.ToString(Newtonsoft.Json.Formatting.None);
            var normalized = BoomboxItemData.Read(device);
            Need(
                builtIns.All(normalized.deck.unlockedDecorationPrefabs.Contains)
                    && normalized.deck.unlockedDecorationPrefabs.Contains("MushroomYellow")
                    && normalized.deck.id == id && normalized.deck.insertedCassette.id == cassetteId,
                "Existing device normalization loses designs or identity"
            );
            cases.Add(new JObject { ["id"] = "built-in-designs-fresh-and-existing", ["passed"] = true });
            Need(player.EquipItem(device, false), "UserFixes fixture equip rejected");
            plugin.Items.Update();
            var deck = plugin.Items.Own;
            Need(deck != null && deck.State.id == id, "UserFixes own device unavailable");
            settings.ShowPlayerPanel.Value = true;
            settings.PlayerPanelPosition.Value = new Vector2(-1, -1);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.3f);
            var panel = GameObject.Find("BoomboxFloatingPlayer").GetComponent<RectTransform>();
            Click(UiButton("BoomboxMusicButton"));
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiButton("ShowPlayerPanel"));
            yield return new WaitForSecondsRealtime(.2f);
            Need(!settings.ShowPlayerPanel.Value && !panel.gameObject.activeInHierarchy, "OFF did not hide player");
            plugin.CloseMenu();
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiButton("BoomboxMusicButton"));
            yield return new WaitForSecondsRealtime(.2f);
            Need(plugin.MenuOpen, "OFF removed inventory music entry");
            Click(UiButton("ShowPlayerPanel"));
            yield return new WaitForSecondsRealtime(.2f);
            Need(panel.gameObject.activeInHierarchy && plugin.MenuOpen, "ON did not show player over open menu");
            var menuWindow = GameObject.Find("BoomboxMenuWindow").GetComponent<RectTransform>();
            Need(
                panel.GetComponentInParent<Canvas>().sortingOrder > menuWindow.GetComponentInParent<Canvas>().sortingOrder,
                "Player is behind menu"
            );
            Click(UiButton("ResetPlayerPanel"));
            yield return new WaitForSecondsRealtime(.2f);
            var resetPosition = panel.anchoredPosition;
            var handle = panel.Find("PlayerDragHandle").gameObject;
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                delta = new Vector2(100, -40)
            };
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.dragHandler);
            yield return null;
            Need(Vector2.Distance(panel.anchoredPosition, resetPosition) > 20, "Fixture interrupted drag did not move");
            Click(UiButton("ShowPlayerPanel"));
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiButton("ResetPlayerPanel"));
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                settings.ShowPlayerPanel.Value && panel.gameObject.activeInHierarchy
                    && settings.PlayerPanelPosition.Value == new Vector2(-1, -1)
                    && Vector2.Distance(panel.anchoredPosition, resetPosition) < .1f,
                "Reset did not recover visible player after interrupted drag"
            );
            cases.Add(new JObject { ["id"] = "player-toggle-entry-and-reset", ["passed"] = true });
            Click(UiButton("ShowPlayerPanel"));
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiButton("Tab4"));
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiButton("Group2"));
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiElement("SlotChip0"));
            int itemCount = player.GetInventory().GetAllItems().Count;
            int deviceCount = player.GetInventory().GetAllItems().Where(BoomboxItemData.IsDevice).Sum(i => i.m_stack);
            int cassetteCount = player.GetInventory().GetAllItems().Where(BoomboxItemData.IsCassette).Sum(i => i.m_stack);
            foreach (string prefab in builtIns)
            {
                UiInput("Search").text = Localization.instance.Localize(
                    ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name
                );
                yield return new WaitForSecondsRealtime(.2f);
                Click(UiElement("Design_" + prefab));
                yield return new WaitForSecondsRealtime(.2f);
                Need(UiButton("DecorationAction").GetComponent<UnityEngine.UI.Button>().interactable, "Built-in install unavailable");
                Click(UiButton("DecorationAction"));
                yield return new WaitForSecondsRealtime(.2f);
                var after = BoomboxItemData.Read(deck.Item).deck;
                Need(after.decorationPlacements.Any(p => p.slot == 0 && p.prefab == prefab), "Built-in design did not install");
                Need(
                    after.id == id && after.insertedCassette.id == cassetteId
                        && player.GetInventory().GetAllItems().Count == itemCount
                        && player.GetInventory().GetAllItems().Where(BoomboxItemData.IsDevice).Sum(i => i.m_stack) == deviceCount
                        && player.GetInventory().GetAllItems().Where(BoomboxItemData.IsCassette).Sum(i => i.m_stack) == cassetteCount,
                    "Built-in install consumed or replaced device/cassette"
                );
                cases.Add(new JObject { ["id"] = "built-in-install", ["prefab"] = prefab, ["passed"] = true });
            }
            UiInput("Search").text = "";
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            yield return new WaitForSecondsRealtime(.25f);
            var decorationRoot = deck.Visual.transform.Find("BoomboxDecorations");
            Need(decorationRoot != null && decorationRoot.childCount > 0, "Installed decoration geometry absent");
            var childIds = decorationRoot.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID()).ToArray();
            var forge = JourneyStation("forge", player.transform.position, roots);
            forge.m_craftRequireRoof = false;
            forge.m_craftRequireFire = false;
            Need(forge.CheckUsable(player, false) && forge.InUseDistance(player), "Fixture forge is not usable");
            proof["stationFixture"] = "Native forge with roof/fire requirements disabled only for this test; material costs remain enabled";
            forge.Interact(player, false, false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(InventoryGui.IsVisible() && player.GetCurrentCraftingStation() == forge, "Native forge UI did not open");
            Click((RectTransform)InventoryGui.instance.m_tabUpgrade.transform);
            yield return null;
            yield return JourneyCraft(ObjectDB.instance.m_recipes.Single(r => r.name == "Recipe_BoomboxUpgrade_2"), steps, id);
            yield return new WaitForSecondsRealtime(.25f);
            var upgraded = player.GetInventory().GetAllItems().Where(BoomboxItemData.IsDevice)
                .Single(i => BoomboxItemData.Read(i).deck.id == id);
            Need(BoomboxItemData.Read(upgraded).deck.insertedCassette.id == cassetteId, "Native upgrade changed inserted cassette");
            var refresh = System.Diagnostics.Stopwatch.StartNew();
            deck.VisualPresenter.RefreshState();
            refresh.Stop();
            Need(
                childIds.SequenceEqual(decorationRoot.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID())),
                "Tier-only native upgrade rebuilt decoration geometry"
            );
            cases.Add(new JObject
            {
                ["id"] = "native-paid-device-upgrade",
                ["tier"] = BoomboxItemData.Read(upgraded).deck.upgradeTier,
                ["decorationChildrenPreserved"] = true,
                ["refreshMillis"] = refresh.Elapsed.TotalMilliseconds,
                ["timingScope"] = "One presenter refresh after native paid upgrade; excludes crafting and fixture setup",
                ["passed"] = true
            });
            proof["seconds"] = Time.realtimeSinceStartup - start;
            Need((float)proof["seconds"] < 35f, "UserFixes exceeded 35-second case budget");
            proof["passed"] = true;
        }
        finally
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(
                UnityEngine.InputSystem.Keyboard.current,
                new UnityEngine.InputSystem.LowLevel.KeyboardState()
            );
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            player.SetCraftingStation(null);
            CasePlatformPlayer(player, position, rotation);
            player.GetComponent<ZSyncTransform>().SyncNow();
            Freeze(player, false);
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = inputMode;
            for (int index = 0; index < entries.Length; index++)
                entries[index].BoxedValue = values[index];
            foreach (var root in roots)
                if (root != null)
                {
                    var view = root.GetComponent<ZNetView>();
                    if (view != null && view.IsValid())
                        ZNetScene.instance.Destroy(root);
                    else
                        UnityEngine.Object.Destroy(root);
                }
            UiParkWindow();
        }
    }

    private IEnumerator HudFixesCapture()
    {
        yield return AwaitAudio();
        var plugin = BoomboxPlugin.Instance;
        var settings = plugin.Settings;
        var frames = new JArray();
        var cases = new JArray();
        var proof = new JObject { ["cases"] = cases, ["frames"] = frames, ["candidate"] = productSha };
        lastCapture = proof;
        float start = Time.realtimeSinceStartup;
        var originalVariant = settings.Effect.Variant.Value;
        float originalAmount = settings.Effect.Amount.Value;
        var originalPosition = settings.PlayerPanelPosition.Value;
        var runeSettings = settings.Effect.Variants[BoomboxMusicEffect.RunesAround];
        var entries = new BepInEx.Configuration.ConfigEntryBase[] {
            runeSettings.Brightness,
            runeSettings.Color,
            runeSettings.Threshold,
            runeSettings.ColorMode
        };
        var values = entries.Select(e => e.BoxedValue).ToArray();
        var properties = new System.Collections.Generic.Dictionary<string, int>(
            audioDeck.State.insertedCassette.properties
        );
        bool originalEffects = settings.ShowVisualEffects.Value,
            originalPlayer = settings.ShowPlayerPanel.Value,
            originalBuff = settings.ShowBuffPanel.Value;
        string language = Localization.instance.GetSelectedLanguage();
        bool oldDebugTime = EnvMan.instance.m_debugTimeOfDay;
        float oldTime = EnvMan.instance.m_debugTime;
        try
        {
            UiParkWindow(true);
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            Localization.instance.SetLanguage("Russian");
            settings.ShowVisualEffects.Value = settings.ShowPlayerPanel.Value = settings.ShowBuffPanel.Value = true;
            settings.Effect.Variant.Value = BoomboxMusicEffect.RunesAround;
            foreach (string color in new[] { "Red", "Green", "Yellow", "Blue", "Purple" })
                audioDeck.State.insertedCassette.properties[color] = 1;
            Player.m_localPlayer.AddKnownItem(audioDeck.Item);
            yield return new WaitForSecondsRealtime(.5f);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.35f);
            var panel = GameObject.Find("BoomboxFloatingPlayer").GetComponent<RectTransform>();
            var before = panel.anchoredPosition;
            yield return HudFrame("hud-default.png", frames);
            var pointer = new PointerEventData(
                EventSystem.current
            )
            {
                button = PointerEventData.InputButton.Left,
                delta = new Vector2(100, -40)
            };
            var handle = panel.Find("PlayerDragHandle").gameObject;
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.endDragHandler);
            yield return null;
            Need(Vector2.Distance(panel.anchoredPosition, before) > 20, "Player drag did not move");
            var moved = panel.anchoredPosition;
            InventoryGui.instance.Hide();
            yield return null;
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.2f);
            Need(Vector2.Distance(panel.anchoredPosition, moved) < .1f, "Player position lost on reopen");
            cases.Add(new JObject
            {
                ["id"] = "player-drag-reopen",
                ["distance"] = Vector2.Distance(before, moved),
                ["persisted"] = settings.PlayerPanelPosition.Value.ToString()
            });
            yield return HudFrame("hud-inventory.png", frames);
            UiDispatch(UiButton("OpenBonuses"), false);
            yield return new WaitForSecondsRealtime(.3f);
            var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
            Need((int)AccessTools.Field(menu.GetType(), "tab").GetValue(menu) == 7, "My bonuses opened wrong tab");
            var bonusValues = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(
                t => t.name.StartsWith("BonusValue") && t.gameObject.activeInHierarchy
            ).OrderBy(
                t => t.name
            ).ToArray();
            Need(bonusValues.Length == 5, "Missing bonus detail rows");
            var live = Player.m_localPlayer.GetSEMan()
                .GetStatusEffects()
                .OfType<BoomboxAuraStatus>()
                .OrderBy(s => s.BonusIndex)
                .ToArray();
            Need(live.Length == 5, "Five fixture bonuses absent");
            var window = GameObject.Find("BoomboxMenuWindow").transform;
            var wood = window.Find("WoodPanel").GetComponent<UnityEngine.UI.Image>();
            Need(
                wood.sprite != null && wood.sprite.name == "woodpanel_trophys",
                "Native Valheim background was replaced"
            );
            var close = UiButton("Close").GetComponent<UnityEngine.UI.Button>();
            Need(
                close.image.sprite == InventoryGui.instance.m_craftButton.image.sprite,
                "Native Valheim button artwork was replaced"
            );
            cases.Add(
                new JObject
                {
                    ["id"] = "native-style",
                    ["backgroundSprite"] = wood.sprite.name,
                    ["buttonSprite"] = close.image.sprite.name
                }
            );
            cases.Add(
                new JObject
                {
                    ["id"] = "bonus-details",
                    ["values"] = new JArray(bonusValues.Select(t => t.text)),
                    ["actual"] = new JArray(
                    live.Select(
                        s => new JObject
                        {
                            ["index"] = s.BonusIndex,
                            ["value"] = s.BonusValue,
                            ["period"] = s.BonusPeriod
                        }
                    )
                )
                }
            );
            for (int i = 0; i < 5; i++)
            {
                var numbers = System.Text.RegularExpressions.Regex.Matches(bonusValues[i].text, @"[+-]?\d+(?:[.,]\d+)?")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(
                    m => double.Parse(m.Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture)
                )
                    .ToArray();
                var expected = i == 0 || i == 4 ? new[] { 5d } : new[] { 1d, 5d };
                Need(
                    numbers.SequenceEqual(expected),
                    "Displayed bonus amount or period differs from independent fixture: " + bonusValues[i].text
                );
            }
            yield return HudFrame("hud-bonus-details.png", frames);
            plugin.CloseMenu();
            yield return new WaitForSecondsRealtime(.15f);
            UiDispatch(UiButton("BoomboxMusicButton"), false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                (int)AccessTools.Field(menu.GetType(), "tab").GetValue(menu) == 0,
                "Choose music did not select library after bonuses"
            );
            UiDispatch(UiButton("Tab5"), false);
            yield return null;
            var current = settings.Effect.Current;
            current.Brightness.Value = 0;
            current.Color.Value = Color.black;
            current.Threshold.Value = .9f;
            current.ColorMode.Value = BoomboxSparkColor.Bands;
            settings.Effect.Amount.Value = 2;
            UiDispatch(UiButton("ResetEffect"), false);
            yield return null;
            Need(
                current.Brightness.Value == (float)current.Brightness.DefaultValue
                && current.Color.Value == (Color)current.Color.DefaultValue
                && current.Threshold.Value == (float)current.Threshold.DefaultValue
                && current.ColorMode.Value == (BoomboxSparkColor)current.ColorMode.DefaultValue
                && settings.Effect.Amount.Value == (float)settings.Effect.Amount.DefaultValue,
                "Effect reset incomplete"
            );
            Need(settings.Effect.Variant.Value == BoomboxMusicEffect.RunesAround, "Reset changed variant");
            cases.Add(new JObject { ["id"] = "effect-reset", ["passed"] = true });
            UiDispatch(UiButton("Tab7"), false);
            yield return new WaitForSecondsRealtime(.2f);
            int buffsBefore = Player.m_localPlayer.GetSEMan().GetStatusEffects().OfType<BoomboxAuraStatus>().Count();
            UiDispatch(UiButton("ShowPlayerPanel"), false);
            UiDispatch(UiButton("BonusHudToggle"), false);
            yield return new WaitForSecondsRealtime(.25f);
            Need(!settings.ShowPlayerPanel.Value && !settings.ShowBuffPanel.Value, "Visibility buttons did not toggle");
            Need(!panel.gameObject.activeInHierarchy, "Hidden player remained active");
            Need(!GameObject.Find("BoomboxCompactBuffCanvas"), "Hidden buff HUD remained active");
            Need(
                buffsBefore == Player.m_localPlayer.GetSEMan().GetStatusEffects().OfType<BoomboxAuraStatus>().Count(),
                "Hiding HUD removed gameplay buffs"
            );
            plugin.CloseMenu();
            yield return new WaitForSecondsRealtime(.2f);
            var standaloneMusic = UiButton("BoomboxMusicButton");
            Need(standaloneMusic.gameObject.activeInHierarchy, "Hiding player removed the inventory music button");
            Need(
                (bool)UiDispatch(standaloneMusic, false)["hitsTarget"],
                "Inventory music button unreachable while player is hidden"
            );
            yield return new WaitForSecondsRealtime(.2f);
            Need(plugin.MenuOpen, "Inventory music button did not reopen the menu");
            UiDispatch(UiButton("Tab7"), false);
            yield return null;
            UiDispatch(UiButton("ShowPlayerPanel"), false);
            UiDispatch(UiButton("BonusHudToggle"), false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(panel.gameObject.activeInHierarchy && plugin.MenuOpen, "Enabled player is hidden while menu is open");
            Need(
                panel.GetComponentInParent<Canvas>().sortingOrder > window.GetComponentInParent<Canvas>().sortingOrder,
                "Enabled player is behind the menu"
            );
            var playerMusic = UiButton("BoomboxMusicButton");
            var playerHit = UiRaycast(playerMusic, false).gameObject;
            Need(playerHit != null && playerHit.transform.IsChildOf(playerMusic), "Enabled player button unreachable");
            UiDispatch(UiButton("ResetPlayerPanel"), false);
            yield return new WaitForSecondsRealtime(.2f);
            var resetPosition = panel.anchoredPosition;
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.dragHandler);
            yield return null;
            Need(Vector2.Distance(panel.anchoredPosition, resetPosition) > 20, "Interrupted drag did not move the player");
            UiDispatch(UiButton("ShowPlayerPanel"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("ResetPlayerPanel"), false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                settings.ShowPlayerPanel.Value && settings.PlayerPanelPosition.Value == new Vector2(-1, -1),
                "Player reset did not enable the default position"
            );
            Need(panel.gameObject.activeInHierarchy, "Player reset did not restore the visible panel");
            Need(Vector2.Distance(panel.anchoredPosition, resetPosition) < .1f, "Interrupted drag overrides player reset");
            cases.Add(new JObject { ["id"] = "player-toggle-and-interrupted-drag-reset", ["passed"] = true });
            yield return HudFrame("hud-settings.png", frames);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            yield return new WaitForSecondsRealtime(.3f);
            var hud = GameObject.Find("BoomboxCompactBuffs");
            var nativeRows = (System.Collections.Generic.List<RectTransform>)AccessTools.Field(
                typeof(Hud),
                "m_statusEffects"
            )
                .GetValue(Hud.instance);
            var points = new Vector3[4];
            hud.GetComponent<RectTransform>().GetWorldCorners(points);
            float hudTop = points[1].y;
            foreach (var row in nativeRows)
            {
                if (!row.gameObject.activeInHierarchy)
                    continue;
                row.GetWorldCorners(points);
                Need(hudTop < points[0].y, "Bonus HUD overlaps native status row");
            }
            var icons = hud.GetComponentsInChildren<UnityEngine.UI.Image>().Where(i => i.name == "BuffIcon").ToArray();
            Need(
                icons.Length == 5
                    && icons.All(i => i.sprite != null)
                    && icons.Select(i => i.sprite.name).Distinct().Count() == 5,
                "HUD must show five distinct game icons"
            );
            Need(icons.All(i => !i.sprite.name.StartsWith("BoomboxPlayerIcon")), "Mockup symbols leaked into game HUD");
            Need(
                hud.GetComponentsInChildren<TMP_Text>().Count(t => t.name.StartsWith("BuffValue")) == 5,
                "HUD must show actual values for five fixture bonuses"
            );
            var hover = hud.transform.Find("BuffRow1");
            ExecuteEvents.Execute(hover.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            yield return null;
            Need(GameObject.Find("BuffTooltip") != null, "Bonus hover missing");
            yield return HudFrame("hud-hover.png", frames);
            ExecuteEvents.Execute(hover.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            cases.Add(
                new JObject
                {
                    ["id"] = "buff-icons",
                    ["icons"] = new JArray(icons.Select(i => i.sprite.name)),
                    ["compactValues"] = true
                }
            );
            yield return HudFrame("hud-buffs.png", frames);
            proof["coreSeconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            settings.Effect.Variant.Value = originalVariant;
            settings.Effect.Amount.Value = originalAmount;
            for (int index = 0; index < entries.Length; index++)
                entries[index].BoxedValue = values[index];
            audioDeck.State.insertedCassette.properties = properties;
            settings.ShowVisualEffects.Value = originalEffects;
            settings.ShowPlayerPanel.Value = originalPlayer;
            settings.ShowBuffPanel.Value = originalBuff;
            settings.PlayerPanelPosition.Value = originalPosition;
            Localization.instance.SetLanguage(language);
            EnvMan.instance.m_debugTimeOfDay = oldDebugTime;
            EnvMan.instance.m_debugTime = oldTime;
            UiParkWindow();
        }
    }

    private static IEnumerator HudFrame(string name, JArray frames)
    {
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        var bytes = name.EndsWith(".jpg") ? texture.EncodeToJPG(90) : texture.EncodeToPNG();
        UnityEngine.Object.Destroy(texture);
        string folder = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "capture");
        System.IO.Directory.CreateDirectory(folder);
        string file = System.IO.Path.Combine(folder, name);
        System.IO.File.WriteAllBytes(file, bytes);
        frames.Add(
            new JObject
            {
                ["file"] = file,
                ["bytes"] = bytes.Length,
                ["sha"] = Hash(file),
                ["unalteredGameBackbuffer"] = true
            }
        );
    }
}
