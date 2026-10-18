using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator UiRoute(ItemDrop.ItemData device, string musicFolder, JObject proof, bool foldersOnly = false)
    {
        musicFolder = Path.GetFullPath(musicFolder);
        string ownedRoot = Path.GetFullPath(Path.Combine(BepInEx.Paths.ConfigPath, "../.."))
            + Path.DirectorySeparatorChar;
        if (!musicFolder.StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UI music fixture outside owned profile");
        if (device == null)
            throw new InvalidOperationException("UI continuation requires real UI-crafted device");
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var steps = new JArray();
        proof["routeTail"] = steps;
        string cassetteId;
        float until;
        if (!foldersOnly)
        {
            steps.Add(UiDispatch(UiInventoryItem(device), true));
            yield return UiAwait(() => BoomboxEquipment.Get(player) == device, "empty device equip");
            steps.Add(new JObject
            {
                ["id"] = "empty-device-rmb",
                ["equipped"] = BoomboxEquipment.Get(player) == device,
                ["grid"] = device.m_gridPos.ToString(),
                ["tutorial"] = UiTutorial("equipment")
            });
            steps.Add(UiDispatch(UiInventoryItem(device), true));
            yield return UiAwait(() => BoomboxEquipment.Get(player) != device, "empty device unequip");
            var cassetteRecipe = ObjectDB.instance.m_recipes.Single(r => r != null
                && r.name == "Recipe_ValheimBoombox_CassetteArchive");
            var row = InventoryGui.instance.m_recipeListRoot.GetComponentsInChildren<Image>(false).Where(
                i => i.sprite != null && cassetteRecipe.m_item.m_itemData.m_shared.m_icons.Contains(i.sprite)
            ).Select(
                i => i.GetComponentInParent<Button>()
            ).First(
                b => b != null && b.gameObject.activeInHierarchy
            );
            steps.Add(UiDispatch((RectTransform)row.transform, false));
            yield return new WaitForSecondsRealtime(.2f);
            var tapesBefore = player.GetInventory().GetAllItems().Where(BoomboxItemData.IsCassette).ToArray();
            steps.Add(UiDispatch((RectTransform)InventoryGui.instance.m_craftButton.transform, false));
            until = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < until
            && !player.GetInventory().GetAllItems().Any(i => BoomboxItemData.IsCassette(i) && !tapesBefore.Contains(i)))
                yield return null;
            var tape = player.GetInventory().GetAllItems().FirstOrDefault(
                i => BoomboxItemData.IsCassette(i) && !tapesBefore.Contains(i)
            );
            if (tape == null)
                throw new InvalidOperationException("Native cassette craft UI did not create item");
            cassetteId = BoomboxItemData.Read(tape).cassette.id;
            RememberUiIdentity("cassette", cassetteId);
            steps.Add(UiDispatch(UiInventoryItem(tape), true));
            yield return UiAwait(
                () => BoomboxItemData.Read(device).deck.insertedCassette?.id == cassetteId,
                "cassette insert"
            );
            steps.Add(new JObject
            {
                ["id"] = "cassette-rmb-insert",
                ["cassette"] = cassetteId,
                ["inserted"] = BoomboxItemData.Read(device).deck.insertedCassette?.id,
                ["remainingInventoryTape"] = player.GetInventory().ContainsItem(tape)
            });
            steps.Add(UiDispatch(UiInventoryItem(device), true));
            yield return UiAwait(() => BoomboxEquipment.Get(player) == device, "device equip with cassette");
            steps.Add(new JObject
            {
                ["id"] = "device-rmb-equipped-with-cassette",
                ["equipped"] = BoomboxEquipment.Get(player) == device,
                ["device"] = BoomboxItemData.Read(device).deck.id,
                ["state"] = JObject.FromObject(BoomboxItemData.Read(device).deck)
            });
        }
        else
        {
            cassetteId = BoomboxItemData.Read(device).deck.insertedCassette?.id;
            if (cassetteId != UiIdentity("cassette"))
                throw new InvalidOperationException("Accepted UI cassette did not persist across reload");
            if (BoomboxEquipment.Get(player) != device)
            {
                steps.Add(UiDispatch(UiInventoryItem(device), true));
                yield return UiAwait(() => BoomboxEquipment.Get(player) == device, "saved device equip");
            }
            steps.Add(new JObject
            {
                ["id"] = "carry-accepted-ui02",
                ["device"] = BoomboxItemData.Read(device).deck.id,
                ["cassette"] = cassetteId,
                ["equipped"] = BoomboxEquipment.Get(player) == device
            });
        }
        steps.Add(UiDispatch(UiButton("BoomboxMusicButton"), false));
        yield return new WaitForSecondsRealtime(.3f);
        if (!plugin.MenuOpen)
            throw new InvalidOperationException("Native inventory Music button did not open menu");
        steps.Add(UiDispatch(UiButton("Tab2"), false));
        yield return new WaitForSecondsRealtime(.2f);
        string targetRoot = Path.GetPathRoot(musicFolder);
        string accumulated = targetRoot;
        foreach (string segment in new[] { targetRoot }.Concat(
            musicFolder.Substring(targetRoot.Length).Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries
            )
        ))
        {
            string label = segment;
            var entries = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(
                b => b.gameObject.activeInHierarchy && b.name.StartsWith("BrowseEntry", StringComparison.Ordinal)
            ).OrderBy(
                b => int.Parse(b.name.Substring(11))
            ).ToArray();
            var entry = entries.FirstOrDefault(b => segment == targetRoot
                ? b.GetComponentInChildren<TMP_Text>().text.StartsWith(label, StringComparison.OrdinalIgnoreCase)
                : b.GetComponentInChildren<TMP_Text>().text == label);
            if (entry == null)
                throw new InvalidOperationException("Native folder entry absent: " + label);
            var scroll = entry.GetComponentInParent<ScrollRect>();
            int index = Array.IndexOf(entries, entry);
            scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(index / (float)Math.Max(1, entries.Length - 6));
            Canvas.ForceUpdateCanvases();
            yield return null;
            steps.Add(UiDispatch((RectTransform)entry.transform, false));
            yield return new WaitForSecondsRealtime(.15f);
            if (segment != targetRoot)
                accumulated = Path.Combine(accumulated, segment);
            var pathText = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).First(
                t => t.name == "BrowserPath" && t.gameObject.activeInHierarchy
            ).text;
            if (!string.Equals(pathText, accumulated, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Native folder selection did not reach " + accumulated + "; actual " + pathText
                );
        }
        plugin.FFmpegPath.Value = BoomboxQa.QaEnvironment.Encoder;
        plugin.FFprobePath.Value = BoomboxQa.QaEnvironment.MediaProbe;
        int revision = plugin.CatalogRevision;
        steps.Add(UiDispatch(UiButton("BrowserAdd"), false));
        until = Time.realtimeSinceStartup + 15;
        while (Time.realtimeSinceStartup < until && (plugin.CatalogRefreshing || plugin.CatalogRevision <= revision))
            yield return null;
        string wav = Path.Combine(musicFolder, "R10 local music.wav");
        var track = plugin.Catalog.playlists.SelectMany(p => p.tracks).FirstOrDefault(
            t => string.Equals(t.path, wav, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.source, "file://" + wav, StringComparison.OrdinalIgnoreCase)
        );
        steps.Add(new JObject
        {
            ["id"] = "folder-browser-add-autoscan",
            ["folder"] = musicFolder,
            ["stored"] = plugin.MusicLibrary.folders.Contains(musicFolder, StringComparer.OrdinalIgnoreCase),
            ["revisionBefore"] = revision,
            ["revisionAfter"] = plugin.CatalogRevision,
            ["refreshing"] = plugin.CatalogRefreshing,
            ["track"] = track == null ? null : JObject.FromObject(track)
        });
        if (track == null)
            throw new InvalidOperationException("Native BrowserAdd auto scan did not discover owned local WAV");
        int folderCount = plugin.MusicLibrary.folders.Count;
        steps.Add(UiDispatch(UiButton("BrowserAdd"), false));
        yield return new WaitForSecondsRealtime(.2f);
        steps.Add(
            new JObject
            {
                ["id"] = "folder-duplicate-add",
                ["countBefore"] = folderCount,
                ["countAfter"] = plugin.MusicLibrary.folders.Count,
                ["matches"] = plugin.MusicLibrary.folders.Count(
                f => string.Equals(f, musicFolder, StringComparison.OrdinalIgnoreCase)
            ),
                ["revision"] = plugin.CatalogRevision
            }
        );
        Need(
            plugin.MusicLibrary.folders.Count == folderCount
                && plugin.MusicLibrary.folders.Count(
                f => string.Equals(f, musicFolder, StringComparison.OrdinalIgnoreCase)
            )
                == 1,
            "Duplicate folder was added"
        );
        yield return UiFrame("ui-02-folders-ru.png", steps);
        Localization.instance.SetLanguage("English");
        yield return new WaitForSecondsRealtime(.2f);
        yield return UiFrame("ui-02-folders-en.png", steps);
        Localization.instance.SetLanguage("Russian");
        var beforeCancel = plugin.MusicLibrary.folders.ToArray();
        steps.Add(UiDispatch(UiButton("BrowserUp"), false));
        yield return new WaitForSecondsRealtime(.15f);
        steps.Add(UiDispatch(UiButton("Close"), false));
        yield return new WaitForSecondsRealtime(.2f);
        steps.Add(new JObject
        {
            ["id"] = "folder-navigate-close-cancel",
            ["unchanged"] = beforeCancel.SequenceEqual(plugin.MusicLibrary.folders),
            ["countBefore"] = beforeCancel.Length,
            ["countAfter"] = plugin.MusicLibrary.folders.Count
        });
        Need(
            beforeCancel.SequenceEqual(plugin.MusicLibrary.folders),
            "Closing folder browser changed selected folders"
        );
        steps.Add(UiDispatch(UiButton("BoomboxMusicButton"), false));
        yield return new WaitForSecondsRealtime(.2f);
        steps.Add(UiDispatch(UiButton("Tab0"), false));
        yield return new WaitForSecondsRealtime(.2f);
        var play = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(
            b => b.name == "TrackPlay" && b.gameObject.activeInHierarchy
                && b.GetComponentInChildren<TMP_Text>().text.Contains("R10 local music")
        );
        steps.Add(UiDispatch((RectTransform)play.transform, false));
        until = Time.realtimeSinceStartup + 8;
        while (Time.realtimeSinceStartup < until && (plugin.Current.Source == null || !plugin.Current.Source.isPlaying))
            yield return null;
        float peak = 0;
        var pcm = new float[512];
        until = Time.realtimeSinceStartup + 1;
        while (Time.realtimeSinceStartup < until && peak <= .000001f)
        {
            if (plugin.Current.Source != null)
            {
                plugin.Current.Source.GetOutputData(pcm, 0);
                peak = pcm.Max(v => Mathf.Abs(v));
            }
            yield return null;
        }
        steps.Add(new JObject
        {
            ["id"] = "local-track-row-play",
            ["track"] = track.id,
            ["currentTrack"] = plugin.Current.State.TrackId,
            ["sourcePlaying"] = plugin.Current.Source != null && plugin.Current.Source.isPlaying,
            ["playing"] = plugin.Current.State.playing,
            ["queue"] = new JArray(plugin.Current.State.tracks),
            ["audioSourceCount"] = plugin.Current.GetComponents<AudioSource>().Length,
            ["nativeDspPeak"] = peak,
            ["dspSamples"] = pcm.Length
        });
        Need(
            plugin.Current.Source != null && plugin.Current.Source.isPlaying && plugin.Current.State.TrackId == track.id
            && peak > .000001f,
            "Selected local track did not produce native audio"
        );
        steps.Add(UiDispatch(UiButton("Tab3"), false));
        yield return new WaitForSecondsRealtime(.2f);
        steps.Add(UiDispatch(UiButton("CassetteEject"), false));
        yield return new WaitForSecondsRealtime(.2f);
        steps.Add(
            new JObject
            {
                ["id"] = "native-cassette-eject",
                ["inserted"] = plugin.Current.State.insertedCassette == null,
                ["sameCassetteInventory"] = player.GetInventory().GetAllItems().Any(
                i => BoomboxItemData.IsCassette(i) && BoomboxItemData.Read(i).cassette.id == cassetteId
            ),
                ["playing"] = plugin.Current.State.playing
            }
        );
        Need(
            plugin.Current.State.insertedCassette == null
            && player.GetInventory().GetAllItems().Any(
                i => BoomboxItemData.IsCassette(i) && BoomboxItemData.Read(i).cassette.id == cassetteId
            ),
            "Eject did not return the same cassette to inventory"
        );
        steps.Add(UiDispatch(UiButton("Close"), false));
        yield return new WaitForSecondsRealtime(.2f);
    }

    private static RectTransform UiButton(
        string name
    ) => (RectTransform)UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(
        b => b.name == name && b.gameObject.activeInHierarchy
    ).transform;
    private static IEnumerator UiAwait(Func<bool> ready, string action)
    {
        float until = Time.realtimeSinceStartup + 3;
        while (!ready() && Time.realtimeSinceStartup < until)
            yield return null;
        if (!ready())
            throw new InvalidOperationException("Native UI action did not settle: " + action);
        yield return new WaitForSecondsRealtime(.16f);
    }
    private static RectTransform UiInventoryItem(ItemDrop.ItemData item)
    {
        var grid = InventoryGui.instance.m_playerGrid;
        var element = AccessTools.Method(typeof(InventoryGrid), "GetElement").Invoke(
            grid,
            new object[] { item.m_gridPos.x, item.m_gridPos.y, Player.m_localPlayer.GetInventory().GetWidth() }
        );
        return element == null
            ? throw new InvalidOperationException("Native inventory UI item cell absent")
            : (RectTransform)AccessTools.Method(element.GetType(), "GetElementRectTransform").Invoke(element, null);
    }
    private static IEnumerator UiFrame(string name, JArray proof)
    {
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        var bytes = image.EncodeToPNG();
        UnityEngine.Object.Destroy(image);
        var path = Path.Combine(BepInEx.Paths.ConfigPath, "capture", name);
        File.WriteAllBytes(path, bytes);
        proof.Add(new JObject
        {
            ["id"] = "frame",
            ["file"] = path,
            ["sha"] = Hash(path),
            ["bytes"] = bytes.Length,
            ["width"] = Screen.width,
            ["height"] = Screen.height
        });
    }
}
