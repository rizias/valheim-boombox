using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CasePlaylistMetadata()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var library = plugin.MusicLibrary;
        var original = JArray.FromObject(library.playlists);
        var originalIds = library.playlists.Select(p => p.id).ToArray();
        var input = InputSystem.settings.backgroundBehavior;
        float start = Time.realtimeSinceStartup;
        uiClicks = new JArray();
        var proof =
            new JObject
            {
                ["case"] = "E-08",
                ["scope"] =
                    "Create two playlists containing the same library track through the + popup, then rename one. Copy means duplicate music references, not an unavailable Copy-playlist button.",
                ["steps"] = uiClicks
            };
        lastCapture = proof;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            var device = CaseDevice("E08", "Green", 4);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.6f);
            float ready = Time.realtimeSinceStartup + 2;
            while (UiRaycast(UiInventoryItem(device), true).gameObject == null && Time.realtimeSinceStartup < ready)
                yield return null;
            var equip = UiDispatch(UiInventoryItem(device), true);
            Need((bool)equip["hitsTarget"], "E08 equip missed");
            yield return UiAwait(() => BoomboxEquipment.Get(player) == device, "E08 equip");
            var before = CaseStable(device);
            int count = player.GetInventory().GetAllItems().Count;
            void Click(RectTransform rect)
            {
                var route = UiDispatch(rect, false);
                uiClicks.Add(route);
                Need((bool)route["hitsTarget"], "E08 native raycast missed " + rect.name);
            }
            Click(UiButton("BoomboxMusicButton"));
            yield return new WaitForSecondsRealtime(.3f);
            Click(UiButton("Tab0"));
            yield return new WaitForSecondsRealtime(.3f);
            foreach (string name in new[] { "QA E08 source", "QA E08 copy" })
            {
                var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(
                    b => b.name == "TrackAdd"
                        && b.gameObject.activeInHierarchy
                        && UiRaycast((RectTransform)b.transform, false).gameObject?.transform.IsChildOf(b.transform)
                        == true
                );
                Need(button != null, "E08 no visible library track add action");
                Click((RectTransform)button.transform);
                yield return new WaitForSecondsRealtime(.15f);
                Click(UiButton("PopupNewPlaylist"));
                yield return null;
                yield return UiType("PopupName", name);
                Click(UiButton("PopupCreate"));
                yield return new WaitForSecondsRealtime(.2f);
            }
            var added = library.playlists.Where(p => !originalIds.Contains(p.id)).ToArray();
            Need(
                added.Length == 2 && added[0].tracks.Count > 0 && added[0].tracks.SequenceEqual(added[1].tracks),
                "E08 did not duplicate the music reference into two lists"
            );
            Click(UiButton("Tab1"));
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiButton("PlaylistEntry" + library.playlists.IndexOf(added[1])));
            yield return null;
            yield return UiType("PlaylistName", "QA E08 renamed");
            Click(UiButton("PlaylistRename"));
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                added[1].name == "QA E08 renamed" && added[0].tracks.SequenceEqual(added[1].tracks),
                "E08 rename changed music contents"
            );
            Need(
                count == player.GetInventory().GetAllItems().Count && JToken.DeepEquals(before, CaseStable(device)),
                "E08 metadata created physical items, changed rank or playback queue"
            );
            proof["playlists"] = JArray.FromObject(added);
            proof["before"] = before;
            proof["after"] = CaseStable(device);
            proof["itemCount"] = count;
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            foreach (var list in library.playlists.Where(p => !originalIds.Contains(p.id)).ToArray())
                library.Delete(list);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
            InputSystem.settings.backgroundBehavior = input;
            Need(
                JToken.DeepEquals(original, JArray.FromObject(library.playlists)),
                "E08 original library was not restored"
            );
        }
    }
}
