using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private bool NativeMusicOperation(string operation)
    {
        if (operation != "audio-native-music")
            return false;
        busy = true;
        StartCoroutine(GuardAudio(NativeMusicCase()));
        return true;
    }
    private IEnumerator NativeMusicCase()
    {
        UiParkWindow();
        var plugin = BoomboxPlugin.Instance;
        var man = MusicMan.instance;
        var rows = new JArray();
        var proof = new JObject { ["cases"] = rows };
        sample = proof;
        float master = MusicMan.m_masterMusicVolume;
        var mode = BoomboxGameMusic.Mode;
        string trigger = (string)AccessTools.Field(typeof(MusicMan), "m_triggeredMusic").GetValue(man);
        var hashes = (Dictionary<int, MusicMan.NamedMusic>)AccessTools.Field(typeof(MusicMan), "m_musicHashes")
            .GetValue(man);
        var clips = new List<AudioClip>();
        var objects = new List<GameObject>();
        var muted = new OwnAudioSessionMute();
        float started = Time.realtimeSinceStartup;
        const string name = "boombox-qa-native-music";
        try
        {
            MusicMan.m_masterMusicVolume = 1f;
            BoomboxGameMusic.Mode = BoomboxGameMusicMode.Normal;
            var music = new MusicMan.NamedMusic
            {
                m_name = name,
                m_clips = new[] { NativeMusicTone(937.5f, clips) },
                m_volume = .5f,
                m_fadeInTime = .05f,
                m_loop = true,
                m_enabled = true
            };
            hashes.Add(name.GetStableHashCode(), music);
            man.TriggerMusic(name);
            var locationObject = new GameObject("QA native MusicLocation");
            objects.Add(locationObject);
            locationObject.SetActive(false);
            locationObject.transform.position = Player.m_localPlayer.transform.position;
            var locationSource = locationObject.AddComponent<AudioSource>();
            locationSource.clip = NativeMusicTone(1875f, clips);
            locationSource.volume = .4f;
            locationSource.spatialBlend = 0;
            locationSource.outputAudioMixerGroup = man.m_musicMixer;
            locationSource.bypassReverbZones = true;
            var location = locationObject.AddComponent<MusicLocation>();
            location.m_oneTime = false;
            location.m_notIfEnemies = false;
            location.m_addRadiusFromLocation = false;
            location.m_radius = 100;
            locationObject.SetActive(true);
            var controlObject = new GameObject("QA unpatched sound control");
            objects.Add(controlObject);
            var control = controlObject.AddComponent<AudioSource>();
            control.clip = NativeMusicTone(2812.5f, clips);
            control.volume = .2f;
            control.spatialBlend = 0;
            control.Play();
            var source = (AudioSource)AccessTools.Field(typeof(MusicMan), "m_musicSource").GetValue(man);
            yield return new WaitForSecondsRealtime(1.5f);
            Need(
                source.isPlaying && source.clip == music.m_clips[0] && locationSource.isPlaying,
                "Native music sources not started"
            );
            Create("GAME-MUSIC", new[] { 0, 0, 0, 0, 0 }, 0, false, Player.m_localPlayer.transform.position);
            var gameDeck = decks["GAME-MUSIC"];
            plugin.PlayQueue(gameDeck, new List<string> { BoomboxSyntheticTrackId.GameMusic }, null, 0);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (gameDeck.Moment.Time < .3 || gameDeck.Moment.Silent || gameDeck.Moment.Level <= 0)
            {
                Need(Time.realtimeSinceStartup < deadline, "Native GameMusic tap did not analyze samples");
                yield return null;
            }
            rows.Add(new JObject
            {
                ["id"] = "native-game-music-analysis",
                ["level"] = gameDeck.Moment.Level,
                ["time"] = gameDeck.Moment.Time,
                ["ranges"] = JObject.FromObject(gameDeck.Moment.Smooth),
                ["synthetic"] = gameDeck.IsSyntheticActive,
                ["ownClipAbsent"] = gameDeck.Source.clip == null
            });
            gameDeck.TogglePlayback();
            SetupAudio();
            audioDeck.State.volume = .001f;
            yield return AwaitAudio();
            Create(
                "MUSIC-SECOND",
                new[] { 0, 0, 0, 0, 0 },
                0,
                true,
                Player.m_localPlayer.transform.position + Vector3.right * 2
            );
            var second = decks["MUSIC-SECOND"];
            second.State.volume = .001f;
            second.State.repeat = true;
            plugin.PlayQueue(second, new List<string> { "qa-r11-0" }, null, 0);
            deadline = Time.realtimeSinceStartup + 8;
            while (!audioDeck.IsActivePlayback || !second.IsActivePlayback)
            {
                Need(Time.realtimeSinceStartup < deadline, "Two real streaming decks not active");
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.5f);
            JObject baseline = null;
            for (int prime = 0; prime < 5; prime++)
            {
                baseline = NativeMusicSignal(source, locationSource, control);
                yield return new WaitForSecondsRealtime(.1f);
            }
            rows.Add(new JObject { ["id"] = "normal", ["signal"] = baseline, ["twoOwnStreams"] = true });
            Need(
                (double)baseline["listenerMusic"] > .005 && (double)baseline["listenerLocation"] > .005
                && (double)baseline["listenerControl"] > .005,
                "Native listener baseline missing tones"
            );
            foreach (var value in new[] { BoomboxGameMusicMode.Quiet, BoomboxGameMusicMode.Off })
            {
                BoomboxGameMusic.Mode = value;
                yield return new WaitForSecondsRealtime(.8f);
                var signal = NativeMusicSignal(source, locationSource, control);
                double expected = value == BoomboxGameMusicMode.Quiet ? .2 : 0;
                rows.Add(new JObject { ["id"] = value.ToString(), ["signal"] = signal, ["expected"] = expected });
                NativeMusicCompare(signal, baseline, expected);
            }
            audioDeck.TogglePlayback();
            yield return new WaitForSecondsRealtime(.7f);
            var one = NativeMusicSignal(source, locationSource, control);
            rows.Add(
                new JObject { ["id"] = "stop-one", ["remainingPlaying"] = second.IsActivePlayback, ["signal"] = one }
            );
            Need(second.IsActivePlayback, "Second stream stopped unexpectedly");
            NativeMusicCompare(one, baseline, 0);
            second.TogglePlayback();
            yield return new WaitForSecondsRealtime(.8f);
            var restored = NativeMusicSignal(source, locationSource, control);
            rows.Add(new JObject { ["id"] = "stop-last", ["signal"] = restored });
            NativeMusicCompare(restored, baseline, 1);
            var tapType = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxMusicTap", true);
            var tap = source.GetComponent(tapType) ?? locationSource.GetComponent(tapType);
            Need(tap != null, "Real game music tap absent");
            AccessTools.Field(tapType, "listening").SetValue(tap, true);
            var callback = AccessTools.Method(tapType, "OnAudioFilterRead");
            var backlog = new float[4096];
            for (int i = 0; i < backlog.Length; i++)
                backlog[i] = .1f;
            for (int i = 0; i < 1000; i++)
                callback.Invoke(tap, new object[] { backlog, 1 });
            int queued = (int)AccessTools.Field(tapType, "count").GetValue(tap);
            int capacity = ((float[])AccessTools.Field(tapType, "pending").GetValue(tap)).Length;
            int drainCapacity = ((float[])AccessTools.Field(tapType, "draining").GetValue(tap)).Length;
            long dropped = (long)AccessTools.Field(tapType, "dropped").GetValue(tap);
            rows.Add(new JObject
            {
                ["id"] = "bounded-game-music-backlog",
                ["method"] = "Numeric challenge of the actual PCM callback while consumer is not draining",
                ["inputFrames"] = 4096000,
                ["queued"] = queued,
                ["capacity"] = capacity,
                ["drainCapacity"] = drainCapacity,
                ["dropped"] = dropped
            });
            Need(
                capacity == 65536 && drainCapacity == 65536 && queued <= capacity && dropped > 0,
                "Game music tap backlog exceeded fixed bound"
            );
            proof["passed"] = true;
            proof["coreSeconds"] = Time.realtimeSinceStartup - started;
        }
        finally
        {
            Clear();
            BoomboxGameMusic.Release();
            BoomboxGameMusic.Mode = mode;
            AccessTools.Method(typeof(MusicMan), "StopMusic").Invoke(man, null);
            hashes.Remove(name.GetStableHashCode());
            AccessTools.Field(typeof(MusicMan), "m_triggeredMusic").SetValue(man, trigger);
            MusicMan.m_masterMusicVolume = master;
            foreach (var obj in objects)
                if (obj != null)
                    UnityEngine.Object.Destroy(obj);
            foreach (var clip in clips)
                if (clip != null)
                    UnityEngine.Object.Destroy(clip);
            muted.Dispose();
            proof["nativeMasterRestored"] = MusicMan.m_masterMusicVolume == master;
            sample = proof;
        }
    }
    private static AudioClip NativeMusicTone(float frequency, List<AudioClip> owned)
    {
        const int rate = 48000;
        var samples = new float[rate * 30];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = .3f * Mathf.Sin(2f * Mathf.PI * frequency * i / rate);
        var clip = AudioClip.Create("QA tone " + frequency, samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        owned.Add(clip);
        return clip;
    }
    private static JObject NativeMusicSignal(AudioSource music, AudioSource location, AudioSource control)
    {
        var data = new float[4096];
        AudioListener.GetOutputData(data, 0);
        var result = new JObject
        {
            ["factor"] = BoomboxGameMusic.Factor,
            ["musicPlaying"] = music.isPlaying,
            ["locationPlaying"] = location.isPlaying,
            ["controlPlaying"] = control.isPlaying,
            ["listenerVolume"] = AudioListener.volume,
            ["listenerPaused"] = AudioListener.pause,
            ["musicVolume"] = music.volume,
            ["locationVolume"] = location.volume,
            ["controlVolume"] = control.volume,
            ["listenerMusic"] = Point3Amplitude(data, 937.5),
            ["listenerLocation"] = Point3Amplitude(data, 1875),
            ["listenerControl"] = Point3Amplitude(data, 2812.5)
        };
        music.GetOutputData(data, 0);
        result["musicSourceDsp"] = Point3Amplitude(data, 937.5);
        location.GetOutputData(data, 0);
        result["locationSourceDsp"] = Point3Amplitude(data, 1875);
        return result;
    }
    private static void NativeMusicCompare(JObject actual, JObject baseline, double expected)
    {
        foreach (string key in new[] { "listenerMusic", "listenerLocation" })
            Need(
                Math.Abs((double)actual[key] / (double)baseline[key] - expected) < .06,
                "Native DSP ratio mismatch: " + key + " " + actual.ToString(Newtonsoft.Json.Formatting.None)
            );
        Need(
            Math.Abs((double)actual["listenerControl"] / (double)baseline["listenerControl"] - 1) < .06,
            "Unpatched sound control changed"
        );
    }
}
