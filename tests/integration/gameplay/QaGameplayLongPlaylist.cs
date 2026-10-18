using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static string LongPlaylistCheckpoint => Path.Combine(BepInEx.Paths.ConfigPath, "qa-L07-checkpoint.json");
    private IEnumerator CaseLongPlaylist(bool save)
    {
        var plugin = BoomboxPlugin.Instance;
        var library = plugin.MusicLibrary;
        var original = JArray.FromObject(library.playlists);
        var checkpoint = save
            ? new JObject { ["sha"] = productSha, ["original"] = original }
            : JObject.Parse(File.ReadAllText(LongPlaylistCheckpoint));
        var input = InputSystem.settings.backgroundBehavior;
        var frames = new JArray();
        var clicks = new JArray();
        uiClicks = clicks;
        bool success = false;
        var proof =
            new JObject
            {
                ["case"] = "L-07",
                ["phase"] = save ? "save" : "load",
                ["frames"] = frames,
                ["clicks"] = clicks,
                ["scope"] =
                    "Maximum supported 60-character Cyrillic name through native popup/rename controls; fresh-process persistence and actual menu captures. UiType records TMP fallback when used."
            };
        lastCapture = proof;
        try
        {
            Need((string)checkpoint["sha"] == productSha, "L07 candidate changed");
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            CaseDevice("L07", "Green", 1);
            Directory.CreateDirectory(Path.Combine(BepInEx.Paths.ConfigPath, "capture"));
            void Click(RectTransform button)
            {
                var click = UiDispatch(button, false);
                clicks.Add(click);
                Need((bool)click["hitsTarget"], "L07 pointer missed " + button.name);
            }
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.6f);
            Click(UiButton("BoomboxMusicButton"));
            yield return new WaitForSecondsRealtime(.3f);
            if (save)
            {
                string name = "QA — Музыка дальних путешествий, северные земли и закаты 2026".PadRight(60, '!')
                    .Substring(0, 60);
                Click(UiButton("Tab0"));
                yield return new WaitForSecondsRealtime(.2f);
                var add = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(
                    b => b.name == "TrackAdd"
                        && b.gameObject.activeInHierarchy
                        && UiRaycast((RectTransform)b.transform, false).gameObject?.transform.IsChildOf(b.transform)
                        == true
                );
                Click((RectTransform)add.transform);
                yield return null;
                Click(UiButton("PopupNewPlaylist"));
                yield return null;
                yield return UiType("PopupName", name);
                Click(UiButton("PopupCreate"));
                yield return new WaitForSecondsRealtime(.2f);
                var prior = original.Select(p => (string)p["id"]).ToArray();
                var created = library.playlists.Single(p => !prior.Contains(p.id));
                Need(created.name == name && created.tracks.Count > 0, "L07 creation truncated name or lost track");
                checkpoint["id"] = created.id;
            }
            var playlist = library.playlists.Single(p => p.id == (string)checkpoint["id"]);
            if (!save)
                Need(
                    JToken.DeepEquals(checkpoint["playlist"], JObject.FromObject(playlist)),
                    "L07 playlist changed across process reload"
                );
            Click(UiButton("Tab1"));
            yield return new WaitForSecondsRealtime(.2f);
            Click(UiButton("PlaylistEntry" + library.playlists.IndexOf(playlist)));
            yield return null;
            if (save)
            {
                string renamed = "QA — Зимний вечер, далёкие берега и песни северного пути 2026".PadRight(60, '!')
                    .Substring(0, 60);
                yield return UiType("PlaylistName", renamed);
                Click(UiButton("PlaylistRename"));
                yield return new WaitForSecondsRealtime(.2f);
                Need(playlist.name == renamed, "L07 rename truncated Cyrillic name");
            }
            var field = UnityEngine.Object.FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None).Single(
                f => f.name == "PlaylistName" && f.gameObject.activeInHierarchy
            );
            Need(
                field.characterLimit == 60 && field.text == playlist.name && playlist.name.Length == 60,
                "L07 displayed edit value differs from stored name"
            );
            field.ActivateInputField();
            field.caretPosition = 0;
            yield return new WaitForSecondsRealtime(.2f);
            yield return UiFrame("L07-" + (save ? "save" : "load") + "-start.png", frames);
            field.caretPosition = field.text.Length;
            yield return new WaitForSecondsRealtime(.2f);
            yield return UiFrame("L07-" + (save ? "save" : "load") + "-end.png", frames);
            field.DeactivateInputField();
            Click(UiButton("Tab5"));
            yield return new WaitForSecondsRealtime(.4f);
            yield return UiFrame("L07-" + (save ? "save" : "load") + "-effects.png", frames);
            checkpoint["playlist"] = JObject.FromObject(playlist);
            if (save)
                File.WriteAllText(LongPlaylistCheckpoint, checkpoint.ToString());
            proof["playlist"] = JObject.FromObject(playlist);
            proof["actionsPassed"] = true;
            proof["visualReviewRequired"] = true;
            success = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(Player.m_localPlayer, false);
            InputSystem.settings.backgroundBehavior = input;
            if (!save || !success)
            {
                var prior = checkpoint["original"].Select(p => (string)p["id"]).ToArray();
                foreach (var list in library.playlists.Where(p => !prior.Contains(p.id)).ToArray())
                    library.Delete(list);
                Need(
                    JToken.DeepEquals(checkpoint["original"], JArray.FromObject(library.playlists)),
                    "L07 original library changed"
                );
            }
        }
    }
}
