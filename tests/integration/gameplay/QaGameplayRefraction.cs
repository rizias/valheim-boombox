using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator RefractionCapture()
    {
        var settings = BoomboxPlugin.Instance.Settings;
        var prior = settings.Effect.Variant.Value;
        var cameraObject = new GameObject("QA native refraction comparison");
        var camera = cameraObject.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.aspect = 1280f / 960;
        camera.fieldOfView = 55;
        var target = new RenderTexture(1280, 960, 24);
        target.Create();
        var oldTarget = RenderTexture.active;
        var pixels = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        Renderer ground = null, air = null;
        var result = new JObject();
        var frames = new JArray();
        result["frames"] = frames;
        result["copiedDepthMode"] = camera.depthTextureMode.ToString();
        camera.depthTextureMode |= DepthTextureMode.Depth;
        try
        {
            settings.Effect.Variant.Value = BoomboxMusicEffect.Pulse;
            yield return new WaitForSecondsRealtime(1);
            float until = Time.realtimeSinceStartup + 8;
            while (true)
            {
                Need(
                    Time.realtimeSinceStartup < until,
                    "Native refraction front did not cross the visible ground patch"
                );
                yield return new WaitForEndOfFrame();
                var all = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                ground = all.FirstOrDefault(
                    r => r.name == "Boombox music ground ripple" && r.enabled && r.gameObject.activeInHierarchy
                );
                air = all.FirstOrDefault(
                    r => r.name == "Boombox music pulse air" && r.enabled && r.gameObject.activeInHierarchy
                );
                if (ground != null && air != null)
                {
                    var ring = ground.sharedMaterial.GetVector("_Ring");
                    if (ring.z > 1 && ring.z < ground.sharedMaterial.GetVector("_GroundArea").z * .45f && ring.w > .05f)
                        break;
                }
            }
            result["ring"] = JArray.FromObject(new[] {
                ground.sharedMaterial.GetVector("_Ring").x,
                ground.sharedMaterial.GetVector("_Ring").y,
                ground.sharedMaterial.GetVector("_Ring").z,
                ground.sharedMaterial.GetVector("_Ring").w
                });
            result["groundArea"] = ground.sharedMaterial.GetVector("_GroundArea").ToString("F3");
            result["captureDepthMode"] = camera.depthTextureMode.ToString();
            var center = Player.m_localPlayer.transform.position;
            var sea = ReadPoint(demoScene["sea"]);
            camera.transform.position = center + sea * 9 + Vector3.up * 7;
            camera.transform.LookAt(center - sea * 3);
            ground.enabled = air.enabled = false;
            var off0 = RefractionPixels(camera, target, pixels);
            var off1 = RefractionPixels(camera, target, pixels);
            frames.Add(ContextPng(camera, target, "fx-refraction-off.png"));
            var rows = new JArray();
            result["cases"] = rows;
            foreach (var renderer in new[] { ground, air })
            {
                renderer.enabled = true;
                var on = RefractionPixels(camera, target, pixels);
                int changed = 0, unstable = 0;
                for (int n = 0; n < on.Length; n += 4)
                {
                    if (PixelDelta(off0[n], off1[n]) > .002f)
                    {
                        unstable++;
                        continue;
                    }
                    if (PixelDelta(off1[n], on[n]) > .02f)
                        changed++;
                }
                var row =
                    new JObject
                    {
                        ["name"] = renderer.name,
                        ["shader"] = renderer.sharedMaterial.shader.name,
                        ["supported"] = renderer.sharedMaterial.shader.isSupported,
                        ["stableChangedPixelsSampled"] = changed,
                        ["unstableControlSamples"] = unstable,
                        ["method"] =
                            "Same native frame/camera; only this refraction renderer toggled, colored light/ring unchanged; every fourth pixel on stable off/off background"
                    };
                rows.Add(row);
                frames.Add(ContextPng(camera, target, renderer == ground ? "fx-ground-on.png" : "fx-air-on.png"));
                renderer.enabled = false;
                Need(
                    renderer.sharedMaterial.shader.isSupported && changed > 10,
                    "Native refraction did not visibly alter stable scene pixels: " + row
                );
            }
            result["musicLevel"] = audioDeck.Moment.Level;
            result["musicTime"] = audioDeck.Moment.Time;
            result["passed"] = true;
        }
        finally
        {
            if (ground != null)
                ground.enabled = true;
            if (air != null)
                air.enabled = true;
            settings.Effect.Variant.Value = prior;
            RenderTexture.active = oldTarget;
            target.Release();
            Destroy(target);
            Destroy(pixels);
            Destroy(cameraObject);
            lastCapture = result;
        }
    }
    private static Color[] RefractionPixels(Camera camera, RenderTexture target, Texture2D pixels)
    {
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = null;
        RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
        pixels.Apply();
        return pixels.GetPixels();
    }
    private static float PixelDelta(
        Color left,
        Color right
    ) => Mathf.Abs(left.r - right.r) + Mathf.Abs(left.g - right.g) + Mathf.Abs(left.b - right.b);
}
