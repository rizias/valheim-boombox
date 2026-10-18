using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator PlaylistValidation()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var proof =
            new JObject
            {
                ["case"] = "D-09",
                ["scope"] =
                    "Actual playlist UI: blank names refused, named empty playlist allowed but playback disabled. Fixture device/cassette; UI input events with explicitly recorded TMP fallback."
            };
        var beforeLists = JArray.FromObject(plugin.MusicLibrary.playlists);
        var originalIds = plugin.MusicLibrary.playlists.Select(p => p.id).ToArray();
        var behavior = InputSystem.settings.backgroundBehavior;
        string language = Localization.instance.GetSelectedLanguage();
        UserPlaylist created = null;
        float started = Time.realtimeSinceStartup;
        lastCapture = proof;
        uiClicks = new JArray();
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            UiParkWindow();
            Localization.instance.SetLanguage("Russian");
            Prepare(player);
            Freeze(player, true);
            var item = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
            var data = BoomboxItemData.Read(item);
            var tape = BoomboxItemData.Read(BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab)).cassette;
            tape.properties["Red"] = 2;
            data.deck.owner = player.GetPlayerID();
            data.deck.insertedCassette = tape;
            data.deck.cassetteId = tape.id;
            data.deck.playing = false;
            item.m_customData["boombox.qa.fixture"] = "D09";
            BoomboxItemData.Write(item, data);
            Need(player.GetInventory().AddItem(item), "D09 fixture inventory full");
            player.AddKnownItem(item);
            plugin.Items.Update();
            var deck = ((Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
                .GetValue(plugin))[data.deck.id];
            decks["D09"] = deck;
            var cassette = JObject.FromObject(deck.State.insertedCassette);
            Need(BoomboxEquipment.Known, "D09 fixture is not a known material");
            yield return null;
            if (CinematicsManager.IsPlaying())
                CinematicsManager.Stop();
            if (Valkyrie.m_instance != null)
                Valkyrie.m_instance.DropPlayer(true);
            player.SetIntro(false);
            player.SetSleeping(false);
            var animator = player.GetComponentInChildren<Animator>();
            for (int step = 0; step < 8 && player.InCutscene(); step++)
            {
                animator.Update(3f);
                yield return null;
            }
            Need(!player.InCutscene(), "D09 fixture still in spawn animation");
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.3f);
            float buttonDeadline = Time.realtimeSinceStartup + 1;
            while (!UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Any(
                b => b.name == "BoomboxMusicButton" && b.gameObject.activeInHierarchy
            )
            && Time.realtimeSinceStartup < buttonDeadline)
                yield return null;
            proof["preconditions"] = new JObject
            {
                ["known"] = BoomboxEquipment.Known,
                ["inventory"] = InventoryGui.IsVisible(),
                ["cutscene"] = player.InCutscene(),
                ["menuOpen"] = plugin.MenuOpen
            };
            void Click(string name)
            {
                proof["phase"] = "click:" + name;
                var route = UiDispatch(UiButton(name), false);
                Need((bool)route["hitsTarget"], "D09 raycast missed " + name);
            }
            Click("BoomboxMusicButton");
            yield return new WaitForSecondsRealtime(.2f);
            Click("Tab1");
            yield return new WaitForSecondsRealtime(.2f);
            foreach (string blank in new[] { "", "   " })
            {
                yield return UiType("PlaylistNewName", blank);
                Need(!UiButton("PlaylistCreate").GetComponent<Button>().interactable, "Blank name enabled Create");
                Click("PlaylistCreate");
                yield return null;
                Need(
                    JToken.DeepEquals(beforeLists, JArray.FromObject(plugin.MusicLibrary.playlists)),
                    "Blank Create modified library"
                );
            }
            yield return UiType("PlaylistNewName", "QA D09 empty");
            Click("PlaylistCreate");
            yield return new WaitForSecondsRealtime(.2f);
            created = plugin.MusicLibrary.playlists.Single(p => !originalIds.Contains(p.id));
            Need(
                created.name == "QA D09 empty" && created.tracks.Count == 0,
                "Named empty playlist was not created correctly"
            );
            Need(!UiButton("PlaylistPlay").GetComponent<Button>().interactable, "Empty playlist enabled playback");
            Click("PlaylistPlay");
            yield return null;
            Need(!deck.State.playing, "Empty playlist started playback");
            yield return UiType("PlaylistName", "   ");
            Need(!UiButton("PlaylistRename").GetComponent<Button>().interactable, "Blank rename enabled");
            Click("PlaylistRename");
            yield return null;
            Need(created.name == "QA D09 empty", "Blank rename changed name");
            Need(
                JToken.DeepEquals(cassette, JObject.FromObject(deck.State.insertedCassette)),
                "Playlist actions changed cassette payload"
            );
            Need(
                originalIds.All(id => plugin.MusicLibrary.playlists.Any(p => p.id == id)),
                "Existing playlist disappeared"
            );
            proof["blankCreateRejected"] = true;
            proof["emptyPlaylistPlaybackRejected"] = true;
            proof["blankRenameRejected"] = true;
            proof["cassetteUnchanged"] = true;
            proof["seconds"] = Time.realtimeSinceStartup - started;
            proof["inputRoutes"] = uiClicks;
            proof["passed"] = true;
        }
        finally
        {
            if (created != null)
                plugin.MusicLibrary.Delete(created);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
            Localization.instance.SetLanguage(language);
            InputSystem.settings.backgroundBehavior = behavior;
            Need(
                JToken.DeepEquals(beforeLists, JArray.FromObject(plugin.MusicLibrary.playlists)),
                "D09 library restoration differs"
            );
        }
    }
}
