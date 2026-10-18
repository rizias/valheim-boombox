using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private sealed class MenuCost
    {
        internal string Name;
        internal double Total, Max;
        internal int Calls;
    }
    private static readonly Dictionary<MethodBase, MenuCost> menuCosts = new Dictionary<MethodBase, MenuCost>();
    private static bool menuMeasuring;
    private static void MenuCostStart(out long __state)
    {
        __state = menuMeasuring ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
    }
    private static void MenuCostEnd(MethodBase __originalMethod, long __state)
    {
        if (__state == 0)
            return;
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - __state) * 1000d
            / System.Diagnostics.Stopwatch.Frequency;
        var entry = menuCosts[__originalMethod];
        entry.Total += ms;
        entry.Max = Math.Max(entry.Max, ms);
        entry.Calls++;
    }
    private bool menuParked;
    private int menuFps, menuVsync;
    private float menuScale;
    private bool menuAudioPause;
    private void ParkMenu(bool park)
    {
        if (park == menuParked)
            return;
        if (park)
        {
            menuFps = Application.targetFrameRate;
            menuVsync = QualitySettings.vSyncCount;
            menuScale = Time.timeScale;
            menuAudioPause = AudioListener.pause;
            BoomboxPlugin.Instance.CloseMenu();
            Time.timeScale = 0f;
            AudioListener.pause = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 8;
        }
        else
        {
            Time.timeScale = menuScale;
            AudioListener.pause = menuAudioPause;
            QualitySettings.vSyncCount = menuVsync;
            Application.targetFrameRate = menuFps;
        }
        menuParked = park;
    }
    private IEnumerator MenuLatencyCapture(bool recreate = false)
    {
        yield return AwaitAudio();
        var plugin = BoomboxPlugin.Instance;
        var assembly = typeof(AudioDeck).Assembly;
        var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
        if (recreate)
        {
            AccessTools.Method(menu.GetType(), "Dispose").Invoke(menu, null);
            yield return null;
        }
        Need(AccessTools.Field(menu.GetType(), "frame").GetValue(menu) == null, "Cold menu was already constructed");
        var rows = new JArray();
        var frames = new JArray();
        var result =
            new JObject
            {
                ["cases"] = rows,
                ["frames"] = frames,
                ["recreatedInWarmProcess"] = recreate,
                ["method"] =
                    "Real inventory Music button, native EventSystem raycast/handlers, normal graphical client; timings buffered until after each case"
            };
        lastCapture = result;
        float started = Time.realtimeSinceStartup;
        var savedCatalog = plugin.Catalog;
        string savedCapture = Environment.GetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE");
        var actor = Player.m_localPlayer;
        bool beforeCutscene = actor.InCutscene();
        if (CinematicsManager.IsPlaying())
            CinematicsManager.Stop();
        if (Valkyrie.m_instance != null)
            Valkyrie.m_instance.DropPlayer(true);
        actor.SetIntro(false);
        actor.SetSleeping(false);
        var animator = actor.GetComponentInChildren<Animator>();
        for (int step = 0; step < 4 && actor.InCutscene(); step++)
        {
            animator.Update(3f);
            yield return null;
        }
        result["actorPreparation"] = new JObject
        {
            ["beforeCutscene"] = beforeCutscene,
            ["afterCutscene"] = actor.InCutscene(),
            ["valkyrie"] = Valkyrie.m_instance != null,
            ["method"] = "Finish only QA spawn animation before input; no InCutscene patch"
        };
        Need(!actor.InCutscene(), "QA actor is not interactive after bounded spawn preparation");
        var patch = new Harmony("local.boombox.qa.menu-latency");
        menuCosts.Clear();
        foreach (var spec in new[] {
            ("BoomboxMenu", "Build"),
            ("BoomboxNativeUI", "get_Wood"),
            ("BoomboxNativeUI", "Button"),
            ("BoomboxNativeUI", "Text"),
            ("BoomboxLibraryData", "Refresh"),
            ("BoomboxLibraryTab", "Bind")
        })
        {
            var method = AccessTools.Method(assembly.GetType("VikingBoombox." + spec.Item1), spec.Item2);
            menuCosts[method] = new MenuCost { Name = spec.Item1 + "." + spec.Item2 };
            patch.Patch(
                method,
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(MenuCostStart)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(MenuCostEnd))
            );
        }
        try
        {
            string source = "file://" + System.IO.Path.Combine(
                Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT"),
                "current/r12-video/demo-source.wav"
            );
            MusicTrack Make(int i) => new MusicTrack
            {
                id = "menu-probe-" + i,
                title = "Проверка трека " + i,
                album = "Проверка альбома",
                source = source,
                available = true,
                duration = 40,
                durationSeconds = 40
            };
            var tracks = Enumerable.Range(0, 20).Select(Make).ToList();
            foreach (var track in tracks)
                plugin.MusicLibrary.rememberedTracks[track.id] = track;
            plugin.Catalog = new MusicCatalog
            {
                playlists = new List<MusicPlaylist> { new MusicPlaylist { name = "Menu probe", tracks = tracks } }
            };
            AccessTools.Property(typeof(BoomboxPlugin), "CatalogRevision").SetValue(plugin, plugin.CatalogRevision + 1);
            Player.m_localPlayer.AddKnownItem(audioDeck.Item);
            Need(BoomboxEquipment.Known, "Player must know the device before its inventory button is visible");
            InventoryGui.instance.Show(null, 1);
            var preparation = new JArray();
            result["inventoryPreparation"] = preparation;
            Button inventoryMusic = null;
            for (int step = 0; step < 20; step++)
            {
                yield return new WaitForSecondsRealtime(.1f);
                inventoryMusic = (Button)AccessTools.Field(typeof(BoomboxInventorySlot), "musicButton")
                    .GetValue(plugin.InventorySlot);
                var hit = inventoryMusic != null && inventoryMusic.gameObject.activeInHierarchy
                    ? UiRaycast((RectTransform)inventoryMusic.transform, false).gameObject
                    : null;
                preparation.Add(new JObject
                {
                    ["step"] = step,
                    ["known"] = BoomboxEquipment.Known,
                    ["inventoryVisible"] = InventoryGui.IsVisible(),
                    ["cutscene"] = Player.m_localPlayer.InCutscene(),
                    ["menuOpen"] = plugin.MenuOpen,
                    ["button"] = inventoryMusic != null,
                    ["self"] = inventoryMusic != null && inventoryMusic.gameObject.activeSelf,
                    ["hierarchy"] = inventoryMusic != null && inventoryMusic.gameObject.activeInHierarchy,
                    ["parent"] = inventoryMusic?.transform.parent.name,
                    ["playerPanel"] = InventoryGui.instance.m_player.gameObject.activeInHierarchy,
                    ["raycast"] = hit?.name
                });
                if (hit != null && hit.transform.IsChildOf(inventoryMusic.transform))
                    break;
                if (!InventoryGui.IsVisible())
                    InventoryGui.instance.Show(null, 1);
            }
            Need(
                inventoryMusic != null && inventoryMusic.gameObject.activeInHierarchy,
                "Native inventory Music button unavailable; see inventoryPreparation"
            );
            foreach (string kind in new[] { "cold", "warm" })
            {
                foreach (var cost in menuCosts.Values)
                {
                    cost.Total = cost.Max = 0;
                    cost.Calls = 0;
                }
                int gc = GC.CollectionCount(0);
                long heap = GC.GetTotalMemory(false);
                var intervals = new List<double>();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double firstCanvas = -1, interactive = -1, listReady = -1, last = 0;
                Canvas.WillRenderCanvases rendered = () =>
                {
                    if (plugin.MenuOpen && firstCanvas < 0)
                        firstCanvas = clock.Elapsed.TotalMilliseconds;
                };
                var music = (RectTransform)inventoryMusic.transform;
                Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", "1");
                Canvas.willRenderCanvases += rendered;
                menuMeasuring = true;
                JObject click;
                try
                {
                    clock.Restart();
                    click = UiDispatch(music, false);
                    result[kind + "Click"] = click;
                    Need((bool)click["hitsTarget"], "Inventory Music raycast missed");
                    for (int i = 0; i < 120; i++)
                    {
                        yield return new WaitForEndOfFrame();
                        Need(plugin.MenuOpen, "Native inventory closed the menu during measurement");
                        double now = clock.Elapsed.TotalMilliseconds;
                        intervals.Add(now - last);
                        last = now;
                        if (interactive < 0)
                        {
                            var window = AccessTools.Field(menu.GetType(), "frame").GetValue(menu);
                            if (window != null)
                            {
                                var rect = (RectTransform)AccessTools.Field(window.GetType(), "Window")
                                    .GetValue(window);
                                var button = rect.Find("Tab1")?.GetComponent<Button>();
                                if (button != null && button.isActiveAndEnabled && button.IsInteractable()
                                && UiRaycast((RectTransform)button.transform, false).gameObject != null)
                                    interactive = now;
                            }
                        }
                        var library = AccessTools.Field(menu.GetType(), "library").GetValue(menu);
                        var building = library == null ? null : AccessTools.Field(library.GetType(), "building");
                        int rowCount = library == null
                            ? 0
                            : ((IList)AccessTools.Field(library.GetType(), "rows").GetValue(library)).Count;
                        if (library != null
                            && rowCount >= 27
                            && (building == null || !(bool)building.GetValue(library))
                            && listReady < 0)
                            listReady = now;
                        if (i >= 15 && listReady >= 0)
                            break;
                        Need(clock.Elapsed.TotalSeconds < 4, "Menu list did not finish within bounded check");
                        yield return null;
                    }
                }
                finally
                {
                    menuMeasuring = false;
                    Canvas.willRenderCanvases -= rendered;
                    Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", savedCapture);
                }
                rows.Add(
                    new JObject
                    {
                        ["kind"] = kind,
                        ["tracks"] = tracks.Count,
                        ["firstCanvasMs"] = firstCanvas,
                        ["interactiveAtEndOfFrameMs"] = interactive,
                        ["fullListMs"] = listReady,
                        ["maxFrameMs"] = intervals.Max(),
                        ["intervalsMs"] = new JArray(intervals),
                        ["gcCollections"] = GC.CollectionCount(0) - gc,
                        ["heapDelta"] = GC.GetTotalMemory(false) - heap,
                        ["calls"] = new JArray(
                        menuCosts.Values.Select(
                            c => new JObject
                            {
                                ["name"] = c.Name,
                                ["calls"] = c.Calls,
                                ["totalMs"] = c.Total,
                                ["maxMs"] = c.Max
                            }
                        )
                    )
                    }
                );
                Need(
                    firstCanvas >= 0 && interactive >= 0 && listReady >= 0,
                    "Menu timing lacks a rendered, interactive or complete-list observation"
                );
                plugin.CloseMenu();
                yield return new WaitForSecondsRealtime(.15f);
            }
            plugin.Open(audioDeck);
            yield return new WaitForSecondsRealtime(.1f);
            var functionality = new JArray();
            result["functionality"] = functionality;
            foreach (int tab in new[] { 1, 2, 0, 1, 0 })
            {
                var click = UiDispatch(UiButton("Tab" + tab), false);
                Need((bool)click["hitsTarget"], "Tab input missed");
                yield return new WaitForSecondsRealtime(.1f);
                functionality.Add(new JObject { ["tab"] = tab, ["menuOpen"] = plugin.MenuOpen });
            }
            var addButton = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(
                b => b.name == "TrackAdd" && b.gameObject.activeInHierarchy
            );
            var addClick = UiDispatch((RectTransform)addButton.transform, false);
            Need((bool)addClick["hitsTarget"], "Track + input missed");
            yield return new WaitForSecondsRealtime(.1f);
            var actualPopup = AccessTools.Field(menu.GetType(), "popup").GetValue(menu);
            Need(actualPopup != null, "Lazy playlist popup not created");
            functionality.Add(new JObject { ["popupCreated"] = true });
            AccessTools.Method(actualPopup.GetType(), "Close").Invoke(actualPopup, null);
            var playTrack = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(
                b => b.name == "TrackPlay" && b.gameObject.activeInHierarchy
                    && b.GetComponentInChildren<TMPro.TMP_Text>().text.EndsWith(
                    "Проверка трека 0",
                    StringComparison.Ordinal
                )
            );
            Need((bool)UiDispatch((RectTransform)playTrack.transform, false)["hitsTarget"], "Track play input missed");
            yield return AwaitAudio();
            Need(
                audioDeck.State.TrackId == "menu-probe-0" && audioDeck.Source.isPlaying,
                "Deferred track callback selected wrong source"
            );
            functionality.Add(new JObject { ["trackCallback"] = audioDeck.State.TrackId, ["playing"] = true });
            foreach (var track in Enumerable.Range(20, 60).Select(Make))
            {
                tracks.Add(track);
                plugin.MusicLibrary.rememberedTracks[track.id] = track;
            }
            AccessTools.Property(typeof(BoomboxPlugin), "CatalogRevision").SetValue(plugin, plugin.CatalogRevision + 1);
            yield return new WaitForEndOfFrame();
            var view = AccessTools.Field(menu.GetType(), "library").GetValue(menu);
            var buildField = AccessTools.Field(view.GetType(), "building");
            bool interrupted = buildField != null && (bool)buildField.GetValue(view);
            plugin.CloseMenu();
            yield return new WaitForSecondsRealtime(.1f);
            Need(
                (bool)UiDispatch((RectTransform)inventoryMusic.transform, false)["hitsTarget"],
                "Reopen after partial binding missed"
            );
            float until = Time.realtimeSinceStartup + 2;
            do
            {
                yield return null;
                Need(Time.realtimeSinceStartup < until, "Reopened list did not finish");
            } while (buildField != null && (bool)buildField.GetValue(view));
            Need(
                plugin.MenuOpen && ((IList)AccessTools.Field(view.GetType(), "rows").GetValue(view)).Count >= 51,
                "Reopened page missing rows"
            );
            functionality.Add(new JObject { ["closedDuringBuild"] = interrupted, ["reopenedFullPage"] = true });
            yield return HudFrame("menu-latency.png", frames);
            result["coreSeconds"] = Time.realtimeSinceStartup - started;
            result["passed"] = true;
        }
        finally
        {
            menuMeasuring = false;
            Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", savedCapture);
            patch.UnpatchSelf();
            plugin.CloseMenu();
            plugin.Catalog = savedCatalog;
            foreach (string key in plugin.MusicLibrary.rememberedTracks.Keys.Where(
                k => k.StartsWith("menu-probe-", StringComparison.Ordinal)
            )
                .ToArray())
                plugin.MusicLibrary.rememberedTracks.Remove(key);
            AccessTools.Property(typeof(BoomboxPlugin), "CatalogRevision").SetValue(plugin, plugin.CatalogRevision + 1);
            lastCapture = result;
        }
    }
}
