using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator SupportCapture(bool contextIconOnly = false)
    {
        yield return AwaitAudio();
        var player = Player.m_localPlayer;
        var originalPosition = player.transform.position;
        var originalRotation = player.transform.rotation;
        var settings = BoomboxPlugin.Instance.Settings;
        bool symbols = settings.ShowCassetteSymbols.Value;
        bool debugTime = EnvMan.instance.m_debugTimeOfDay;
        float day = EnvMan.instance.m_debugTime;
        var cameraRoot = new GameObject("QA native support and icon camera");
        var camera = cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.aspect = 1280f / 960;
        camera.nearClipPlane = .002f;
        camera.farClipPlane = 300;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.08f, .10f, .14f);
        var target = new RenderTexture(1280, 960, 24);
        target.Create();
        var lightRoot = new GameObject("QA native support light");
        var light = lightRoot.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        light.shadows = LightShadows.None;
        var shots = new JArray();
        var result = new JObject();
        try
        {
            settings.ShowCassetteSymbols.Value = false;
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            var frame = audioDeck.Visual.transform;
            var skull = frame.Find("BoomboxDecorations/DecorationSlot_17");
            lightRoot.transform.rotation = Quaternion.LookRotation(-frame.forward - frame.up * .5f - frame.right * .25f);
            var support = frame.GetComponentsInChildren<Renderer>(true).Where(
                r => r.enabled && r.gameObject.activeInHierarchy && r.name.StartsWith("Body_", StringComparison.Ordinal)
            ).ToArray();
            var selected = skull.GetComponentsInChildren<Renderer>(true).Where(
                r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
            ).Concat(
                support
            ).ToArray();
            var skullRenderers = skull.GetComponentsInChildren<Renderer>(true).Where(
                r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
            ).ToArray();
            var bounds = skullRenderers[0].bounds;
            foreach (var r in skullRenderers.Skip(1))
                bounds.Encapsulate(r.bounds);
            bounds.Encapsulate(skull.position - frame.up * .02f);
            yield return null;
            if (!contextIconOnly)
            {
                var gate = cameraRoot.AddComponent<QaGeometryGate>();
                bool done = false;
                gate.Configure(
                audioDeck,
                "any",
                () =>
                    {
                        result["contact"] = SupportContact(frame, skull);
                        foreach (string view in new[] { "front", "side" })
                            shots.Add(
                            Macro(
                                camera,
                                target,
                                "skull-support",
                                "actual",
                                view,
                                frame,
                                selected,
                                bounds,
                                .5f,
                                22f
                            )
                        );
                        done = true;
                    }
                );
                while (!done)
                {
                    if (gate.Error != null)
                        throw new InvalidOperationException(gate.Error);
                    yield return null;
                }
                gate.enabled = false;
            }
            result["shots"] = shots;
            result["stage"] = "support-contact-complete";
            lastCapture = result;
            var open = FindNativeShore(originalPosition, out var sea);
            if (!player.TeleportTo(open, Quaternion.LookRotation(sea), true))
                throw new InvalidOperationException("Native shoreline teleport rejected");
            float limit = Time.realtimeSinceStartup + 60;
            while (player.IsTeleporting())
            {
                if (Time.realtimeSinceStartup > limit)
                    throw new InvalidOperationException("Shoreline loading timeout");
                yield return null;
            }
            yield return new WaitForSecondsRealtime(4);
            var candidates = new[] { Vector3.zero, Vector3.right * 8, Vector3.left * 8, Vector3.forward * 8, Vector3.back * 8 };
            Vector3 landing = player.transform.position;
            bool clear = false;
            foreach (var offset in candidates)
            {
                var p = landing + offset;
                if (!ZoneSystem.instance.GetGroundHeight(p, out float height))
                    continue;
                p.y = height + .1f;
                if (Physics.OverlapSphere(p, 18, LayerMask.GetMask("piece"), QueryTriggerInteraction.Ignore).Any(
                    c => c.GetComponentInParent<Piece>()
                ))
                    continue;
                if (Physics.CheckSphere(
                    p + Vector3.up * 1.5f,
                    3f,
                    LayerMask.GetMask("Default", "static_solid"),
                    QueryTriggerInteraction.Ignore
                ))
                    continue;
                landing = p;
                clear = true;
                break;
            }
            if (!clear)
                throw new InvalidOperationException("No actual open shoreline without obstacles");
            if (Vector3.Distance(landing, player.transform.position) > 1)
            {
                Need(
                    player.TeleportTo(landing, Quaternion.LookRotation(sea), true),
                    "Support capture teleport refused"
                );
                while (player.IsTeleporting())
                    yield return null;
                yield return new WaitForSecondsRealtime(2);
            }
            camera.CopyFrom(Camera.main);
            camera.enabled = false;
            camera.aspect = 1280f / 960;
            camera.fieldOfView = 52f;
            var body = audioDeck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
            Vector3 center = frame.TransformPoint(body.center);
            camera.transform.position = center + (frame.forward + frame.right * .4f).normalized * 1.1f
                + Vector3.up * .04f;
            camera.transform.LookAt(center, frame.up);
            shots.Add(ContextPng(camera, target, "daylight-context.png"));
            var decorations = frame.Find("BoomboxDecorations");
            var all = frame.GetComponentsInChildren<Renderer>(true).Where(
                r => r.enabled
                    && r.gameObject.activeInHierarchy
                    && !(r is ParticleSystemRenderer)
                    &&
                    !r.transform.IsChildOf(decorations)
                    && !r.name.StartsWith("NowPlaying", StringComparison.Ordinal)
                    && !r.name.StartsWith("TrackProgress", StringComparison.Ordinal)
            ).ToArray();
            result["icon"] = NativeIcon(camera, frame, body, all);
            result["shots"] = shots;
            result["shorePosition"] = Point(player.transform.position);
            result["dayOnly"] = true;
            result["weatherChanged"] = false;
            result["source"] = audioDeck.State.id;
            result["iconApproval"] = "PENDING actual render owner approval";
            result["productGeometryChanged"] = false;
            lastCapture = result;
        }
        finally
        {
            settings.ShowCassetteSymbols.Value = symbols;
            EnvMan.instance.m_debugTimeOfDay = debugTime;
            EnvMan.instance.m_debugTime = day;
            target.Release();
            Destroy(target);
            Destroy(cameraRoot);
            Destroy(lightRoot);
            Need(player.TeleportTo(originalPosition, originalRotation, true), "Support position restore refused");
        }
        float restoreDeadline = Time.realtimeSinceStartup + 4;
        while (player.IsTeleporting())
        {
            Need(Time.realtimeSinceStartup < restoreDeadline, "Support position restore did not settle");
            yield return null;
        }
    }

    private static Vector3 FindNativeShore(Vector3 origin, out Vector3 direction)
    {
        float water = ZoneSystem.instance.m_waterLevel;
        int meadows = 0;
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        for (int radius = 100; radius <= 10000; radius += 100)
            for (int angle = 0; angle < 72; angle++)
            {
                var p = origin
                    + new Vector3(Mathf.Sin(angle * Mathf.PI / 36), 0, Mathf.Cos(angle * Mathf.PI / 36)) * radius;
                if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Meadows)
                    continue;
                meadows++;
                float y = WorldGenerator.instance.GetHeight(p);
                minimum = Mathf.Min(minimum, y);
                maximum = Mathf.Max(maximum, y);
                if (y < water + 2 || y > water + 40)
                    continue;
                if (new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }.Any(
                    d => Math.Abs(WorldGenerator.instance.GetHeight(p + d * 6) - y) > 1.4f
                ))
                    continue;
                for (int look = 0; look < 16; look++)
                {
                    direction = Quaternion.AngleAxis(look * 22.5f, Vector3.up) * Vector3.forward;
                    if (WorldGenerator.instance.GetHeight(p + direction * 60) < water
                    && WorldGenerator.instance.GetHeight(p + direction * 100) < water
                    && RefineNativeShore(p, direction, out var coast))
                        return coast;
                }
            }
        direction = Vector3.zero;
        throw new InvalidOperationException(
            "Actual flat meadow unavailable: candidates=" + meadows + ", heights=" + minimum + ".." + maximum
                + ", water="
                + water
        );
    }

    private JObject SupportContact(Transform frame, Transform skull)
    {
        var filters = frame.GetComponentsInChildren<MeshFilter>(true).Where(
            f => f.name.StartsWith("Body_", StringComparison.Ordinal) && f.gameObject.activeInHierarchy
        ).ToArray();
        var colliders = new List<MeshCollider>();
        var type = typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxHeadPlacement", true);
        var get = type.GetMethod("CollisionMesh", BindingFlags.NonPublic | BindingFlags.Static);
        var pedestal = skull.Find("Base");
        var foot = pedestal.TransformPoint(Vector3.down * .5f);
        float radius = pedestal.TransformVector(Vector3.right * .5f).magnitude;
        var gaps = new List<float>();
        try
        {
            foreach (var filter in filters)
            {
                var obj = new GameObject("QA actual support collision");
                obj.layer = 31;
                obj.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                obj.transform.localScale = filter.transform.lossyScale;
                var collider = obj.AddComponent<MeshCollider>();
                collider.sharedMesh = (Mesh)get.Invoke(null, new object[] { filter });
                colliders.Add(collider);
            }
            Physics.SyncTransforms();
            for (int n = 0; n < 13; n++)
            {
                float angle = (n - 1) * Mathf.PI / 6;
                var point = foot + (n == 0
                    ? Vector3.zero
                    : skull.right * Mathf.Cos(angle) * radius * .8f + skull.forward * Mathf.Sin(angle) * radius * .8f);
                float distance = float.PositiveInfinity;
                foreach (var collider in colliders)
                    if (collider.Raycast(new Ray(point + skull.up * .05f, -skull.up), out var hit, .1f))
                        distance = Mathf.Min(distance, hit.distance);
                if (float.IsPositiveInfinity(distance))
                    throw new InvalidOperationException("Pedestal footprint has no actual case surface");
                gaps.Add(distance - .05f);
            }
            return new JObject
            {
                ["samples"] = gaps.Count,
                ["gapsMetres"] = new JArray(gaps),
                ["minimumGap"] = gaps.Min(),
                ["maximumGap"] = gaps.Max(),
                ["footRadius"] = radius,
                ["method"] =
                    "13 footprint rays against actual case triangle colliders; pedestal native mesh foot, no bounds contact oracle"
            };
        }
        finally
        {
            foreach (var collider in colliders)
            {
                collider.gameObject.SetActive(false);
                Destroy(collider.gameObject);
            }
        }
    }

    private JObject NativeIcon(Camera camera, Transform frame, Bounds body, Renderer[] selected)
    {
        var layers = selected.Select(r => r.gameObject.layer).ToArray();
        var rt = new RenderTexture(256, 256, 24);
        rt.Create();
        var old = RenderTexture.active;
        var pixels = new Texture2D(256, 256, TextureFormat.RGB24, false);
        try
        {
            foreach (var r in selected)
                r.gameObject.layer = 30;
            camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .07f);
            camera.aspect = 1;
            camera.orthographic = true;
            var center = frame.TransformPoint(body.center);
            camera.transform.position = center + (frame.forward + frame.right).normalized;
            camera.transform.LookAt(center, frame.up);
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = null;
            var projected = new List<Vector3>();
            foreach (var filter in selected.Select(r => r.GetComponent<MeshFilter>()).Where(
                f => f != null && f.sharedMesh != null && f.sharedMesh.isReadable
            ))
                foreach (int index in filter.sharedMesh.triangles.Distinct())
                    projected.Add(
                        camera.transform.InverseTransformPoint(
                            filter.transform.TransformPoint(filter.sharedMesh.vertices[index])
                        )
                    );
            float width = projected.Max(v => v.x) - projected.Min(v => v.x), height = projected.Max(v => v.y) - projected.Min(v => v.y);
            camera.orthographicSize = Mathf.Max(width * 256f / 220f, height * 256f / 220f) * .5f;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            pixels.Apply();
            string folder = Path.Combine(BepInEx.Paths.ConfigPath, "capture/support");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "icon-candidate-256.png"), pixels.EncodeToPNG());
            var small = new Texture2D(128, 128, TextureFormat.RGB24, false);
            try
            {
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                        small.SetPixel(x, y, pixels.GetPixel(x * 2, y * 2));
                small.Apply();
                File.WriteAllBytes(Path.Combine(folder, "icon-preview-128.png"), small.EncodeToPNG());
            }
            finally
            {
                Destroy(small);
            }
            return new JObject
            {
                ["nativeRender"] = true,
                ["trackTextHiddenOnlyInCapture"] = true,
                ["bodyTris"] = 17567,
                ["quarterDegrees"] = 45,
                ["projectedBodyWidthPixels"] = width / (2 * camera.orthographicSize) * 256,
                ["medium"] = "Unmodified current Unity model/meshes/materials/runes, isolated clean body"
            };
        }
        finally
        {
            for (int i = 0; i < selected.Length; i++)
                selected[i].gameObject.layer = layers[i];
            RenderTexture.active = old;
            rt.Release();
            Destroy(rt);
            Destroy(pixels);
        }
    }

    private JObject ContextPng(Camera camera, RenderTexture target, string name)
    {
        var old = RenderTexture.active;
        var pixels = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
            pixels.Apply();
            var bytes = pixels.EncodeToPNG();
            if (bytes.Length > 4 * 1024 * 1024)
                throw new InvalidOperationException("Context PNG cap");
            string folder = Path.Combine(BepInEx.Paths.ConfigPath, "capture/support");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
            return new JObject { ["file"] = name, ["bytes"] = bytes.Length, ["actualScene"] = true };
        }
        finally
        {
            RenderTexture.active = old;
            Destroy(pixels);
        }
    }
}
