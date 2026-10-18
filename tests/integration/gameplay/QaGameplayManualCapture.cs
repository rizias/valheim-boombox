using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator ManualCapture()
    {
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        Need(player != null && plugin.AudioWorldActive, "Manual capture requires the isolated ready actor/world");
        string publicMusic = Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic);
        Need(Directory.Exists(publicMusic), "Public Music directory must exist for the read-only folder example");
        string wave = Path.Combine(BoomboxQa.QaEnvironment.QaRoot, "current/r12-video/demo-source.wav");
        Need(File.Exists(wave), "Existing owned mathematical WAV fixture missing");
        string language = Localization.instance.GetSelectedLanguage();
        string playerName = player.GetPlayerName();
        var playerData = player.GetComponent<ZNetView>().GetZDO();
        var profile = Game.instance.GetPlayerProfile();
        string profileName = profile.GetName();
        var chat = Chat.instance != null ? Chat.instance.gameObject : null;
        bool chatVisible = chat != null && chat.activeSelf;
        var chatElements = new List<KeyValuePair<GameObject, bool>>();
        var knownMaterial = (HashSet<string>)AccessTools.Field(typeof(Player), "m_knownMaterial").GetValue(player);
        var knownRecipes = (HashSet<string>)AccessTools.Field(typeof(Player), "m_knownRecipes").GetValue(player);
        var originalMaterials = knownMaterial.ToArray();
        var originalRecipes = knownRecipes.ToArray();
        var body = player.GetComponent<Rigidbody>();
        var constraints = body.constraints;
        var config = plugin.Config.Select(pair => new KeyValuePair<ConfigEntryBase, object>(pair.Value, pair.Value.BoxedValue)).ToArray();
        bool saveConfig = plugin.Config.SaveOnConfigSet;
        var library = plugin.MusicLibrary;
        var catalog = plugin.Catalog;
        var lookupField = AccessTools.Field(typeof(BoomboxPlugin), "trackLookup");
        var lookup = lookupField.GetValue(plugin);
        var revision = AccessTools.Property(typeof(BoomboxPlugin), "CatalogRevision");
        int originalRevision = plugin.CatalogRevision;
        bool debugTime = EnvMan.instance.m_debugTimeOfDay;
        float hour = EnvMan.instance.m_debugTime;
        var frames = new JArray();
        var names = new JObject();
        var proof = new JObject
        {
            ["candidate"] = productSha,
            ["documentationOnly"] = true,
            ["scope"] = "Real Unity UI states selected for documentation; this does not establish player-action gameplay acceptance",
            ["frames"] = frames,
            ["names"] = names,
            ["publicFolderReadOnly"] = publicMusic,
            ["audioFixture"] = "Existing owned mathematical WAV; no personal music or desktop audio capture"
        };
        lastCapture = proof;
        var spare = BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab);
        var menuField = AccessTools.Field(typeof(BoomboxPlugin), "menu");
        float started = Time.realtimeSinceStartup;
        try
        {
            plugin.Config.SaveOnConfigSet = false;
            playerData.Set(ZDOVars.s_playerName, "Skald");
            profile.SetName("Skald");
            Game.instance.GetPlayerProfile().SetName("Skald");
            if (chat != null)
                chat.SetActive(false);
            Execute("host", new JObject());
            bool beforeCutscene = player.InCutscene();
            yield return CaseReadyInventory();
            if (Chat.instance != null)
            {
                var window = (RectTransform)AccessTools.Field(typeof(Terminal), "m_chatWindow").GetValue(Chat.instance);
                chatElements.Add(new KeyValuePair<GameObject, bool>(window.gameObject, window.gameObject.activeSelf));
                var worldTexts = (IEnumerable)AccessTools.Field(typeof(Chat), "m_worldTexts").GetValue(Chat.instance);
                foreach (var text in worldTexts)
                {
                    var gui = (GameObject)AccessTools.Field(text.GetType(), "m_gui").GetValue(text);
                    if (gui != null)
                        chatElements.Add(new KeyValuePair<GameObject, bool>(gui, gui.activeSelf));
                }
                foreach (var element in chatElements)
                    element.Key.SetActive(false);
            }
            proof["actorPreparation"] = new JObject
            {
                ["beforeCutscene"] = beforeCutscene,
                ["afterCutscene"] = player.InCutscene(),
                ["method"] = "Existing CaseReadyInventory native cinematic/sleeping completion and bounded animator updates"
            };
            foreach (string selectedLanguage in new[] { "English", "Russian" })
            {
                Localization.instance.SetLanguage(selectedLanguage);
                names[selectedLanguage == "Russian" ? "Ru" : "En"] = ManualNativeNames();
            }
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            plugin.Settings.PersonalMusicOnly.Value = false;
            plugin.Settings.MusicOnly.Value = false;
            plugin.Settings.ShowBuffPanel.Value = true;
            plugin.Settings.ShowPlayerPanel.Value = true;
            plugin.Settings.Effect.CreatureDamage.Value = true;
            plugin.Settings.Effect.Variant.Value = BoomboxMusicEffect.RunesAround;
            plugin.Settings.Effect.Current.Threshold.Value = .3f;
            plugin.Settings.Effect.Current.Brightness.Value = 1.4f;
            plugin.FFmpegPath.Value = BoomboxQa.QaEnvironment.Encoder;
            plugin.FFprobePath.Value = BoomboxQa.QaEnvironment.MediaProbe;
            plugin.MusicLibrary = new UserMusicLibrary();
            plugin.Catalog = new MusicCatalog();
            lookupField.SetValue(plugin, new Dictionary<string, MusicTrack>());
            Create("MANUAL", new[] { 1, 1, 1, 1, 1 }, 0, false, player.transform.position);
            audioDeck = decks["MANUAL"];
            audioDeck.State.volume = .05f;
            audioDeck.State.repeat = true;
            audioDeck.State.unlockedDecorationPrefabs.Add("TrophySkeleton");
            audioDeck.State.decorationPlacements.Add(new BoomboxDecorationPlacement
            {
                slot = 17,
                prefab = "TrophySkeleton",
                effect = "Health",
                effectParticles = false
            });
            Need(player.GetInventory().AddItem(spare), "No inventory slot for the sample cassette");
            player.AddKnownItem(audioDeck.Item);
            player.AddKnownItem(spare);
            Need(BoomboxEquipment.Known && BoomboxEquipment.Get(player) == audioDeck.Item,
                "Documentation actor must know and wear the fixture before opening the inventory");
            foreach (string selectedLanguage in new[] { "English", "Russian" })
            {
                bool ru = selectedLanguage == "Russian";
                string prefix = "manual-" + (ru ? "ru" : "en") + "-";
                playerData.Set(ZDOVars.s_playerName, ru ? "Скальд" : "Skald");
                profile.SetName(ru ? "Скальд" : "Skald");
                Game.instance.GetPlayerProfile().SetName(ru ? "Скальд" : "Skald");
                Need(player.GetPlayerName() == (ru ? "Скальд" : "Skald"), "Fixture source name did not update");
                Localization.instance.SetLanguage(selectedLanguage);
                plugin.CloseMenu();
                var existingMenu = menuField.GetValue(plugin);
                if (existingMenu != null)
                    AccessTools.Method(existingMenu.GetType(), "Dispose").Invoke(existingMenu, null);
                plugin.MusicLibrary.playlists.Clear();
                plugin.MusicLibrary.rememberedTracks.Clear();
                var track = new MusicTrack
                {
                    id = "manual-skald-song",
                    title = ru ? "Песнь у костра" : "Song by the fire",
                    artist = ru ? "Скальд" : "Skald",
                    album = ru ? "Песни странствий" : "Songs of the road",
                    source = "file://" + wave,
                    path = wave,
                    available = true,
                    trackNumber = 1
                };
                plugin.Catalog.playlists = new List<MusicPlaylist>
                {
                    new MusicPlaylist { id = "manual-album", name = track.album, tracks = new List<MusicTrack> { track } }
                };
                plugin.MusicLibrary.Remember(track);
                var playlist = plugin.MusicLibrary.Create(ru ? "У костра" : "By the fire", new[] { track.id });
                revision.SetValue(plugin, originalRevision + (ru ? 2 : 1));
                plugin.PlayQueue(audioDeck, new List<string> { track.id }, playlist.id, 0);
                yield return AwaitAudio();
                yield return new WaitForSecondsRealtime(3.5f);
                AccessTools.Method(typeof(BoomboxAuraNetwork), "PublishImmediate").Invoke(null, null);
                plugin.Notice = "";
                InventoryGui.instance.Show(null, 1);
                yield return UiAwait(() => InventoryGui.IsVisible() && !player.InCutscene()
                    && InventoryGui.instance.m_player.gameObject.activeInHierarchy
                    && UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                        .Any(button => button.name == "BoomboxMusicButton" && button.gameObject.activeInHierarchy),
                    "Documentation inventory and music button must remain visible after native Update");
                Need(UiButton("BoomboxMusicButton").GetComponent<Button>().interactable,
                    "Inventory music-open button is absent or disabled");
                yield return ManualFrame(prefix + "inventory.png", frames, started, proof);
                UiButton("BoomboxMusicButton").GetComponent<Button>().onClick.Invoke();
                yield return new WaitForSecondsRealtime(.2f);
                var menu = menuField.GetValue(plugin);
                var states = new[]
                {
                    (Name: "library", Tab: 0, Page: -1),
                    (Name: "folders", Tab: 2, Page: -1),
                    (Name: "playlists", Tab: 1, Page: -1),
                    (Name: "cassette", Tab: 3, Page: -1),
                    (Name: "decorations", Tab: 4, Page: -1),
                    (Name: "effects", Tab: 5, Page: -1),
                    (Name: "bonuses", Tab: 7, Page: 0),
                    (Name: "gameplay", Tab: 7, Page: 1),
                    (Name: "party", Tab: 7, Page: 2)
                };
                foreach (var state in states)
                {
                    AccessTools.Method(menu.GetType(), "SelectNativeTab").Invoke(menu, new object[] { state.Tab });
                    yield return new WaitForSecondsRealtime(.2f);
                    if (state.Name == "folders")
                    {
                        var folders = AccessTools.Field(menu.GetType(), "folders").GetValue(menu);
                        var browser = AccessTools.Field(folders.GetType(), "browser").GetValue(folders);
                        AccessTools.Method(browser.GetType(), "Browse").Invoke(browser, new object[] { publicMusic });
                    }
                    else if (state.Name == "playlists")
                    {
                        var panel = AccessTools.Field(menu.GetType(), "playlists").GetValue(menu);
                        AccessTools.Method(panel.GetType(), "Edit").Invoke(panel, new object[] { playlist });
                    }
                    else if (state.Name == "decorations")
                    {
                        var panel = AccessTools.Field(menu.GetType(), "trophyPanel").GetValue(menu);
                        var trophyCatalog = AccessTools.Field(panel.GetType(), "catalog").GetValue(panel);
                        AccessTools.Method(trophyCatalog.GetType(), "SelectGroupFor").Invoke(trophyCatalog, new object[] { 17 });
                        AccessTools.Method(panel.GetType(), "SelectSlot").Invoke(panel, new object[] { 17 });
                    }
                    if (state.Page >= 0)
                    {
                        var panel = AccessTools.Field(menu.GetType(), "bonusPanel").GetValue(menu);
                        AccessTools.Method(panel.GetType(), "Select").Invoke(panel, new object[] { state.Page });
                    }
                    yield return new WaitForSecondsRealtime(state.Name == "effects" ? .8f : .25f);
                    yield return ManualFrame(prefix + state.Name + ".png", frames, started, proof);
                }
                plugin.CloseMenu();
            }
            proof["captured"] = frames.Count == 20;
            proof["seconds"] = Time.realtimeSinceStartup - started;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            var menu = menuField.GetValue(plugin);
            if (menu != null)
                AccessTools.Method(menu.GetType(), "Dispose").Invoke(menu, null);
            Clear();
            audioDeck = null;
            player.GetInventory().RemoveItem(spare);
            plugin.MusicLibrary = library;
            plugin.Catalog = catalog;
            lookupField.SetValue(plugin, lookup);
            revision.SetValue(plugin, originalRevision);
            playerData.Set(ZDOVars.s_playerName, playerName);
            profile.SetName(profileName);
            Game.instance.GetPlayerProfile().SetName(profileName);
            if (chat != null)
                chat.SetActive(chatVisible);
            foreach (var element in chatElements)
                if (element.Key != null)
                    element.Key.SetActive(element.Value);
            knownMaterial.Clear();
            knownMaterial.UnionWith(originalMaterials);
            knownRecipes.Clear();
            knownRecipes.UnionWith(originalRecipes);
            body.constraints = constraints;
            Localization.instance.SetLanguage(language);
            foreach (var entry in config)
                entry.Key.BoxedValue = entry.Value;
            plugin.Config.SaveOnConfigSet = saveConfig;
            EnvMan.instance.m_debugTimeOfDay = debugTime;
            EnvMan.instance.m_debugTime = hour;
            UiParkWindow();
            proof["temporaryStateRestored"] = true;
        }
    }

    private static JObject ManualNativeNames()
    {
        var recipes = Enumerable.Range(1, 8).Select(tier => BoomboxProgression.GetTier(tier).Recipe)
            .Concat(new[] { BoomboxCassetteWorkshop.BaseRecipe })
            .Concat(Enumerable.Range(1, 7).Select(BoomboxCassetteProgression.DefaultRecipe)).ToArray();
        var ids = recipes.Where(recipe => recipe != null)
            .SelectMany(recipe => recipe.Ingredients.Select(ingredient => ingredient.Prefab)
                .Concat(new[] { recipe.StationPrefab }))
            .Concat(new[]
            {
                "Wood", "Resin", "LeatherScraps", "BoneFragments", "Coins", "Bronze", "FineWood", "Iron",
                "Silver", "Crystal", "BlackMetal", "LinenThread", "Eitr", "BlackMarble", "FlametalNew", "AskHide",
                "Gold", "Frostwood", "MushroomYellow", "TrophyEikthyr", "TrophyTheElder", "TrophyBonemass", "TrophyDragonQueen",
                "TrophyGoblinKing", "TrophySeekerQueen", "TrophyFader", "piece_workbench", "forge", "blackforge"
            }).Where(id => !string.IsNullOrEmpty(id)).Distinct().OrderBy(id => id).ToArray();
        var items = new JArray();
        var missing = new JArray();
        foreach (string id in ids)
        {
            var item = ObjectDB.instance.GetItemPrefab(id)?.GetComponent<ItemDrop>();
            var station = ZNetScene.instance.GetPrefab(id)?.GetComponent<CraftingStation>();
            string itemKey = item?.m_itemData?.m_shared?.m_name;
            string stationKey = station?.m_name;
            items.Add(new JObject
            {
                ["prefab"] = id,
                ["itemFound"] = item != null,
                ["stationFound"] = station != null,
                ["itemKey"] = itemKey,
                ["itemName"] = string.IsNullOrEmpty(itemKey) ? null : Localization.instance.Localize(itemKey),
                ["stationKey"] = stationKey,
                ["stationName"] = string.IsNullOrEmpty(stationKey) ? null : Localization.instance.Localize(stationKey)
            });
            if (string.IsNullOrEmpty(itemKey) && string.IsNullOrEmpty(stationKey))
                missing.Add(new JObject { ["prefab"] = id, ["reason"] = "No native item or station name key" });
            else if ((item != null && string.IsNullOrEmpty(itemKey)) || (station != null && string.IsNullOrEmpty(stationKey)))
                missing.Add(new JObject { ["prefab"] = id, ["reason"] = "Native component has a null or empty name key" });
        }
        return new JObject { ["items"] = items, ["missing"] = missing };
    }

    private static IEnumerator ManualFrame(string name, JArray frames, float started, JObject proof)
    {
        Need(Time.realtimeSinceStartup - started < 85f, "Documentation capture work budget exhausted");
        Need(Screen.width <= 1920 && Screen.height <= 1080, "Manual capture resolution exceeds its declared limit");
        var labels = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)
            .Where(text => text.gameObject.activeInHierarchy).Select(text => text.text ?? "").ToArray();
        var blocked = labels.Where(text => text.IndexOf("QA_CLIENT", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf(BoomboxQa.QaEnvironment.QaRoot, StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        if (blocked.Length != 0)
        {
            var warnings = proof["occlusionReview"] as JArray;
            if (warnings == null)
                proof["occlusionReview"] = warnings = new JArray();
            warnings.Add(new JObject { ["frame"] = name, ["activeLabels"] = new JArray(blocked) });
        }
        proof["visualPrivacyReviewRequired"] = true;
        yield return HudFrame(name, frames);
        var frame = (JObject)frames.Last;
        frame["width"] = Screen.width;
        frame["height"] = Screen.height;
        frame["visibleLabels"] = new JArray(labels);
    }
}
