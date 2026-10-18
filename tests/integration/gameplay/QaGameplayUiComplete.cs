using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator UiComplete(JObject request)
    {
        bool background = Application.runInBackground;
        var inputBehavior = InputSystem.settings.backgroundBehavior;
        string language = Localization.instance.GetSelectedLanguage();
        int width = Screen.width, height = Screen.height;
        var screenMode = Screen.fullScreenMode;
        try
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            UiParkWindow();
            uiClicks = new JArray();
            var plugin = BoomboxPlugin.Instance;
            var player = Player.m_localPlayer;
            var device = player.GetInventory().GetAllItems().Single(
                i => BoomboxItemData.IsDevice(i)
                    && BoomboxItemData.Read(i).deck.id == UiIdentity("device")
            );
            var proof =
                new JObject
                {
                    ["scope"] =
                    "Remaining R10 UI04/05/07/08 and UI06 clear/replace/save; no accepted craft/folder/23opens repeat",
                    ["device"] = BoomboxItemData.Read(device).deck.id,
                    ["persistedBefore"] = JObject.FromObject(BoomboxItemData.Read(device).deck),
                    ["plugins"] = new JArray(BepInEx.Bootstrap.Chainloader.PluginInfos.Select(p => p.Key)),
                    ["extraSlot"] = BoomboxEquipment.SlotPosition?.ToString()
                };
            var steps = new JArray();
            var frames = new JArray();
            proof["steps"] = steps;
            proof["frames"] = frames;
            sample = proof;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("shudnal.ExtraSlots")
                || !BoomboxEquipment.SlotPosition.HasValue
                || BoomboxItemData.Read(device).deck.unlockedDecorationSlots.Count != 24)
                throw new InvalidOperationException("ExtraSlots loaded/24 saved slots precondition failed");
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.3f);
            var benchOrigin = player.transform.position + Vector3.up * 35;
            var bench = UnityEngine.Object.Instantiate(
                ZNetScene.instance.GetPrefab("piece_workbench"),
                benchOrigin,
                Quaternion.identity
            );
            fixtures.Add(bench);
            var roof = bench.GetComponent<CraftingStation>().m_roofCheckPoint.position;
            var positions = new[] {
                roof + Vector3.up,
                roof + Vector3.right * 3,
                roof + Vector3.left * 3,
                roof + Vector3.forward * 3,
                roof + Vector3.back * 3,
                benchOrigin - Vector3.up * .2f
            };
            var sizes = new[] {
                new Vector3(6, .25f, 6),
                new Vector3(.25f, 4, 6),
                new Vector3(.25f, 4, 6),
                new Vector3(6, 4, .25f),
                new Vector3(6, 4, .25f),
                new Vector3(10, .4f, 10)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
                surface.layer = LayerMask.NameToLayer("piece");
                surface.transform.position = positions[i];
                surface.transform.localScale = sizes[i];
                surface.GetComponent<MeshRenderer>().enabled = false;
                fixtures.Add(surface);
            }
            player.transform.position = benchOrigin + Vector3.forward * 1.5f;
            Freeze(player, true);
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(.3f);
            if (BoomboxEquipment.Get(player) == device)
            {
                UiDispatch(UiInventoryItem(device), true);
                yield return UiAwait(() => BoomboxEquipment.Get(player) != device, "ExtraSlots unequip");
            }
            UiDispatch(UiInventoryItem(device), true);
            yield return UiAwait(() => BoomboxEquipment.Get(player) == device, "ExtraSlots equip");
            steps.Add(new JObject
            {
                ["id"] = "extraslot-equip",
                ["grid"] = device.m_gridPos.ToString(),
                ["slot"] = BoomboxEquipment.SlotPosition?.ToString(),
                ["equipped"] = BoomboxEquipment.Get(player) == device
            });
            var cassette = player.GetInventory().GetAllItems().Single(
                i => BoomboxItemData.IsCassette(i)
                    && BoomboxItemData.Read(i).cassette.id == UiIdentity("cassette")
            );
            UiDispatch(UiInventoryItem(cassette), true);
            yield return UiAwait(
                () => BoomboxItemData.Read(device).deck.insertedCassette?.id == UiIdentity("cassette"),
                "ExtraSlots cassette insert"
            );
            steps.Add(UiDispatch(UiButton("BoomboxMusicButton"), false));
            yield return new WaitForSecondsRealtime(.2f);
            var deck = plugin.Current;
            UiDispatch(UiButton("Tab4"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("Group0"), false);
            yield return new WaitForSecondsRealtime(.16f);
            UiDispatch(UiElement("SlotChip0"), false);
            yield return new WaitForSecondsRealtime(.16f);
            steps.Add(UiDispatch(UiButton("ClearSlot"), false));
            yield return UiAwait(() => !deck.State.decorationPlacements.Any(p => p.slot == 17), "clear saved design");
            UiRawItem("Wood", 2);
            UiRawItem("Resin", 1);
            UiRawItem("MushroomYellow", 1);
            UiDispatch(UiButton("Group2"), false);
            yield return new WaitForSecondsRealtime(.16f);
            UiDispatch(UiElement("SlotChip0"), false);
            yield return new WaitForSecondsRealtime(.16f);
            UiDispatch(UiElement("Design_MushroomYellow"), false);
            yield return new WaitForSecondsRealtime(.16f);
            UiDispatch(UiButton("DecorationAction"), false);
            yield return UiAwait(
                () => deck.State.unlockedDecorationPrefabs.Contains("MushroomYellow"),
                "unlock hanging design"
            );
            UiDispatch(UiButton("DecorationAction"), false);
            yield return UiAwait(
                () => deck.State.decorationPlacements.Any(p => p.slot == 0 && p.prefab == "MushroomYellow"),
                "install hanging design"
            );
            UiDispatch(UiElement("Design_TrophySkeleton"), false);
            yield return new WaitForSecondsRealtime(.16f);
            UiDispatch(UiButton("DecorationAction"), false);
            yield return UiAwait(
                () => deck.State.decorationPlacements.Any(p => p.slot == 0 && p.prefab == "TrophySkeleton"),
                "replace hanging design"
            );
            steps.Add(new JObject
            {
                ["id"] = "ui06-tail",
                ["cleared17"] = !deck.State.decorationPlacements.Any(p => p.slot == 17),
                ["replacement0"] = JObject.FromObject(deck.State.decorationPlacements.Single(p => p.slot == 0)),
                ["all24"] = new JArray(deck.State.unlockedDecorationSlots.OrderBy(s => s))
            });
            foreach (int tab in new[] { 0, 1, 2, 3, 7, 4, 5 })
            {
                UiDispatch(UiButton("Tab" + tab), false);
                yield return new WaitForSecondsRealtime(.2f);
                var window = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Single(
                    t => t.name == "BoomboxMenuWindow" && t.gameObject.activeInHierarchy
                );
                steps.Add(new JObject
                {
                    ["id"] = "tab-frame",
                    ["tab"] = tab,
                    ["position"] = Point(window.position),
                    ["scale"] = Point(window.localScale),
                    ["size"] = Point(window.rect.size)
                });
            }
            UiDispatch(UiButton("Tab1"), false);
            yield return new WaitForSecondsRealtime(.2f);
            yield return UiType("PlaylistNewName", "R10 playlist");
            UiDispatch(UiButton("PlaylistCreate"), false);
            yield return new WaitForSecondsRealtime(.2f);
            var playlist = plugin.MusicLibrary.playlists.Single(p => p.name == "R10 playlist");
            steps.Add(new JObject
            {
                ["id"] = "playlist-created-empty",
                ["playlist"] = playlist.id,
                ["tracks"] = playlist.tracks.Count,
                ["hint"] = UiText("PlaylistEmpty")
            });
            yield return UiType("PlaylistName", "R10 renamed");
            UiDispatch(UiButton("PlaylistRename"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("Tab0"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("LibrarySelectResults"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("Tab1"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("PlaylistAddSelected"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("Tab0"), false);
            yield return new WaitForSecondsRealtime(.2f);
            var plus = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(
                b => b.name == "TrackAdd" && b.gameObject.activeInHierarchy
            );
            steps.Add(UiDispatch((RectTransform)plus.transform, false));
            yield return new WaitForSecondsRealtime(.2f);
            var popupEntry = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(
                b => b.name.StartsWith("PopupEntry", StringComparison.Ordinal)
                    && b.gameObject.activeInHierarchy
                    && b.GetComponentInChildren<TMP_Text>().text.StartsWith(
                    playlist.name + " ·",
                    StringComparison.Ordinal
                )
            );
            int beforePlus = playlist.tracks.Count;
            steps.Add(UiDispatch((RectTransform)popupEntry.transform, false));
            yield return new WaitForSecondsRealtime(.2f);
            steps.Add(
                new JObject
                {
                    ["id"] = "library-plus-popup-append",
                    ["before"] = beforePlus,
                    ["after"] = playlist.tracks.Count
                }
            );
            UiDispatch(UiButton("Tab1"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("PlaylistPlay"), false);
            yield return UiAwait(
                () => deck.State.playlistId == playlist.id
                    && deck.State.playing,
                "play edited playlist"
            );
            var before = playlist.tracks.ToArray();
            string playing = deck.State.TrackId;
            bool wasPlaying = deck.State.playing;
            var row0 = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Single(
                t => t.name == "PlaylistTrack0" && t.gameObject.activeInHierarchy
            );
            var row1 = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Single(
                t => t.name == "PlaylistTrack1" && t.gameObject.activeInHierarchy
            );
            var canvas = row0.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var drag = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(camera, row0.TransformPoint(row0.rect.center)),
                dragging = true
            };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(drag, hits);
            if (!hits.Any(h => h.gameObject.transform.IsChildOf(row0)))
                throw new InvalidOperationException("Playlist drag raycast missed source row");
            ExecuteEvents.ExecuteHierarchy(
                hits.First(h => h.gameObject.transform.IsChildOf(row0)).gameObject,
                drag,
                ExecuteEvents.beginDragHandler
            );
            drag.position = RectTransformUtility.WorldToScreenPoint(
                camera,
                row1.TransformPoint(new Vector3(row1.rect.center.x, row1.rect.yMin - 5, 0))
            );
            ExecuteEvents.Execute(row0.gameObject, drag, ExecuteEvents.dragHandler);
            yield return null;
            ExecuteEvents.Execute(row0.gameObject, drag, ExecuteEvents.endDragHandler);
            yield return new WaitForSecondsRealtime(.2f);
            steps.Add(new JObject
            {
                ["id"] = "playlist-actual-drag",
                ["before"] = new JArray(before),
                ["after"] = new JArray(playlist.tracks),
                ["sameTrack"] = deck.State.TrackId == playing,
                ["samePlaying"] = deck.State.playing == wasPlaying,
                ["marker"] = UiText("PlaylistState")
            });
            if (playlist.tracks.Count < 2 || playlist.tracks[1] != before[0])
                throw new InvalidOperationException("Native playlist drag did not move row0 down one");
            var remove = row1.GetComponentsInChildren<Button>().Single(b => b.name == "PlaylistTrackRemove");
            UiDispatch((RectTransform)remove.transform, false);
            yield return new WaitForSecondsRealtime(.2f);
            steps.Add(new JObject
            {
                ["id"] = "playlist-remove",
                ["name"] = playlist.name,
                ["tracks"] = new JArray(playlist.tracks),
                ["deckTracks"] = new JArray(deck.State.tracks),
                ["playing"] = deck.State.playing
            });
            Localization.instance.SetLanguage("English");
            yield return new WaitForSecondsRealtime(.2f);
            yield return UiFrame("ui-complete-playlist-en.png", frames);
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.4f);
            yield return UiFrame("ui-complete-scale720.png", frames);
            UiDispatch(UiButton("Tab0"), false);
            yield return new WaitForSecondsRealtime(.2f);
            yield return UiType("LibrarySearch", "R10");
            steps.Add(
                new JObject
                {
                    ["id"] = "native-search-input",
                    ["text"] = UiInput("LibrarySearch").text,
                    ["focused"] = UiInput("LibrarySearch").isFocused,
                    ["rows"] = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Count(
                    b => b.name == "TrackPlay" && b.gameObject.activeInHierarchy
                )
                }
            );
            bool menuBeforeKey = plugin.MenuOpen, playingBeforeKey = deck.State.playing;
            bool searchFocus = UiInput("LibrarySearch").isFocused;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.F7));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.F4));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(.15f);
            steps.Add(
                new JObject
                {
                    ["id"] = "search-hotkey-guard",
                    ["applicationFocused"] = Application.isFocused,
                    ["inputFocused"] = searchFocus,
                    ["menuSame"] = plugin.MenuOpen == menuBeforeKey,
                    ["playingSame"] = deck.State.playing == playingBeforeKey,
                    ["scope"] =
                        "Only claim native focused keyboard guard when Application.isFocused and input.isFocused; otherwise runtime blocked-focus boundary plus inspected product guard"
                }
            );
            Need(Application.isFocused && searchFocus, "Focused keyboard guard was not exercised");
            Need(
                plugin.MenuOpen == menuBeforeKey && deck.State.playing == playingBeforeKey,
                "Hotkey escaped focused text input"
            );
            UiDispatch(UiButton("Tab3"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("CassetteEject"), false);
            yield return UiAwait(() => deck.State.insertedCassette == null, "ExtraSlots eject");
            steps.Add(
                new JObject
                {
                    ["id"] = "extraslot-eject",
                    ["sameCassetteInventory"] = player.GetInventory().GetAllItems().Any(
                    i => BoomboxItemData.IsCassette(i)
                            && BoomboxItemData.Read(i).cassette.id == UiIdentity("cassette")
                ),
                    ["playing"] = deck.State.playing
                }
            );
            UiDispatch(UiButton("Close"), false);
            yield return new WaitForSecondsRealtime(.2f);
            plugin.Save();
            Game.instance.SavePlayerProfile(false, false);
            proof["after"] = JObject.FromObject(deck.State);
            proof["playlist"] = JObject.FromObject(playlist);
            proof["utc"] = DateTime.UtcNow.ToString("O");
        }
        finally
        {
            Application.runInBackground = background;
            InputSystem.settings.backgroundBehavior = inputBehavior;
            Localization.instance.SetLanguage(language);
            Screen.SetResolution(width, height, screenMode);
            BoomboxPlugin.Instance.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(Player.m_localPlayer, false);
            UiParkWindow();
        }
    }
    private static TMP_InputField UiInput(
        string name
    ) => UnityEngine.Object.FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None).Single(
        t => t.name == name && t.gameObject.activeInHierarchy
    );
    private static string UiText(
        string name
    ) => UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Single(
        t => t.name == name && t.gameObject.activeInHierarchy
    ).text;
    private static IEnumerator UiType(string name, string value)
    {
        var input = UiInput(name);
        UiDispatch((RectTransform)input.transform, false);
        yield return new WaitForSecondsRealtime(.15f);
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftCtrl, Key.A));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        yield return null;
        foreach (char ch in value)
            InputSystem.QueueTextEvent(Keyboard.current, ch);
        yield return null;
        yield return null;
        yield return new WaitForSecondsRealtime(.15f);
        if (input.text != value)
        {
            input.text = value;
            uiClicks.Add(new JObject
            {
                ["route"] = "TMP_InputField.text UI-control setter/onValueChanged; no hardware typing claim",
                ["field"] = name,
                ["value"] = value
            });
            yield return new WaitForSecondsRealtime(.15f);
        }
    }
}
