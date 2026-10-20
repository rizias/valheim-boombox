using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator VideoProbe(JObject request)
    {
        string stem = (string)request["stem"] ?? "probe";
        double recordedSeconds = (double?)request["seconds"] ?? 2;
        bool context = (bool?)request["context"] ?? false;
        if (!((stem == "probe" && recordedSeconds == 2 && !context)
            || (stem == "demo-probe" && recordedSeconds == 2 && context)
        || (stem == "demo" && recordedSeconds == 30 && context)))
            throw new InvalidOperationException("Unknown bounded recording");
        yield return AwaitAudio();
        yield return new WaitForSecondsRealtime(2);
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || Camera.main == null)
            throw new InvalidOperationException("Real GPU/main camera required");
        var settings = BoomboxPlugin.Instance.Settings;
        bool visuals = settings.ShowVisualEffects.Value;
        bool debugTime = EnvMan.instance.m_debugTimeOfDay;
        float day = EnvMan.instance.m_debugTime;
        int rate = Application.targetFrameRate, sync = QualitySettings.vSyncCount;
        bool backgroundLimit = Settings.ReduceBackgroundUsage;
        string captureActive = Environment.GetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE");
        var main = Camera.main;
        var mainTarget = main.targetTexture;
        int mainMask = main.cullingMask;
        var small = new RenderTexture(32, 18, 16);
        var layers = new Dictionary<GameObject, int>();
        var cameraRoot = new GameObject("QA actual video camera");
        var lightRoot = new GameObject("QA actual video light");
        var camera = cameraRoot.AddComponent<Camera>();
        var target = new RenderTexture(QaVideoPipe.Width, QaVideoPipe.Height, 24);
        var rig = cameraRoot.AddComponent<QaVideoRender>();
        var tap = audioDeck.Source.gameObject.AddComponent<QaVideoAudio>();
        QaVideoPipe encoder = null;
        JObject recording = null;
        try
        {
            settings.ShowVisualEffects.Value = true;
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            Settings.ReduceBackgroundUsage = false;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            camera.CopyFrom(Camera.main);
            camera.enabled = false;
            small.Create();
            main.targetTexture = small;
            if (context)
                main.cullingMask = 0;
            camera.aspect = QaVideoPipe.Width / (float)QaVideoPipe.Height;
            camera.fieldOfView = 34;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 300;
            if (!context)
            {
                camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .105f, .14f);
                foreach (Transform node in audioDeck.Visual.GetComponentsInChildren<Transform>(true))
                {
                    layers[node.gameObject] = node.gameObject.layer;
                    node.gameObject.layer = 30;
                }
            }
            var light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.4f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << 30;
            light.enabled = !context;
            lightRoot.transform.rotation = Quaternion.LookRotation(-audioDeck.Visual.transform.forward -
                audioDeck.Visual.transform.up * .5f - audioDeck.Visual.transform.right * .25f);
            target.Create();
            encoder = new QaVideoPipe(
                (string)request["pipe"],
                true,
                new[]
                    {
                "-hide_banner",
                "-v",
                "error",
                "-nostats",
                "-nostdin",
                "-f",
                "rawvideo",
                "-pixel_format",
                "rgba",
                "-video_size",
                QaVideoPipe.Width + "x" + QaVideoPipe.Height,
                "-framerate",
                "60",
                "-i",
                "pipe:0",
                "-vf",
                "vflip",
                "-c:v",
                "h264_nvenc",
                "-preset",
                "p4",
                "-cq",
                "23",
                "-b:v",
                "0",
                "-bf",
                "0",
                "-pix_fmt",
                "yuv420p",
                "-fps_mode",
                "passthrough",
                "-an",
                "-movflags",
                "+frag_keyframe+empty_moov",
                "-f",
                "mp4",
                "pipe:1"
                    }
            );
            var modelFrame = audioDeck.Visual.transform;
            var modelCenter = modelFrame.TransformPoint(audioDeck.Visual.GetComponent<VikingBoomboxVisualState>()
                .BodyBounds.center)
                + modelFrame.up * .06f;
            camera.transform.position = modelCenter + modelFrame.forward * 1.8f;
            camera.transform.LookAt(modelCenter, modelFrame.up);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            var warm = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
            while (!warm.done)
                yield return null;
            if (warm.hasError)
                throw new InvalidOperationException("Native GPU warm readback failed");
            yield return new WaitForSecondsRealtime(1);
            using (var mute = new OwnAudioSessionMute())
            {
                Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", "1");
                tap.Begin();
                rig.Begin(audioDeck, camera, target, encoder, recordedSeconds + 2, context);
                while (!rig.Done)
                {
                    if (encoder.Error != null || tap.Error != null)
                        throw new InvalidOperationException(encoder.Error ?? tap.Error);
                    yield return null;
                }
                tap.Stop();
                Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", captureActive);
                if (rig.Error != null)
                    throw new InvalidOperationException(rig.Error);
                float deadline = Time.realtimeSinceStartup + 5;
                while (encoder.Pending > 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                yield return encoder.FinishAsync();
            }
            string folder = Path.Combine(BepInEx.Paths.ConfigPath, "capture");
            Directory.CreateDirectory(folder);
            var audio = tap.Save(Path.Combine(folder, stem + ".wav"));
            var frames = rig.Frames;
            var retained = frames.Where(f => (double)f["time"] >= 2).ToArray();
            if (retained.Length < 2)
                throw new InvalidOperationException("Capture produced fewer than two recorded frames");
            double origin = (double)retained[0]["time"];
            double seconds = (double)retained.Last()["time"] - origin;
            double fps = (retained.Length - 1) / seconds;
            lastCapture = new JObject
            {
                ["kind"] = "video-probe-counters",
                ["count"] = retained.Length,
                ["fps"] = fps,
                ["audio"] = audio,
                ["encodedBytes"] = encoder.Bytes,
                ["firstUtc"] = rig.FirstUtc.ToString("O"),
                ["lastUtc"] = rig.LastUtc.ToString("O"),
                ["width"] = QaVideoPipe.Width,
                ["height"] = QaVideoPipe.Height,
                ["frames"] = frames,
                ["rawCount"] = frames.Count,
                ["warmupCount"] = frames.Count - retained.Length,
                ["warmupSeconds"] = origin
            };
            if (fps < 50 || (double)audio["peak"] <= .001 || (int)audio["callbacks"] < 10)
                throw new InvalidOperationException(
                    "GPU/DSP probe below declared limits: fps=" + fps + ", peak=" + audio["peak"]
                );
            var pts = new List<string>();
            for (int i = 0; i < frames.Count; i++)
            {
                pts.Add("eq(N," + i + ")*" + ((double)frames[i]["time"]).ToString("F9", CultureInfo.InvariantCulture));
            }
            string timing = Path.Combine(folder, stem + "-timestamps.txt");
            string filter = "settb=1/1000000,setpts='(" + string.Join("+", pts) + ")/TB',trim=start_frame="
                + (frames.Count - retained.Length).ToString(CultureInfo.InvariantCulture)
                + ",setpts=PTS-STARTPTS";
            if (Encoding.UTF8.GetByteCount(filter) > 2 * 1024 * 1024)
                throw new InvalidOperationException("Timing metadata cap");
            File.WriteAllText(timing, filter, new UTF8Encoding(false));
            recording = new JObject
            {
                ["kind"] = stem,
                ["modelOnly"] = !context,
                ["scene"] = demoScene?.DeepClone(),
                ["frames"] = frames,
                ["count"] = retained.Length,
                ["rawCount"] = frames.Count,
                ["warmupCount"] = frames.Count - retained.Length,
                ["warmupSeconds"] = origin,
                ["fps"] = fps,
                ["encodedBytes"] = encoder.Bytes,
                ["audio"] = audio,
                ["timing"] = timing,
                ["audioDelta"] = (double)audio["firstDsp"] - (double)retained[0]["dsp"],
                ["duration"] = seconds + .017,
                ["recordedFirstDsp"] = (double)retained[0]["dsp"],
                ["source"] = audioDeck.State.id,
                ["owner"] = audioDeck.State.owner,
                ["gpu"] = SystemInfo.graphicsDeviceName,
                ["firstUtc"] = rig.FirstUtc.ToString("O"),
                ["lastUtc"] = rig.LastUtc.ToString("O"),
                ["issued"] = encoder.Issued,
                ["written"] = encoder.Written,
                ["width"] = QaVideoPipe.Width,
                ["height"] = QaVideoPipe.Height,
                ["capturePoint"] = "Actual AudioSource.OnAudioFilterRead; not listener mix"
            };
        }
        finally
        {
            rig.enabled = false;
            tap.Stop();
            Environment.SetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE", captureActive);
            if (encoder != null && encoder.Pending > 0)
                AsyncGPUReadback.WaitAllRequests();
            try
            {
                encoder?.Dispose();
                if (recording != null)
                {
                    recording["gpuPending"] = encoder.Pending;
                    recording["encoderThreadsStopped"] = encoder.ThreadsStopped;
                }
            }
            finally
            {
                foreach (var pair in layers)
                    if (pair.Key != null)
                        pair.Key.layer = pair.Value;
                bool restored = layers.All(pair => pair.Key == null || pair.Key.layer == pair.Value);
                settings.ShowVisualEffects.Value = visuals;
                EnvMan.instance.m_debugTimeOfDay = debugTime;
                EnvMan.instance.m_debugTime = day;
                Settings.ReduceBackgroundUsage = backgroundLimit;
                Application.targetFrameRate = rate;
                QualitySettings.vSyncCount = sync;
                main.cullingMask = mainMask;
                main.targetTexture = mainTarget;
                small.Release();
                Destroy(small);
                target.Release();
                Destroy(target);
                Destroy(cameraRoot);
                Destroy(lightRoot);
                Destroy(tap);
                if (recording != null)
                {
                    recording["layersRestored"] = restored;
                    recording["mainMaskRestored"] = main.cullingMask == mainMask;
                    recording["mainTargetRestored"] = main.targetTexture == mainTarget;
                    recording["mainEnabled"] = main.enabled;
                    lastCapture = recording;
                }
            }
        }
    }

    private IEnumerator VideoMux(JObject request)
    {
        double delta = (double)request["audioDelta"];
        string filter = delta >= 0
            ? "adelay=" + Math.Round(delta * 1000).ToString(CultureInfo.InvariantCulture) + ":all=1"
            : "atrim=start=" + (-delta).ToString("R", CultureInfo.InvariantCulture) + ",asetpts=PTS-STARTPTS";
        using (var encoder = new QaVideoPipe(
            (string)request["pipe"],
            false,
            new[]
                {
            "-hide_banner",
            "-v",
            "error",
            "-nostdin",
            "-i",
            (string)request["input"],
            "-i",
            (string)request["audio"],
            "-map",
            "0:v:0",
            "-map",
            "1:a:0",
            "-c:v",
            "h264_nvenc",
            "-preset",
            "p4",
            "-cq",
            "23",
            "-b:v",
            "0",
            "-bf",
            "0",
            "-pix_fmt",
            "yuv420p",
            "-fps_mode",
            "passthrough",
            "-enc_time_base",
            "1:1000000",
            "-/filter:v",
            (string)request["timing"],
            "-af",
            filter,
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            "-t",
            ((double)request["duration"]).ToString("R", CultureInfo.InvariantCulture),
            "-movflags",
            "+frag_keyframe+empty_moov",
            "-f",
            "mp4",
            "pipe:1"
                }
        ))
        {
            yield return encoder.FinishAsync();
            lastCapture = new JObject { ["kind"] = "video-mux", ["bytes"] = encoder.Bytes };
        }
    }
}

[DefaultExecutionOrder(12000)]
public sealed class QaVideoRender : MonoBehaviour
{
    public readonly JArray Frames = new JArray();
    public bool Done;
    public string Error;
    public DateTime FirstUtc, LastUtc;
    private AudioDeck deck;
    private Camera camera;
    private RenderTexture target;
    private QaVideoPipe encoder;
    private long first;
    private long nextCapture;
    private double duration;
    private Vector3 center;
    private bool context;
    internal void Begin(
        AudioDeck value,
        Camera view,
        RenderTexture texture,
        QaVideoPipe pipe,
        double seconds,
        bool contextual = false
    )
    {
        if (seconds <= 0 || seconds > 46)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        deck = value;
        camera = view;
        target = texture;
        encoder = pipe;
        duration = seconds;
        context = contextual;
        center = deck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds.center;
    }
    private void LateUpdate()
    {
        if (deck == null || Done)
            return;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        try
        {
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            if (first == 0)
            {
                first = stamp;
                FirstUtc = DateTime.UtcNow;
            }
            double time = (stamp - first) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (time >= duration)
            {
                Done = true;
                return;
            }
            if (stamp < nextCapture)
                return;
            long slot = (stamp - first) * 60 / System.Diagnostics.Stopwatch.Frequency;
            nextCapture = first + (slot + 1) * System.Diagnostics.Stopwatch.Frequency / 60;
            JObject view = null;
            if (context)
                view = QaDemoPose.Apply(deck, camera, Math.Max(0, time - 2), duration - 2);
            else
            {
                var model = deck.Visual.transform;
                float angle = (float)(Math.Max(0, time - 2) / 2 * Math.PI / 2);
                Vector3 point = model.TransformPoint(center) + model.up * .06f;
                camera.transform.position = point
                    + (model.forward * Mathf.Cos(angle) + model.right * Mathf.Sin(angle)) * 1.8f;
                camera.transform.LookAt(point, model.up);
            }
            double dsp = AudioSettings.dspTime;
            encoder.Capture(camera, target);
            LastUtc = DateTime.UtcNow;
            double renderMs = (System.Diagnostics.Stopwatch.GetTimestamp() - stamp) * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;
            Frames.Add(new JObject
            {
                ["sequence"] = Frames.Count,
                ["time"] = time,
                ["dsp"] = dsp,
                ["renderMs"] = renderMs,
                ["nativeFrameDelta"] = Time.unscaledDeltaTime,
                ["targetRate"] = Application.targetFrameRate,
                ["vsync"] = QualitySettings.vSyncCount,
                ["backgroundLimit"] = Settings.ReduceBackgroundUsage,
                ["motion"] = deck.VisualStrength,
                ["events"] = deck.EmittedWaves,
                ["playing"] = deck.Source.isPlaying,
                ["virtual"] = deck.Source.isVirtual,
                ["view"] = view
            });
        }
        catch (Exception failure)
        {
            Error = failure.ToString();
            Done = true;
        }
    }
}
