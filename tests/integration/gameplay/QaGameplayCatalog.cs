using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CatalogCapture()
    {
        yield return AwaitAudio();
        if (audioDeck.State.playing)
            audioDeck.TogglePlayback();
        var state = audioDeck.State;
        var oldPlacements = state.decorationPlacements.ToArray();
        var oldUnlocked = state.unlockedDecorationPrefabs.ToArray();
        var settings = BoomboxPlugin.Instance.Settings;
        bool movement = settings.DecorationMovement.Value, musicMovement = settings.DecorationMusicMovement.Value;
        settings.DecorationMovement.Value = settings.DecorationMusicMovement.Value = false;
        var cameraRoot = new GameObject("QA catalogue camera");
        var camera = cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.orthographic = true;
        camera.aspect = 1;
        camera.nearClipPlane = .001f;
        camera.farClipPlane = 10;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.25f, .28f, .32f);
        camera.cullingMask = 1 << 30;
        var lightRoot = new GameObject("QA catalogue light");
        var light = lightRoot.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        light.cullingMask = 1 << 30;
        light.shadows = LightShadows.None;
        var target = new RenderTexture(512, 512, 24);
        target.Create();
        var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
        var atlas = new Texture2D(1024, 2048, TextureFormat.RGB24, false);
        var entries = new JArray();
        var pages = new JArray();
        var result =
            new JObject
            {
                ["entries"] = entries,
                ["pages"] = pages,
                ["method"] =
                    "Actual current prefab placement, orthographic front/right90, fixed scale within each pair, four pairs per native pixel atlas; no generated artwork",
                ["baseTriangles"] = 17567
            };
        lastCapture = result;
        sample = result;
        int row = 0, page = 0;
        float started = Time.realtimeSinceStartup;
        var previous = RenderTexture.active;
        try
        {
            foreach (var kind in new[] { BoomboxDecorationKind.Figurine, BoomboxDecorationKind.Hanging })
                foreach (string prefab in BoomboxTrophyStorage.Catalog(kind))
                {
                    if (row == 0)
                    {
                        var empty = new Color[1024 * 2048];
                        for (int n = 0; n < empty.Length; n++)
                            empty[n] = camera.backgroundColor;
                        atlas.SetPixels(empty);
                    }
                    int slot = kind == BoomboxDecorationKind.Figurine ? 17 : 0;
                    state.decorationPlacements.Clear();
                    state.unlockedDecorationPrefabs.Clear();
                    state.unlockedDecorationPrefabs.Add(prefab);
                    state.decorationPlacements.Add(
                        new BoomboxDecorationPlacement
                        {
                            slot = slot,
                            prefab = prefab,
                            effect = "Health",
                            effectParticles = false
                        }
                    );
                    audioDeck.VisualPresenter.RefreshState(true);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var frame = audioDeck.Visual.transform;
                    var holder = frame.Find("BoomboxDecorations/DecorationSlot_" + slot);
                    Need(holder != null, "Catalogue holder missing: " + prefab);
                    var renderers = holder.GetComponentsInChildren<Renderer>(true).Where(
                        r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
                    ).ToArray();
                    Need(renderers.Length > 0, "Catalogue actual renderers missing: " + prefab);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers.Skip(1))
                        bounds.Encapsulate(renderer.bounds);
                    var layers = renderers.Select(r => r.gameObject.layer).ToArray();
                    camera.orthographicSize = Mathf.Max(.015f, bounds.extents.magnitude * 1.08f);
                    lightRoot.transform.rotation = Quaternion.LookRotation(-frame.forward - frame.up * .5f
                        - frame.right * .3f);
                    try
                    {
                        foreach (var renderer in renderers)
                            renderer.gameObject.layer = 30;
                        for (int side = 0; side < 2; side++)
                        {
                            camera.transform.position = bounds.center + (side == 0 ? frame.forward : frame.right) * 2;
                            camera.transform.LookAt(bounds.center, frame.up);
                            camera.targetTexture = target;
                            camera.Render();
                            camera.targetTexture = null;
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                            pixels.Apply();
                            atlas.SetPixels(side * 512, (3 - row) * 512, 512, 512, pixels.GetPixels());
                        }
                    }
                    finally
                    {
                        for (int n = 0; n < renderers.Length; n++)
                            if (renderers[n] != null)
                                renderers[n].gameObject.layer = layers[n];
                    }
                    var entry = new JObject
                    {
                        ["prefab"] = prefab,
                        ["kind"] = kind.ToString(),
                        ["slot"] = slot,
                        ["page"] = page,
                        ["rowFromTop"] = row,
                        ["frontColumn"] = 0,
                        ["sideColumn"] = 1,
                        ["size"] = Point(bounds.size),
                        ["renderers"] = renderers.Length,
                        ["orthographicSize"] = camera.orthographicSize
                    };
                    if (kind == BoomboxDecorationKind.Figurine)
                    {
                        try
                        {
                            entry["caseContact"] = SupportContact(frame, holder);
                        }
                        catch (Exception failure)
                        {
                            entry["contactError"] = failure.Message;
                        }
                    }
                    entries.Add(entry);
                    row++;
                    if (row == 4)
                    {
                        pages.Add(CatalogPage(atlas, page++));
                        row = 0;
                    }
                }
            if (row != 0)
                pages.Add(CatalogPage(atlas, page));
            state.decorationPlacements.Clear();
            state.decorationPlacements.AddRange(oldPlacements);
            state.unlockedDecorationPrefabs.Clear();
            state.unlockedDecorationPrefabs.AddRange(oldUnlocked);
            audioDeck.VisualPresenter.RefreshState(true);
            yield return null;
            yield return CapeAndGlow(result);
            result["coreSeconds"] = Time.realtimeSinceStartup - started;
            result["captureComplete"] = true;
            result["visualReview"] = "PENDING";
        }
        finally
        {
            state.decorationPlacements.Clear();
            state.decorationPlacements.AddRange(oldPlacements);
            state.unlockedDecorationPrefabs.Clear();
            state.unlockedDecorationPrefabs.AddRange(oldUnlocked);
            audioDeck.VisualPresenter.RefreshState(true);
            settings.DecorationMovement.Value = movement;
            settings.DecorationMusicMovement.Value = musicMovement;
            RenderTexture.active = previous;
            target.Release();
            Destroy(target);
            Destroy(pixels);
            Destroy(atlas);
            Destroy(cameraRoot);
            Destroy(lightRoot);
            lastCapture = result;
            sample = result;
        }
    }
    private static JObject CatalogPage(Texture2D atlas, int page)
    {
        atlas.Apply();
        var bytes = atlas.EncodeToPNG();
        Need(bytes.Length < 8 * 1024 * 1024, "Catalogue page size cap");
        string folder = Path.Combine(BepInEx.Paths.ConfigPath, "capture/catalog");
        Directory.CreateDirectory(folder);
        string name = "catalog-" + page.ToString("D2") + ".png";
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return new JObject
        {
            ["file"] = name,
            ["sha"] = Hash(path),
            ["bytes"] = bytes.Length,
            ["width"] = atlas.width,
            ["height"] = atlas.height
        };
    }
}
