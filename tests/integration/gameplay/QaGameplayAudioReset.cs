using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private int nativeAudioChanges;
    private readonly List<bool> nativeAudioDevices = new List<bool>();
    private bool AudioResetOperation(string operation, JObject request)
    {
        if (operation != "audio-native-reset")
            return false;
        if (role != "client-a")
            throw new InvalidOperationException("Actual reset probe belongs to client A");
        busy = true;
        StartCoroutine(GuardAudio(NativeAudioReset()));
        return true;
    }
    private void ObserveAudioReset(bool deviceChanged)
    {
        nativeAudioChanges++;
        nativeAudioDevices.Add(deviceChanged);
    }
    private IEnumerator NativeAudioReset()
    {
        AudioConfiguration original = AudioSettings.GetConfiguration();
        var results = new JArray();
        nativeAudioChanges = 0;
        nativeAudioDevices.Clear();
        bool restored = false;
        var muted = new OwnAudioSessionMute();
        AudioSettings.OnAudioConfigurationChanged += ObserveAudioReset;
        try
        {
            yield return AwaitAudio();
            Need(audioDeck.Seek(4f), "Native pre-reset seek refused");
            yield return AwaitAudio();
            yield return new WaitForSecondsRealtime(.05f);
            foreach (bool paused in new[] { false, true })
            {
                if (audioDeck.State.playing == paused)
                    audioDeck.TogglePlayback();
                yield return new WaitForSecondsRealtime(.15f);
                audioDeck.Capture();
                float before = audioDeck.State.seconds;
                string track = audioDeck.State.TrackId;
                var oldClip = audioDeck.Source.clip;
                var oldStream = audioDeck.Stream;
                int subscriptions = PerfAudioOwners(), changed = nativeAudioChanges;
                long pcm = audioTap.Nonzero;
                double started = Time.realtimeSinceStartupAsDouble;
                var next = original;
                next.dspBufferSize = original.dspBufferSize == 1024 ? 512 : 1024;
                Need(AudioSettings.Reset(next), "Actual Unity AudioSettings.Reset returned false");
                float deadline = Time.realtimeSinceStartup + 12f;
                while (audioDeck.Source.clip == oldClip || audioDeck.Loading || nativeAudioChanges == changed)
                {
                    Need(Time.realtimeSinceStartup < deadline, "Native engine reset callback/new clip did not settle");
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(.25f);
                audioDeck.Capture();
                bool signal = paused || audioTap.Nonzero > pcm;
                bool state = audioDeck.State.TrackId == track
                    && audioDeck.State.playing == !paused
                    && audioDeck.Source.isPlaying == !paused;
                double elapsed = Time.realtimeSinceStartupAsDouble - started;
                bool position = Math.Abs(audioDeck.State.seconds - before) <= (paused ? .03 : elapsed + .5) && audioDeck.State.seconds >= before - .03;
                bool released = oldClip == null && (ReferenceEquals(oldStream, audioDeck.Stream) || oldStream.Stopped)
                && PerfAudioOwners() == subscriptions;
                var actual = AudioSettings.GetConfiguration();
                bool configured = actual.dspBufferSize == next.dspBufferSize && actual.sampleRate == original.sampleRate
                && actual.speakerMode == original.speakerMode;
                var result = new JObject
                {
                    ["id"] = paused ? "AUDIO-03-native-paused-reset" : "AUDIO-03-native-playing-reset",
                    ["passed"] = signal && state && position && released && configured,
                    ["paused"] = paused,
                    ["beforeSeconds"] = before,
                    ["afterSeconds"] = audioDeck.State.seconds,
                    ["elapsed"] = elapsed,
                    ["track"] = track,
                    ["nativeEvents"] = nativeAudioChanges - changed,
                    ["deviceChanged"] = new JArray(nativeAudioDevices),
                    ["oldClipDestroyed"] = oldClip == null,
                    ["oldStreamCurrent"] = ReferenceEquals(oldStream, audioDeck.Stream),
                    ["oldStreamStopped"] = oldStream.Stopped,
                    ["beforeSubscriptions"] = subscriptions,
                    ["afterSubscriptions"] = PerfAudioOwners(),
                    ["beforePcmNonzero"] = pcm,
                    ["afterPcmNonzero"] = audioTap.Nonzero,
                    ["original"] = AudioConfigurationJson(original),
                    ["requested"] = AudioConfigurationJson(next),
                    ["actual"] = AudioConfigurationJson(actual),
                    ["statePreserved"] = state,
                    ["positionPreserved"] = position
                };
                results.Add(result);
                sample = new JObject { ["resetCases"] = results };
                Need(
                    (bool)result["passed"],
                    "Actual native audio reset contract failed: " + result.ToString(Newtonsoft.Json.Formatting.None)
                );
                Need(AudioSettings.Reset(original), "Original Unity AudioConfiguration restore refused");
                yield return new WaitForSecondsRealtime(.5f);
                if (paused)
                    audioDeck.TogglePlayback();
            }
            int restoring = nativeAudioChanges;
            Need(AudioSettings.Reset(original), "Final original Unity configuration restore refused");
            float restoreDeadline = Time.realtimeSinceStartup + 6f;
            while (nativeAudioChanges == restoring)
            {
                Need(
                    Time.realtimeSinceStartup < restoreDeadline,
                    "Original configuration engine restore event missing"
                );
                yield return null;
            }
            yield return null;
            restored = SameAudioConfiguration(original, AudioSettings.GetConfiguration());
            Need(restored, "Actual final Unity configuration differs from original");
        }
        finally
        {
            if (!SameAudioConfiguration(original, AudioSettings.GetConfiguration()))
                AudioSettings.Reset(original);
            AudioSettings.OnAudioConfigurationChanged -= ObserveAudioReset;
            sample = new JObject
            {
                ["resetCases"] = results,
                ["configurationRestored"] = restored
                    && SameAudioConfiguration(original, AudioSettings.GetConfiguration()),
                ["original"] = AudioConfigurationJson(original),
                ["final"] = AudioConfigurationJson(AudioSettings.GetConfiguration()),
                ["nativeEvents"] = nativeAudioChanges
            };
            muted.Dispose();
        }
    }
    private static JObject AudioConfigurationJson(AudioConfiguration config) => new JObject
    {
        ["sampleRate"] = config.sampleRate,
        ["speakerMode"] = config.speakerMode.ToString(),
        ["dspBufferSize"] = config.dspBufferSize,
        ["realVoices"] = config.numRealVoices,
        ["virtualVoices"] = config.numVirtualVoices
    };
    private static bool SameAudioConfiguration(
        AudioConfiguration left,
        AudioConfiguration right
    ) => left.sampleRate == right.sampleRate && left.speakerMode == right.speakerMode
    && left.dspBufferSize == right.dspBufferSize
    && left.numRealVoices == right.numRealVoices
    && left.numVirtualVoices == right.numVirtualVoices;
}
