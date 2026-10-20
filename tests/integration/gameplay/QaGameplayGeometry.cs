using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator GeometryCapture(JObject request)
    {
        yield return AwaitAudio();
        var settings = BoomboxPlugin.Instance.Settings;
        settings.DecorationMovement.Value =
            settings.DecorationMusicMovement.Value =
            settings.DecorationParticles.Value =
            false;
        var state = audioDeck.State;
        if (!state.unlockedDecorationSlots.Contains(15))
            state.unlockedDecorationSlots.Add(15);
        state.decorationPlacements.RemoveAll(p => p.slot == 15);
        state.decorationPlacements.Add(
            new BoomboxDecorationPlacement
            {
                slot = 15,
                prefab = BoomboxItemData.DevicePrefab,
                effect = "Health",
                effectParticles = false
            }
        );
        audioDeck.VisualPresenter.RefreshState(true);
        yield return null;
        var cameraRoot = new GameObject("QA MODEL03 native camera");
        var camera = cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.aspect = 1280f / 960;
        camera.nearClipPlane = .002f;
        camera.farClipPlane = 100;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.11f, .13f, .16f);
        var target = new RenderTexture(1280, 960, 24);
        target.Create();
        var gate = cameraRoot.AddComponent<QaGeometryGate>();
        var shots = new JArray();
        var numbers = new JArray();
        try
        {
            foreach (string phase in new[] { "peak", "rest" })
            {
                bool captured = false;
                gate.Configure(
                audioDeck,
                phase,
                () =>
                    {
                        var visual = audioDeck.Visual.transform;
                        var decorations = visual.Find("BoomboxDecorations");
                        var items = new[] {
                        (name: "main", frame: visual),
                        (name: "mini12", frame: decorations.Find("DecorationSlot_12/PendantPivot/NativeDecoration")
                                .GetChild(0)),
                        (name: "mini15", frame: decorations.Find("DecorationSlot_15/PendantPivot/NativeDecoration")
                                .GetChild(0))
                        };
                        foreach (var item in items)
                        {
                            var membranes = item.frame.GetComponentsInChildren<Transform>(true).Where(
                            t => t.name == "MembraneLeft" || t.name == "MembraneRight"
                        ).Where(
                            t => item.name != "main" || !t.IsChildOf(decorations)
                        ).ToArray();
                            var selected = item.frame.GetComponentsInChildren<Renderer>(true).Where(
                            r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
                                    && (item.name != "main" || !r.transform.IsChildOf(decorations))
                        ).ToArray();
                            var bounds = selected[0].bounds;
                            foreach (var renderer in selected.Skip(1))
                                bounds.Encapsulate(renderer.bounds);
                            foreach (var membrane in membranes)
                                numbers.Add(SpeakerNumbers(item.name, item.frame, membrane, phase));
                            foreach (string view in new[] { "front", "side" })
                                shots.Add(Macro(camera, target, item.name, phase, view, item.frame, selected, bounds,
                                    item.name == "main" ? .9f : .5f, item.name == "main" ? 54f : 14f));
                        }
                        if (phase == "rest")
                        {
                            var skull = decorations.Find("DecorationSlot_17");
                            var selected = skull.GetComponentsInChildren<Renderer>(true).Where(
                            r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
                        ).ToArray();
                            var bounds = selected[0].bounds;
                            foreach (var renderer in selected.Skip(1))
                                bounds.Encapsulate(renderer.bounds);
                            foreach (string view in new[] { "front", "side" })
                                shots.Add(
                                Macro(camera, target, "skull17", phase, view, visual, selected, bounds, .5f, 20f)
                            );
                            numbers.Add(SkullNumbers(skull));
                        }
                        captured = true;
                    }
                );
                float deadline = Time.realtimeSinceStartup + 20;
                while (!captured)
                {
                    if (gate.Error != null)
                        throw new InvalidOperationException(gate.Error);
                    if (Time.realtimeSinceStartup > deadline)
                        throw new InvalidOperationException(
                            "No actual PCM " + phase + " extrema; source=" + audioDeck.Source.time + " strength="
                                + audioDeck.VisualStrength
                        );
                    yield return null;
                }
            }
            lastCapture = new JObject
            {
                ["kind"] = "MODEL03-macro",
                ["shots"] = shots,
                ["numbers"] = numbers,
                ["source"] = state.id,
                ["owner"] = state.owner,
                ["itemPrefab"] = audioDeck.Item.m_dropPrefab.name,
                ["quality"] = audioDeck.Item.m_quality,
                ["cassette"] = state.insertedCassette.prefab,
                ["placements"] = new JArray(
                    state.decorationPlacements.Select(p => new JObject { ["slot"] = p.slot, ["prefab"] = p.prefab })
                ),
                ["musicDriven"] = true,
                ["sourceTrack"] = state.TrackId,
                ["motionMultiplier"] = settings.MembraneMotion.Value,
                ["baseTris"] = 17567,
                ["visualAcceptance"] = "PENDING pixels/contact review"
            };
        }
        finally
        {
            gate.enabled = false;
            target.Release();
            Destroy(target);
            Destroy(cameraRoot);
        }
    }

    private JObject Macro(Camera camera, RenderTexture target, string name, string phase, string view,
    Transform frame, Renderer[] selected, Bounds bounds, float distance, float fov)
    {
        var layers = selected.Select(r => r.gameObject.layer).ToArray();
        var previous = RenderTexture.active;
        var pixels = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        string file = name + "-" + phase + "-" + view + ".png";
        try
        {
            foreach (var renderer in selected)
                renderer.gameObject.layer = 30;
            camera.cullingMask = 1 << 30;
            camera.fieldOfView = fov;
            var direction = view == "front" ? frame.forward : frame.right;
            camera.transform.position = bounds.center + direction.normalized * distance;
            camera.transform.LookAt(bounds.center, frame.up);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
            pixels.Apply();
            var bytes = pixels.EncodeToPNG();
            if (bytes.Length > 2 * 1024 * 1024)
                throw new InvalidOperationException("Single macro cap");
            string folder = Path.Combine(BepInEx.Paths.ConfigPath, "capture/model03");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, file), bytes);
            return new JObject
            {
                ["file"] = file,
                ["subject"] = name,
                ["phase"] = phase,
                ["view"] = view,
                ["distance"] = distance,
                ["fov"] = fov,
                ["frame"] = Time.frameCount,
                ["strength"] = audioDeck.VisualStrength,
                ["sampleFrame"] = audioDeck.Source.timeSamples,
                ["dsp"] = AudioSettings.dspTime,
                ["utc"] = DateTime.UtcNow.ToString("O"),
                ["bytes"] = bytes.Length,
                ["direction"] = JArray.FromObject(new[] { direction.x, direction.y, direction.z })
            };
        }
        finally
        {
            for (int i = 0; i < selected.Length; i++)
                if (selected[i])
                    selected[i].gameObject.layer = layers[i];
            RenderTexture.active = previous;
            Destroy(pixels);
        }
    }

    private JObject SpeakerNumbers(string subject, Transform frame, Transform membrane, string phase)
    {
        var suspension = frame.GetComponent<BoomboxSpeakerSuspension>();
        var cones = (IEnumerable)AccessTools.Field(typeof(BoomboxSpeakerSuspension), "cones").GetValue(suspension);
        foreach (object cone in cones)
        {
            var type = cone.GetType();
            if ((Transform)AccessTools.Field(type, "Membrane").GetValue(cone) != membrane)
                continue;
            var fixedCenter = (Vector3)AccessTools.Field(type, "FixedCenter").GetValue(cone);
            var localRim = (Vector3)AccessTools.Field(type, "LocalRim").GetValue(cone);
            var moving = (Vector3)AccessTools.Method(typeof(BoomboxSpeakerSuspension), "PointInFrame").Invoke(
                null,
                new object[] { frame, membrane, localRim }
            );
            var mesh = (Mesh)AccessTools.Field(type, "Mesh").GetValue(cone);
            var vertices = mesh.vertices;
            var delta = moving - fixedCenter;
            float scale = membrane.localScale.x / (float)AccessTools.Field(type, "Scale").GetValue(cone);
            return new JObject
            {
                ["subject"] = subject,
                ["phase"] = phase,
                ["cone"] = membrane.name,
                ["delta"] = JArray.FromObject(new[] { delta.x, delta.y, delta.z }),
                ["scaleRatio"] = scale,
                ["fixedEdgeError"] = vertices.Take(40).Max(v => Math.Abs(v.z - fixedCenter.z)),
                ["movingEdgeError"] = vertices.Skip(160).Max(v => Math.Abs(v.z - moving.z)),
                ["surroundTris"] = mesh.triangles.Length / 3,
                ["sourceMesh"] = membrane.GetComponent<MeshFilter>().sharedMesh.name
            };
        }
        throw new InvalidOperationException("Actual surround missing for " + subject + "/" + membrane.name);
    }

    private static JObject SkullNumbers(Transform holder)
    {
        var stem = holder.Find("Stem");
        var pedestal = holder.Find("Base");
        var head = holder.Find("HeadPivot/Head");
        var headType = typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxHeadPlacement", true);
        var getMesh = headType.GetMethod("CollisionMesh", BindingFlags.Static | BindingFlags.NonPublic);
        var headPoints = head.GetComponentsInChildren<MeshFilter>(true).SelectMany(f =>
        {
            var mesh = (Mesh)getMesh.Invoke(null, new object[] { f });
            var matrix = holder.worldToLocalMatrix * f.transform.localToWorldMatrix;
            return mesh.triangles.Distinct().Select(i => matrix.MultiplyPoint3x4(mesh.vertices[i]));
        }).ToArray();
        float stemTop = holder.InverseTransformPoint(stem.GetComponent<Renderer>().bounds.max).y;
        float baseTop = holder.InverseTransformPoint(pedestal.GetComponent<Renderer>().bounds.max).y;
        float stemBottom = holder.InverseTransformPoint(stem.GetComponent<Renderer>().bounds.min).y;
        return new JObject
        {
            ["subject"] = "skull17",
            ["headMinimumY"] = headPoints.Min(p => p.y),
            ["stemTopY"] = stemTop,
            ["baseTopY"] = baseTop,
            ["stemBottomY"] = stemBottom,
            ["meshNames"] = new JArray(head.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh.name)),
            ["note"] = "bounds/vertex extrema are diagnostics; pole/head surface contact requires macro/raycast review"
        };
    }
}

[DefaultExecutionOrder(12001)]
public sealed class QaGeometryGate : MonoBehaviour
{
    private AudioDeck deck;
    private string phase;
    private Action capture;
    public string Error;
    internal void Configure(AudioDeck value, string desired, Action work)
    {
        deck = value;
        phase = desired;
        capture = work;
        Error = null;
    }
    private void LateUpdate()
    {
        if (capture == null)
            return;
        if (!deck.IsActivePlayback || phase == "peak" && deck.VisualStrength < .9999f
        || phase == "rest" && deck.VisualStrength > .00001f)
            return;
        var work = capture;
        capture = null;
        try
        {
            work();
        }
        catch (Exception failure)
        {
            Error = failure.ToString();
        }
    }
}
