using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private QaPerfBridge perfBridge;
    private Stopwatch perfClock, perfOperationClock;
    private bool perfRunning, perfStop, perfComplete;
    private int perfWarm, perfMeasure, perfStep = -1, perfSeries;
    private string perfPhase, perfFailure, perfId, perfCaptureBefore;
    private double perfNext, perfResourceNext;
    private MusicLiveStream perfCurrentStream;
    private AudioClip perfCurrentClip;
    private readonly List<MusicLiveStream> perfRetired = new List<MusicLiveStream>();
    private readonly List<AudioClip> perfClips = new List<AudioClip>();
    private Dictionary<string, AudioDeck> perfRegistry;
    private bool[] perfSettings;
    private bool PerfOperation(string operation, JObject request)
    {
        if (!operation.StartsWith("perf-", StringComparison.Ordinal))
            return false;
        if (operation == "perf-stop")
        {
            perfStop = true;
            return true;
        }
        if (operation == "perf-observe-stop")
        {
            if (perfRunning)
                throw new InvalidOperationException("Stop actual series before telemetry");
            StopPerfTelemetry();
            return true;
        }
        if (operation != "perf-observe" && operation != "perf-start")
            throw new InvalidOperationException("Unknown performance action");
        if (perfBridge != null)
            throw new InvalidOperationException("Performance telemetry already owned");
        perfWarm = (int)request["warm"];
        perfMeasure = (int)request["measure"];
        if (!((perfWarm == 0 && perfMeasure == 120) || (perfWarm == 1200 && perfMeasure == 43200)))
            throw new InvalidOperationException("Only declared probe or twenty-minute plus twelve-hour series allowed");
        string project = BoomboxQa.QaEnvironment.QaRoot;
        perfCaptureBefore = Environment.GetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE");
        perfBridge = new QaPerfBridge((string)request["pipe"], project);
        Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", "1");
        perfClock = Stopwatch.StartNew();
        perfPhase = "observer";
        perfRegistry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
            .GetValue(BoomboxPlugin.Instance);
        if (operation == "perf-observe")
            return true;
        if (role != "client-a")
            throw new InvalidOperationException("Actual series belongs to A");
        var settings = BoomboxPlugin.Instance.Settings;
        perfSettings = new[] {
            settings.ShowVisualEffects.Value,
            settings.DecorationParticles.Value,
            settings.DecorationMovement.Value,
            settings.DecorationMusicMovement.Value
        };
        SetupAudio();
        perfId = audioDeck.State.id;
        audioDeck.State.decorationPlacements.Clear();
        string[] effects = { "Damage", "Health", "Stamina", "Eitr", "Speed" };
        audioDeck.State.unlockedDecorationPrefabs = new List<string> { "MushroomYellow", "TrophySkeleton" };
        for (int slot = 0; slot < 24; slot++)
            audioDeck.State.decorationPlacements.Add(new BoomboxDecorationPlacement
            {
                slot = slot,
                prefab = slot < 17 ? "MushroomYellow" : "TrophySkeleton",
                effect = effects[slot % 5],
                effectParticles = true
            });
        BoomboxItems.Persist(audioDeck);
        settings.ShowVisualEffects.Value =
            settings.DecorationParticles.Value =
            settings.DecorationMovement.Value =
            settings.DecorationMusicMovement.Value =
            true;
        audioDeck.VisualPresenter.RefreshState(true);
        BoomboxPlugin.Instance.PlayQueue(audioDeck, new[] { "qa-r11-0", "qa-r11-1" }, null, 0);
        audioDeck.State.repeat = true;
        perfRunning = true;
        perfPhase = "preparing";
        StartCoroutine(PerfGuard(PerfRun()));
        return true;
    }
    private IEnumerator PerfGuard(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            bool more;
            try
            {
                more = stack.Peek().MoveNext();
            }
            catch (Exception failure)
            {
                perfFailure = failure.ToString();
                break;
            }
            if (!more)
            {
                (stack.Pop() as IDisposable)?.Dispose();
                continue;
            }
            object current = stack.Peek().Current;
            if (current is IEnumerator nested)
            {
                stack.Push(nested);
                continue;
            }
            yield return current;
        }
        while (stack.Count > 0)
            (stack.Pop() as IDisposable)?.Dispose();
        perfPhase = "clearing";
        BoomboxPlugin.Instance.CloseMenu();
        InventoryGui.instance?.Hide();
        PerfFollow();
        Clear();
        audioDeck = null;
        var cleanup = Stopwatch.StartNew();
        while (cleanup.Elapsed.TotalSeconds < 30)
        {
            PerfFollow();
            if (!perfRegistry.ContainsKey(perfId)
                && perfRetired.Count == 0
                && perfClips.Count == 0
                && PerfAudioOwners() == 0)
                break;
            yield return null;
        }
        if (perfRegistry.ContainsKey(perfId)
            || perfRetired.Count != 0
            || perfClips.Count != 0
            || PerfAudioOwners() != 0)
            perfFailure = perfFailure ?? "Actual stream/clip/subscription cleanup did not settle";
        var settings = BoomboxPlugin.Instance.Settings;
        settings.ShowVisualEffects.Value = perfSettings[0];
        settings.DecorationParticles.Value = perfSettings[1];
        settings.DecorationMovement.Value = perfSettings[2];
        settings.DecorationMusicMovement.Value = perfSettings[3];
        perfRunning = false;
        perfPhase = perfFailure == null ? "cleared" : "failed";
        perfResourceNext = 0;
    }
    private IEnumerator PerfRun()
    {
        yield return PerfAwait();
        long nonzero = audioTap.Nonzero;
        var audible = Stopwatch.StartNew();
        while (audioTap.Nonzero <= nonzero && audible.Elapsed.TotalSeconds < 3)
            yield return null;
        Need(
            audioTap.Nonzero > nonzero
                && audioDeck.Source.clip.channels == 2
                && audioDeck.Source.clip.frequency == 48000,
            "Actual stereo48k PCM gate failed"
        );
        PerfFullFixture();
        perfClock.Restart();
        perfNext = perfResourceNext = 0;
        while (!perfStop && perfClock.Elapsed.TotalSeconds < perfWarm + perfMeasure)
        {
            double origin = perfClock.Elapsed.TotalSeconds;
            for (perfStep = 0; perfStep < 12
                && !perfStop
                && perfClock.Elapsed.TotalSeconds < perfWarm + perfMeasure; perfStep++)
            {
                perfOperationClock = Stopwatch.StartNew();
                PerfFollow();
                var player = Player.m_localPlayer;
                var plugin = BoomboxPlugin.Instance;
                Need(
                    audioDeck != null && !player.IsDead() && !player.IsTeleporting(),
                    "Native apparatus/player missing during series"
                );
                perfPhase = new[] {
                    "play",
                    "pause",
                    "resume",
                    "seek",
                    "next",
                    "menu-open",
                    "menu-close",
                    "unequip",
                    "equip",
                    "place",
                    "pickup",
                    "play-settle"
                }[perfStep];
                if (perfStep == 0 || perfStep == 2 || perfStep == 11)
                {
                    if (!audioDeck.State.playing)
                        audioDeck.TogglePlayback();
                    yield return PerfAwait();
                }
                else if (perfStep == 1)
                {
                    if (audioDeck.State.playing)
                        audioDeck.TogglePlayback();
                    Need(!audioDeck.Source.isPlaying, "Actual pause failed");
                }
                else if (perfStep == 3)
                {
                    Need(audioDeck.Seek(audioDeck.Source.timeSamples < 48000 ? 1.25f : .25f), "Actual seek refused");
                    yield return PerfAwait();
                }
                else if (perfStep == 4)
                {
                    audioDeck.SelectTrack(audioDeck.State.trackIndex + 1);
                    yield return PerfAwait();
                }
                else if (perfStep == 5)
                {
                    plugin.Open(audioDeck);
                    Need(plugin.MenuOpen, "Actual menu did not open");
                }
                else if (perfStep == 6)
                {
                    plugin.CloseMenu();
                    InventoryGui.instance?.Hide();
                    Need(!plugin.MenuOpen, "Actual menu did not close");
                }
                else if (perfStep == 7)
                    player.UnequipItem(audioDeck.Item, false);
                else if (perfStep == 8)
                {
                    Need(player.EquipItem(audioDeck.Item, false), "Native equip refused");
                    plugin.Items.Update();
                    yield return PerfAwait();
                }
                else if (perfStep == 9)
                {
                    Need(
                        plugin.Items.PutDown(
                            audioDeck,
                            player.transform.position + player.transform.forward * 2,
                            Quaternion.identity,
                            null
                        ),
                        "Actual placement refused"
                    );
                    yield return PerfAwait();
                }
                else if (perfStep == 10)
                {
                    Need(plugin.Items.PickUp(audioDeck), "Actual pickup refused");
                    yield return PerfAwait();
                }
                while (!perfStop
                    && perfClock.Elapsed.TotalSeconds < Math.Min(origin + (perfStep + 1) * 5, perfWarm + perfMeasure))
                    yield return null;
                PerfFollow();
                if (perfStep == 7)
                    Need(
                        !audioDeck.Item.m_equipped && !audioDeck.Source.isPlaying,
                        "Native unequip did not suspend source"
                    );
                if (perfStep == 9)
                    Need(audioDeck.WorldDrop != null, "Placed native object missing");
                if (perfStep == 10)
                {
                    Need(
                        audioDeck.WorldDrop == null
                            && player.GetInventory().ContainsItem(audioDeck.Item)
                            && audioDeck.Item.m_equipped,
                        "Native pickup/equip did not settle"
                    );
                    PerfFullFixture();
                }
            }
            perfSeries++;
        }
        perfComplete = !perfStop && perfClock.Elapsed.TotalSeconds >= perfWarm + perfMeasure;
    }
    private IEnumerator PerfAwait()
    {
        var watch = Stopwatch.StartNew();
        while (!perfStop)
        {
            PerfFollow();
            Need(watch.Elapsed.TotalSeconds < 15, "Source readiness deadline");
            Need(
                audioDeck != null
                    && (audioDeck.Stream == null || audioDeck.Stream.Error == null
                        && audioDeck.Stream.CleanupError == null),
                "Actual stream error"
            );
            if (audioDeck != null && !audioDeck.Loading && audioDeck.Source.clip != null && audioDeck.IsActivePlayback)
                yield break;
            yield return null;
        }
    }
    private void PerfFollow()
    {
        if (perfId != null)
            perfRegistry.TryGetValue(perfId, out audioDeck);
        var next = audioDeck?.Stream;
        var clip = audioDeck?.Source?.clip;
        if (perfCurrentStream != null && perfCurrentStream != next)
            perfRetired.Add(perfCurrentStream);
        if (perfCurrentClip != null && perfCurrentClip != clip)
            perfClips.Add(perfCurrentClip);
        perfCurrentStream = next;
        perfCurrentClip = clip;
        perfRetired.RemoveAll(s => s.Stopped);
        perfClips.RemoveAll(c => c == null);
        Need(perfRetired.Count <= 4 && perfClips.Count <= 4, "Retired resources accumulated");
        if (audioDeck != null)
        {
            decks["AUDIO"] = audioDeck;
            audioTap = audioDeck.Source.GetComponent<QaAudioTap>()
                ?? audioDeck.Source.gameObject.AddComponent<QaAudioTap>();
        }
        PerfCountTap();
    }
    private void StopPerfTelemetry()
    {
        var bridge = perfBridge;
        if (bridge == null)
            return;
        try
        {
            bridge.Dispose();
        }
        finally
        {
            perfBridge = null;
            Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", perfCaptureBefore);
        }
    }
    private void PerfFullFixture()
    {
        var saved = BoomboxItemData.Read(audioDeck.Item).deck;
        var slots = audioDeck.Visual.GetComponentsInChildren<Transform>(true).Where(
            t => t.name.StartsWith("DecorationSlot_", StringComparison.Ordinal)
        ).ToArray();
        Need(
            audioDeck.State.decorationPlacements.Count == 24
                && saved.decorationPlacements.Count == 24
                && slots.Length == 24,
            "Full24 stored/rendered fixture missing"
        );
        Need(
            saved.decorationPlacements.Count(p => p.slot < 17 && p.prefab == "MushroomYellow") == 17
            && saved.decorationPlacements.Count(p => p.slot >= 17 && p.slot < 24 && p.prefab == "TrophySkeleton") == 7,
            "Stored17pendants/7heads missing"
        );
        Need(
            slots.All(t => t.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled)),
            "Rendered decoration slot is empty"
        );
        perfFixtureGates++;
    }
}
