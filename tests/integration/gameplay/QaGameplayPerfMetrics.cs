using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private long perfSequence, perfCallbacks, perfNonzero, perfLastCallbacks, perfLastNonzero;
    private QaAudioTap perfTapSeen;
    private readonly double[] perfFrames = new double[1024];
    private int perfFrameCount;
    private int perfFixtureGates;
    private readonly int[] perfResources = new int[12];
    private void PerfCountTap()
    {
        if (!ReferenceEquals(perfTapSeen, audioTap))
        {
            if (!ReferenceEquals(perfTapSeen, null))
            {
                perfCallbacks += Math.Max(0, perfTapSeen.Callbacks - perfLastCallbacks);
                perfNonzero += Math.Max(0, perfTapSeen.Nonzero - perfLastNonzero);
            }
            perfTapSeen = audioTap;
            perfLastCallbacks = perfLastNonzero = 0;
        }
        if (ReferenceEquals(perfTapSeen, null))
            return;
        long callbacks = perfTapSeen.Callbacks, nonzero = perfTapSeen.Nonzero;
        perfCallbacks += Math.Max(0, callbacks - perfLastCallbacks);
        perfNonzero += Math.Max(0, nonzero - perfLastNonzero);
        perfLastCallbacks = callbacks;
        perfLastNonzero = nonzero;
    }
    private static int PerfAudioOwners()
    {
        var field = AccessTools.Field(typeof(AudioSettings), "OnAudioConfigurationChanged");
        if (field == null)
            throw new InvalidOperationException("Actual audio subscription field unavailable");
        var callbacks = field.GetValue(null) as Delegate;
        return callbacks?.GetInvocationList().Count(d => d.Target is AudioDeck) ?? 0;
    }
    private static int PerfThreads(MusicLiveStream live)
    {
        if (live == null)
            return 0;
        lock (AccessTools.Field(typeof(MusicLiveStream), "gate").GetValue(live))
        {
            int count = ((Thread)AccessTools.Field(typeof(MusicLiveStream), "supervisor").GetValue(live)).IsAlive
                ? 1
                : 0;
            var worker = AccessTools.Field(typeof(MusicLiveStream), "worker").GetValue(live);
            if (worker == null)
                return count;
            if (((Thread)AccessTools.Field(worker.GetType(), "Thread").GetValue(worker)).IsAlive)
                count++;
            var feed = AccessTools.Field(worker.GetType(), "Feed").GetValue(worker);
            if (feed != null && ((Thread)AccessTools.Field(feed.GetType(), "thread").GetValue(feed)).IsAlive)
                count++;
            return count;
        }
    }
    private void PerfTick()
    {
        if (perfBridge == null)
            return;
        if (perfFrameCount < perfFrames.Length)
            perfFrames[perfFrameCount++] = Time.unscaledDeltaTime * 1000.0;
        double now = perfClock.Elapsed.TotalSeconds;
        if (now < perfNext)
            return;
        perfNext = now + 1;
        var overhead = Stopwatch.StartNew();
        try
        {
            if (role == "client-a" && perfId != null)
                PerfFollow();
            bool settled = !perfRunning
                || perfOperationClock != null
                && perfOperationClock.Elapsed.TotalSeconds >= 2
                && audioDeck != null
                && !audioDeck.Loading
                && perfRetired.Count == 0
                && perfClips.Count == 0;
            if (settled && now >= perfResourceNext)
            {
                perfResourceNext = now + 60;
                perfResources[3] = Resources.FindObjectsOfTypeAll<AudioClip>().Count(
                    c => c.name.StartsWith("qa-r11-", StringComparison.Ordinal)
                );
                perfResources[4] = Resources.FindObjectsOfTypeAll<Material>().Length;
                perfResources[5] = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                perfResources[6] = Resources.FindObjectsOfTypeAll<Texture>().Length;
            }
            perfResources[0] = perfRegistry.Count;
            perfResources[1] = BoomboxAuraNetwork.Sources.Count(s => s.Active);
            perfResources[2] = BoomboxRemoteNetwork.Current?.Views.Count() ?? 0;
            perfResources[7] = perfRetired.Count + perfClips.Count;
            perfResources[8] = PerfThreads(perfCurrentStream) + perfRetired.Sum(PerfThreads);
            perfResources[9] = PerfAudioOwners();
            perfResources[10] = 1;
            Array.Sort(perfFrames, 0, perfFrameCount);
            double median = perfFrameCount == 0
                ? 0
                : perfFrames[perfFrameCount / 2], p95 = perfFrameCount == 0
                    ? 0
                    : perfFrames[(int)Math.Min(perfFrameCount - 1, Math.Ceiling(perfFrameCount * .95) - 1)];
            var value = new JObject
            {
                ["role"] = role,
                ["pid"] = Process.GetCurrentProcess().Id,
                ["sha"] = productSha,
                ["sequence"] = ++perfSequence,
                ["tick"] = Stopwatch.GetTimestamp(),
                ["seconds"] = now,
                ["warm"] = perfWarm,
                ["measure"] = perfMeasure,
                ["step"] = perfStep,
                ["series"] = perfSeries,
                ["phase"] = perfPhase,
                ["running"] = perfRunning,
                ["complete"] = perfComplete,
                ["settled"] = settled,
                ["fullFixtureGates"] = perfFixtureGates,
                ["failure"] = perfFailure ?? perfBridge.Error,
                ["errors"] = perfBridge.Errors,
                ["firstError"] = perfBridge.FirstError,
                ["managed"] = GC.GetTotalMemory(false),
                ["monoUsed"] = Profiler.GetMonoUsedSizeLong(),
                ["unityAllocated"] = Profiler.GetTotalAllocatedMemoryLong(),
                ["unityReserved"] = Profiler.GetTotalReservedMemoryLong(),
                ["gc0"] = GC.CollectionCount(0),
                ["gc1"] = GC.CollectionCount(1),
                ["gc2"] = GC.CollectionCount(2),
                ["frameCount"] = perfFrameCount,
                ["frameMedian"] = median,
                ["frameP95"] = p95,
                ["frameBufferFull"] = perfFrameCount == perfFrames.Length,
                ["resources"] = new JArray(perfResources),
                ["buffer"] = audioDeck?.AudioBufferBytes ?? 0,
                ["playing"] = audioDeck?.Source?.isPlaying ?? false,
                ["position"] = audioDeck?.Source?.clip != null ? audioDeck.Source.timeSamples : -1,
                ["launches"] = audioDeck?.Stream?.Launches ?? 0,
                ["underruns"] = audioDeck?.Stream?.Underruns ?? 0,
                ["callbacks"] = perfCallbacks,
                ["nonzero"] = perfNonzero,
                ["dsp"] = AudioSettings.dspTime,
                ["observerMs"] = overhead.Elapsed.TotalMilliseconds,
                ["cachePath"] = BoomboxPlugin.Instance.CachePath.Value
            };
            perfFrameCount = 0;
            perfBridge.Publish(value);
        }
        catch (Exception failure)
        {
            perfFailure = failure.ToString();
            perfStop = true;
        }
    }
}
