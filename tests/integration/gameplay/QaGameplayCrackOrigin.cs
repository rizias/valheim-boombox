using System.Collections;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CrackOriginCapture()
    {
        yield return AwaitAudio();
        var settings = BoomboxPlugin.Instance.Settings;
        var old = settings.Effect.Variant.Value;
        bool visible = settings.ShowVisualEffects.Value, damage = settings.Effect.CreatureDamage.Value;
        bool day = EnvMan.instance.m_debugTimeOfDay;
        float hour = EnvMan.instance.m_debugTime;
        var frames = new JArray();
        var proof = new JObject { ["candidate"] = productSha, ["frames"] = frames };
        lastCapture = proof;
        var cameraObject = new GameObject("QA crack origin camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.aspect = 1280f / 960;
        camera.fieldOfView = 55;
        var target = new RenderTexture(1280, 960, 24);
        target.Create();
        float started = Time.realtimeSinceStartup;
        try
        {
            UiParkWindow(true);
            BoomboxPlugin.Instance.CloseMenu();
            InventoryGui.instance.Hide();
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            settings.Effect.CreatureDamage.Value = false;
            settings.ShowVisualEffects.Value = true;
            settings.Effect.Variant.Value = BoomboxMusicEffect.FixedCracks;
            var runner = audioDeck.GetComponent<MusicEffectRunner>();
            IList cracks = null;
            float deadline = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var view = AccessTools.Field(typeof(MusicEffectRunner), "view").GetValue(runner);
                if (view == null || view.GetType().Name != "MusicFixedCracksView")
                    continue;
                cracks = (IList)AccessTools.Field(view.GetType(), "cracks").GetValue(view);
                if (cracks.Count > 0)
                    break;
            }
            Need(cracks != null && cracks.Count > 0, "No real audio-triggered cracks on native terrain");
            var center = Player.m_localPlayer.transform.position;
            var roots = new JArray();
            foreach (var crack in cracks)
            {
                var lines = (IList)AccessTools.Field(crack.GetType(), "Lines").GetValue(crack);
                if (lines.Count == 0)
                    continue;
                var point = ((IList)lines[0])[0];
                float x = (float)AccessTools.Field(point.GetType(), "X")
                    .GetValue(point),
                    z = (float)AccessTools.Field(point.GetType(), "Z")
                    .GetValue(point);
                float offset = Vector2.Distance(new Vector2(x, z), new Vector2(center.x, center.z));
                roots.Add(offset);
                Need(offset < .01f, "Native crack root displaced from stationary player");
            }
            Need(roots.Count > 0, "Ground clipping removed all main lines");
            proof["rootOffsetsMeters"] = roots;
            camera.transform.position = center + new Vector3(0, 7, -4);
            camera.transform.LookAt(center);
            yield return new WaitForEndOfFrame();
            var frame = ContextPng(camera, target, "crack-origin.png");
            frame["file"] = Path.Combine(BepInEx.Paths.ConfigPath, "capture/support/crack-origin.png");
            frames.Add(frame);
            proof["seconds"] = Time.realtimeSinceStartup - started;
            proof["passed"] = true;
        }
        finally
        {
            settings.Effect.Variant.Value = old;
            settings.ShowVisualEffects.Value = visible;
            settings.Effect.CreatureDamage.Value = damage;
            EnvMan.instance.m_debugTimeOfDay = day;
            EnvMan.instance.m_debugTime = hour;
            target.Release();
            Destroy(target);
            Destroy(cameraObject);
            UiParkWindow();
        }
    }
}
