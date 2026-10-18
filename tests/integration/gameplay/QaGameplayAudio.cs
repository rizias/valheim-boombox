using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private AudioDeck audioDeck;
    private QaAudioTap audioTap;
    private readonly List<JObject> audioCases = new List<JObject>();
    private readonly List<MusicLiveStream> audioRetired = new List<MusicLiveStream>();
    private readonly HashSet<int> audioProcessIds = new HashSet<int>();
    private string audioPhase;
    private int audioCompleted;
    private int audioRetiredCount, audioRetiredStopped;
    private const int AudioSeed = 1106150;
    private bool AudioOperation(string operation, JObject request)
    {
        if (!operation.StartsWith("audio-", StringComparison.Ordinal))
            return false;
        if (operation == "audio-setup")
        {
            SetupAudio();
            string file = (string)request["fixture"];
            if (file != null)
            {
                string root = Path.GetFullPath(Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT"));
                string expected = Path.GetFullPath(Path.Combine(root, "current/membrane-literal/membranes.wav"));
                string demo = Path.GetFullPath(Path.Combine(root, "current/r12-video/demo-source.wav"));
                string actual = Path.GetFullPath(file);
                if ((!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
                && !actual.Equals(demo, StringComparison.OrdinalIgnoreCase))
                || !File.Exists(actual))
                    throw new InvalidOperationException("Owned mathematical WAV fixture missing");
                var plugin = BoomboxPlugin.Instance;
                plugin.MusicLibrary.Remember(
                    new MusicTrack
                    {
                        id = "qa-r12-membranes",
                        source = "file://" + file,
                        title = "Демонстрационный трек",
                        available = true
                    }
                );
                plugin.MusicLibrary.Save();
                plugin.PlayQueue(audioDeck, new List<string> { "qa-r12-membranes" }, null, 0);
            }
            return true;
        }
        if (operation == "audio-start")
        {
            if (role != "client-a")
                throw new InvalidOperationException("Local PCM probe belongs to real client A");
            SetupAudio();
            audioCases.Clear();
            audioRetired.Clear();
            audioCompleted = audioRetiredCount = audioRetiredStopped = 0;
            busy = true;
            StartCoroutine(GuardAudio(AudioCycles((int?)request["count"] ?? 2)));
            return true;
        }
        if (operation == "audio-continue")
        {
            busy = true;
            StartCoroutine(GuardAudio(AudioCycles((int?)request["count"] ?? 148)));
            return true;
        }
        if (operation == "audio-clear")
        {
            if (audioDeck?.Stream != null)
            {
                audioRetired.Add(audioDeck.Stream);
                audioRetiredCount++;
            }
            Clear();
            audioDeck = null;
            audioPhase = "cleared";
            return true;
        }
        if (operation == "audio-snapshot")
            return true;
        throw new InvalidOperationException("Unknown audio probe operation " + operation);
    }
    private void SetupAudio()
    {
        var plugin = BoomboxPlugin.Instance;
        if (Player.m_localPlayer == null || !plugin.AudioWorldActive)
            throw new InvalidOperationException("Real player/audio world not ready");
        string folder = Path.Combine(BoomboxQa.QaEnvironment.FixturesRoot, "audio/Музыка с пробелами");
        plugin.FFmpegPath.Value = BoomboxQa.QaEnvironment.Encoder;
        plugin.FFprobePath.Value = BoomboxQa.QaEnvironment.MediaProbe;
        plugin.Settings.AudioBufferSeconds.Value = 2f;
        var entries = new[] {
            ("source.wav", (int?)null),
            ("album.flac", (int?)null),
            ("блоки.wv", (int?)null),
            ("образ со ссылкой.iso.wv", (int?)2),
            ("album.cue", (int?)1),
            ("embedded.flac", (int?)2),
            ("music.zip", (int?)2)
        };
        var ids = new List<string>();
        for (int index = 0; index < entries.Length; index++)
        {
            string uri = entries[index].Item1 == "music.zip"
                ? "unpack://zip|0|file://" + Path.Combine(folder, entries[index].Item1) + "|inside/album.cue"
                : "file://" + Path.Combine(folder, entries[index].Item1);
            string id = "qa-r11-" + index;
            plugin.MusicLibrary.Remember(
                new MusicTrack
                {
                    id = id,
                    source = uri,
                    subsong = entries[index].Item2,
                    title = entries[index].Item1,
                    available = true
                }
            );
            ids.Add(id);
        }
        plugin.MusicLibrary.Save();
        Create("AUDIO", new[] { 0, 0, 0, 0, 0 }, 0, false, Player.m_localPlayer.transform.position);
        audioDeck = decks["AUDIO"];
        audioDeck.State.volume = .05f;
        audioDeck.State.repeat = true;
        audioTap = audioDeck.Source.gameObject.AddComponent<QaAudioTap>();
        plugin.PlayQueue(audioDeck, ids, null, 0);
        audioPhase = "preparing";
    }
    private IEnumerator GuardAudio(IEnumerator body)
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
                error = failure.ToString();
                while (stack.Count > 0)
                    try
                    {
                        (stack.Pop() as IDisposable)?.Dispose();
                    }
                    catch (Exception cleanup)
                    {
                        error += "\nCleanup: " + cleanup;
                    }
                audioPhase = "failed";
                busy = false;
                Logger.LogError(error);
                yield break;
            }
            if (!more)
            {
                stack.Pop();
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
        audioPhase = "ready";
        busy = false;
    }
    private IEnumerator AudioCycles(int count)
    {
        if (count < 1 || audioCompleted + count > 150)
            throw new InvalidOperationException("Audio cycle count outside declared 150");
        var random = new System.Random(AudioSeed + audioCompleted);
        for (int offset = 0; offset < count; offset++)
        {
            int cycle = audioCompleted, kind = cycle % 10;
            audioPhase = "cycle-" + cycle;
            yield return AwaitAudio();
            var oldStream = audioDeck.Stream;
            var oldClip = audioDeck.Source.clip;
            int oldClipId = oldClip.GetInstanceID(), beforeFrame = audioDeck.Source.timeSamples;
            long callbacks = audioTap.Callbacks, nonzero = audioTap.Nonzero;
            double dsp = AudioSettings.dspTime;
            string operation = new[] {
                "load",
                "select",
                "seek",
                "pause-seek-resume",
                "next",
                "seek-fixed",
                "reload",
                "eof-repeat",
                "reload-after-play",
                "eof-next"
            }[kind];
            float requested = 0f;
            bool mutation = false;
            if (kind == 0)
                BoomboxPlugin.Instance.PlayQueue(
                    audioDeck,
                    audioDeck.State.tracks,
                    null,
                    (audioDeck.State.trackIndex + 1 + random.Next(audioDeck.State.tracks.Count - 1))
                        % audioDeck.State.tracks.Count
                );
            else if (kind == 1 || kind == 4)
                audioDeck.SelectTrack(audioDeck.State.trackIndex + 1);
            else if (kind == 2 || kind == 3)
            {
                if (kind == 3)
                {
                    audioDeck.TogglePlayback();
                    yield return new WaitForSecondsRealtime(.08f);
                    Need(!audioDeck.Source.isPlaying, "Pause did not stop actual AudioSource");
                }
                requested = Math.Min(audioDeck.TrackSeconds - .4f, .3f + (float)random.NextDouble());
                if ((int)Math.Round(requested * MusicLiveStream.Frequency) == beforeFrame)
                    requested = .2f;
                mutation = (int)Math.Round(requested * MusicLiveStream.Frequency) != beforeFrame;
                Need(audioDeck.Seek(requested), "Real AudioDeck.Seek rejected position");
                if (kind == 3)
                {
                    yield return new WaitForSecondsRealtime(.08f);
                    Need(!audioDeck.State.playing && !audioDeck.Source.isPlaying, "Paused seek resumed playback");
                    audioDeck.TogglePlayback();
                }
            }
            else if (kind == 5)
            {
                requested = beforeFrame == MusicLiveStream.Frequency ? .5f : 1f;
                mutation = (int)Math.Round(requested * MusicLiveStream.Frequency) != beforeFrame;
                Need(audioDeck.Seek(requested), "Actual counted fixed seek rejected");
            }
            else if (kind == 6)
                audioDeck.LoadTrack();
            else if (kind == 8)
                audioDeck.LoadTrack();
            else
            {
                audioDeck.State.repeat = kind == 7;
                audioDeck.SelectTrack(kind == 7 ? audioDeck.State.tracks.Count - 1 : 0);
                yield return AwaitAudio();
                var ending = audioDeck.Source.clip;
                requested = audioDeck.TrackSeconds - .12f;
                Need(audioDeck.Seek(requested), "EOF seek rejected");
                float deadline = Time.realtimeSinceStartup + 10f;
                while (audioDeck.Source.clip == ending && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Need(
                    audioDeck.Source.clip != ending && audioDeck.State.trackIndex == (kind == 7 ? 0 : 1) && audioDeck.State.playing,
                    "Native EOF did not advance/repeat queue"
                );
                audioDeck.State.repeat = true;
            }
            yield return AwaitAudio();
            callbacks = audioTap.Callbacks;
            nonzero = audioTap.Nonzero;
            dsp = AudioSettings.dspTime;
            float audibleDeadline = Time.realtimeSinceStartup + 1.5f;
            while ((audioTap.Callbacks <= callbacks || audioTap.Nonzero <= nonzero || AudioSettings.dspTime <= dsp)
            && Time.realtimeSinceStartup < audibleDeadline)
                yield return null;
            Need(
                audioDeck.IsActivePlayback && audioDeck.Source.isPlaying && audioDeck.Source.timeSamples > 0,
                "Actual playback/DSP position did not advance"
            );
            Need(
                audioTap.Callbacks > callbacks && audioTap.Nonzero > nonzero && AudioSettings.dspTime > dsp,
                "Actual PCM callbacks have no nonzero samples or DSP time is frozen"
            );
            if (oldStream != audioDeck.Stream)
            {
                audioRetired.Add(oldStream);
                audioRetiredCount++;
                Need(oldStream.Stopped, "Old stream supervisor remains active after track replacement");
            }
            if (oldClip != audioDeck.Source.clip)
            {
                yield return null;
                yield return null;
                Need(oldClip == null, "Deferred old Unity AudioClip not destroyed");
            }
            mutation |= audioDeck.Stream != oldStream || audioDeck.Source.clip.GetInstanceID() != oldClipId;
            Need(mutation, "No-op must not count toward the actual 150 mutations");
            Need(audioDeck.AudioBufferBytes == 1843200, "Default native stream buffer changed");
            Need(audioRetired.All(s => s.Stopped), "A retired supervisor accumulated");
            audioRetiredStopped += audioRetired.Count;
            audioRetired.Clear();
            RecordProcess(audioDeck.Stream);
            audioCases.Add(
                new JObject
                {
                    ["cycle"] = cycle + 1,
                    ["operation"] = operation,
                    ["mutation"] = mutation,
                    ["beforeFrame"] = beforeFrame,
                    ["beforeClip"] = oldClipId,
                    ["requested"] = requested,
                    ["track"] = audioDeck.State.TrackId,
                    ["position"] = audioDeck.State.seconds,
                    ["timeSamples"] = audioDeck.Source.timeSamples,
                    ["clip"] = audioDeck.Source.clip.GetInstanceID(),
                    ["buffer"] = audioDeck.AudioBufferBytes,
                    ["launches"] = audioDeck.Stream.Launches,
                    ["underruns"] = audioDeck.Stream.Underruns,
                    ["callbacks"] = audioTap.Callbacks,
                    ["nonzero"] = audioTap.Nonzero,
                    ["rms"] = audioTap.Rms,
                    ["dsp"] = AudioSettings.dspTime,
                    ["retiredStopped"] = audioRetiredStopped,
                    ["clips"] = Resources.FindObjectsOfTypeAll<AudioClip>().Count(
                    c => c.name.StartsWith("qa-r11-", StringComparison.Ordinal)
                ),
                    ["ownedThreads"] = OwnedThreads(audioDeck.Stream)
                }
            );
            audioCompleted++;
        }
    }
    private IEnumerator AwaitAudio()
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (audioDeck != null && (audioDeck.Loading || audioDeck.Source.clip == null || !audioDeck.IsActivePlayback))
        {
            if (audioDeck.Stream != null)
                RecordProcess(audioDeck.Stream);
            Need(Time.realtimeSinceStartup < deadline, "AudioDeck playback preparation timeout: " + audioDeck.Status);
            Need(!audioDeck.Status.StartsWith("Ошибка", StringComparison.Ordinal), audioDeck.Status);
            yield return null;
        }
        Need(audioDeck != null && audioDeck.Stream != null, "Native AudioDeck/stream missing");
    }
    private static void Need(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    private void RecordProcess(MusicLiveStream live)
    {
        var worker = AccessTools.Field(typeof(MusicLiveStream), "worker").GetValue(live);
        if (worker == null)
            return;
        if (AccessTools.Field(worker.GetType(), "Process").GetValue(worker) is Process decoder)
            try
            {
                audioProcessIds.Add(decoder.Id);
            }
            catch (InvalidOperationException) { }
    }
    private static int OwnedThreads(MusicLiveStream live)
    {
        int count = ((Thread)AccessTools.Field(typeof(MusicLiveStream), "supervisor").GetValue(live)).IsAlive ? 1 : 0;
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
    private JObject AudioSnapshot()
    {
        if (audioPhase == null)
            return null;
        int stopped = audioRetired.Count(s => s.Stopped);
        var result = new JObject
        {
            ["phase"] = audioPhase,
            ["completed"] = audioCompleted,
            ["seed"] = AudioSeed,
            ["outputRate"] = AudioSettings.outputSampleRate,
            ["speakerMode"] = AudioSettings.speakerMode.ToString(),
            ["driver"] = AudioSettings.driverCapabilities.ToString(),
            ["dspTime"] = AudioSettings.dspTime,
            ["processIds"] = new JArray(audioProcessIds),
            ["retired"] = audioRetiredCount,
            ["retiredStopped"] = audioRetiredStopped + stopped,
            ["clips"] = Resources.FindObjectsOfTypeAll<AudioClip>().Count(
                c => c.name.StartsWith("qa-r11-", StringComparison.Ordinal)
            )
        };
        if (audioCases.Count > 0)
            result["latest"] = audioCases[audioCases.Count - 1].DeepClone();
        if (!busy)
            result["cases"] = new JArray(audioCases.Select(c => c.DeepClone()));
        if (audioTap != null)
        {
            result["callbacks"] = audioTap.Callbacks;
            result["nonzero"] = audioTap.Nonzero;
            result["rms"] = audioTap.Rms;
        }
        if (audioDeck != null)
        {
            result["playing"] = audioDeck.Source.isPlaying;
            result["loading"] = audioDeck.Loading;
            result["status"] = audioDeck.Status;
            result["buffer"] = audioDeck.AudioBufferBytes;
            result["virtual"] = audioDeck.Source.isVirtual;
            result["position"] = audioDeck.State.seconds;
            result["timeSamples"] = audioDeck.Source.timeSamples;
            result["listenerPause"] = AudioListener.pause;
            result["sourceVolume"] = audioDeck.Source.volume;
        }
        return result;
    }
}

public sealed class QaAudioTap : MonoBehaviour
{
    private long callbacks, nonzero;
    private double rms;
    public long Callbacks => Interlocked.Read(ref callbacks);
    public long Nonzero => Interlocked.Read(ref nonzero);
    public double Rms => Volatile.Read(ref rms);
    private void OnAudioFilterRead(float[] data, int channels)
    {
        double energy = 0;
        for (int index = 0; index < data.Length; index++)
            energy += data[index] * (double)data[index];
        Volatile.Write(ref rms, Math.Sqrt(energy / Math.Max(1, data.Length)));
        Interlocked.Increment(ref callbacks);
        if (energy > 1e-12)
            Interlocked.Increment(ref nonzero);
    }
}
