using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static QaGameplay point3Observer;
    private long point3Packets;
    private bool point3PrivatePath;
    private readonly HashSet<string> point3Methods = new HashSet<string>();
    private AudioDeck point3Audio;
    private QaAudioTap point3Tap;
    private int point3OutputReads;
    private readonly float[] point3SourceSamples = new float[4096];
    private readonly float[] point3ListenerSamples = new float[4096];
    private bool Point3Operation(string operation, JObject request)
    {
        if (operation == "point3-path")
        {
            var point = ReadPoint(request["position"]);
            var hits = Physics.CapsuleCastAll(
                point + Vector3.up * .35f,
                point + Vector3.up * 1.65f,
                .35f,
                Vector3.forward,
                22f,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            sample = new JObject
            {
                ["start"] = Point(point),
                ["hits"] = new JArray(hits.Select(hit => new JObject
                {
                    ["name"] = hit.collider.name,
                    ["root"] = hit.collider.transform.root.name,
                    ["distance"] = hit.distance,
                    ["point"] = Point(hit.point)
                }))
            };
            return true;
        }
        if (operation == "point3-trace")
        {
            point3Observer = this;
            safetyHarmony.Patch(
                AccessTools.Method(typeof(ZRpc), "Invoke", new[] { typeof(string), typeof(object[]) }),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(Point3Rpc))
            );
            return true;
        }
        if (operation == "point3-cadence")
        {
            if (!ZNet.instance.IsServer())
                throw new InvalidOperationException("Only the owned server changes fixture cadence");
            foreach (var recovery in new[] {
                BoomboxPlugin.Instance.Settings.Health,
                BoomboxPlugin.Instance.Settings.Stamina,
                BoomboxPlugin.Instance.Settings.Eitr
            })
                recovery.Interval.Value = .25f;
            return true;
        }
        if (operation == "point3-audio-start")
        {
            if (role == "server")
                throw new InvalidOperationException("Audio belongs to an owned client");
            Clear();
            Create("P3_AUDIO", new int[5], 0, false, Player.m_localPlayer.transform.position);
            point3Audio = decks["P3_AUDIO"];
            point3OutputReads = 0;
            point3Tap = point3Audio.Source.gameObject.AddComponent<QaAudioTap>();
            var plugin = BoomboxPlugin.Instance;
            string root = Path.GetFullPath(Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT"));
            string file = Path.Combine(root, "current/point3-private-audio", role + ".wav");
            if (!File.Exists(file))
                throw new InvalidOperationException("Prepared owned tone is absent");
            plugin.FFmpegPath.Value = BoomboxQa.QaEnvironment.Encoder;
            plugin.FFprobePath.Value = BoomboxQa.QaEnvironment.MediaProbe;
            string id = "qa-point3-local-" + role;
            plugin.MusicLibrary.Remember(
                new MusicTrack { id = id, source = "file://" + file, title = "Private owned tone", available = true }
            );
            plugin.MusicLibrary.Save();
            point3Audio.State.volume = .1f;
            point3Audio.State.repeat = true;
            plugin.PlayQueue(point3Audio, new List<string> { id }, null, 0);
            return true;
        }
        if (operation != "point3-snapshot")
            return false;
        var player = Player.m_localPlayer;
        sample = new JObject
        {
            ["packets"] = point3Packets,
            ["privatePathFound"] = point3PrivatePath,
            ["methods"] = new JArray(point3Methods.OrderBy(n => n)),
            ["values"] = player == null ? new JArray() : new JArray(BoomboxAuraNetwork.For(player).Values),
            ["ids"] = new JArray(BoomboxAuraNetwork.Sources.Select(s => s.Id))
        };
        if (point3Audio == null || point3Audio.Source == null)
            return true;
        sample["playing"] = point3Audio.IsActivePlayback;
        sample["position"] = point3Audio.Source.timeSamples;
        sample["foreignAudioSources"] = UnityEngine.Object.FindObjectsByType<AudioDeck>(FindObjectsSortMode.None).Count(
            d => d.State.owner != player.GetPlayerID() && d.Source != null
        );
        point3Audio.Source.GetOutputData(point3SourceSamples, 0);
        AudioListener.GetOutputData(point3ListenerSamples, 0);
        sample["outputReads"] = ++point3OutputReads;
        sample["source375"] = Point3Amplitude(point3SourceSamples, 375);
        sample["source750"] = Point3Amplitude(point3SourceSamples, 750);
        sample["listener375"] = Point3Amplitude(point3ListenerSamples, 375);
        sample["listener750"] = Point3Amplitude(point3ListenerSamples, 750);
        sample["rate"] = AudioSettings.outputSampleRate;
        sample["nativePcmCallbacks"] = point3Tap?.Callbacks;
        sample["nativePcmNonzero"] = point3Tap?.Nonzero;
        sample["nativePcmRms"] = point3Tap?.Rms;
        sample["sourceVirtual"] = point3Audio.Source.isVirtual;
        sample["sourceVolume"] = point3Audio.Source.volume;
        sample["sourceMute"] = point3Audio.Source.mute;
        sample["sourcePosition"] = Point(point3Audio.Source.transform.position);
        sample["sourceMaxDistance"] = point3Audio.Source.maxDistance;
        sample["listenerVolume"] = AudioListener.volume;
        sample["listenerPause"] = AudioListener.pause;
        sample["masterPreference"] = PlatformPrefs.GetFloat("MasterVolume", 1f);
        sample["musicPreference"] = PlatformPrefs.GetFloat("MusicVolume", 1f);
        sample["nativeMusicMaster"] = MusicMan.m_masterMusicVolume;
        sample["mixer"] = point3Audio.Source.outputAudioMixerGroup?.name;
        sample["listeners"] = new JArray(
            UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Select(l => new JObject
            {
                ["name"] = l.name,
                ["enabled"] = l.enabled,
                ["active"] = l.gameObject.activeInHierarchy,
                ["position"] = Point(l.transform.position),
                ["distance"] = Vector3.Distance(l.transform.position, point3Audio.Source.transform.position)
            })
        );
        return true;
    }
    private static void Point3Rpc(string __0, object[] __1)
    {
        var observer = point3Observer;
        if (observer == null || __1 == null)
            return;
        foreach (var argument in __1)
        {
            if (!(argument is ZPackage package))
                continue;
            observer.point3Packets++;
            if (observer.point3Methods.Count < 64)
                observer.point3Methods.Add(__0);
            string text = Encoding.UTF8.GetString(package.GetArray());
            observer.point3PrivatePath |= text.Contains("point3-private-audio")
                || text.Contains("client-a.wav")
                || text.Contains("client-b.wav");
        }
    }
    private static double Point3Amplitude(float[] samples, double frequency)
    {
        double real = 0, imaginary = 0, rate = AudioSettings.outputSampleRate;
        if (rate <= 0)
            return 0;
        for (int index = 0; index < samples.Length; index++)
        {
            double phase = 2 * Math.PI * frequency * index / rate;
            real += samples[index] * Math.Cos(phase);
            imaginary += samples[index] * Math.Sin(phase);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
    }
}
