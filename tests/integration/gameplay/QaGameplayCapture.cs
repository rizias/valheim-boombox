using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private JObject lastCapture;

    private bool CaptureOperation(string operation, JObject request)
    {
        if (operation == "mount-capture")
        {
            busy = true;
            StartCoroutine(GuardAudio(MountCapture()));
            return true;
        }
        if (operation == "usability-capture")
        {
            busy = true;
            StartCoroutine(GuardAudio(UsabilityCapture()));
            return true;
        }
        if (operation == "menu-latency")
        {
            busy = true;
            StartCoroutine(GuardAudio(MenuLatencyCapture((bool?)request["recreate"] ?? false)));
            return true;
        }
        if (operation == "playlist-validation")
        {
            busy = true;
            StartCoroutine(GuardAudio(PlaylistValidation()));
            return true;
        }
        if (operation == "catalog-case")
        {
            busy = true;
            StartCoroutine(GuardAudio(RemainingCase((string)request["caseId"])));
            return true;
        }
        if (operation == "hud-fixes")
        {
            busy = true;
            StartCoroutine(GuardAudio(HudFixesCapture()));
            return true;
        }
        if (operation == "icon-preview")
        {
            busy = true;
            StartCoroutine(GuardAudio(IconPreview()));
            return true;
        }
        if (operation == "gameplay-settings")
        {
            busy = true;
            StartCoroutine(GuardAudio(GameplaySettingsCapture()));
            return true;
        }
        if (operation == "crack-origin")
        {
            busy = true;
            StartCoroutine(GuardAudio(CrackOriginCapture()));
            return true;
        }
        if (operation == "effect-preview")
        {
            busy = true;
            StartCoroutine(GuardAudio(EffectPreviewCapture()));
            return true;
        }
        if (operation == "visibility-buffs")
        {
            busy = true;
            StartCoroutine(GuardAudio(VisibilityCapture()));
            return true;
        }
        if (operation == "upgrade-cycle")
        {
            busy = true;
            StartCoroutine(GuardAudio(UpgradeCycle()));
            return true;
        }
        if (operation == "menu-park" || operation == "menu-resume")
        {
            ParkMenu(operation == "menu-park");
            return true;
        }
        if (operation == "menu-inspect")
        {
            lastCapture = new JObject
            {
                ["cutscene"] = Player.m_localPlayer?.InCutscene(),
                ["intro"] = Player.m_localPlayer?.InIntro(),
                ["menuOpen"] = BoomboxPlugin.Instance.MenuOpen,
                ["timeScale"] = Time.timeScale,
                ["valkyrie"] = Valkyrie.m_instance != null
            };
            return true;
        }
        if (operation == "demo-setup" || operation == "demo-clear")
        {
            busy = true;
            StartCoroutine(GuardAudio(operation == "demo-setup" ? DemoSetup(request) : DemoClear()));
            return true;
        }
        if (operation == "demo-rewind")
        {
            busy = true;
            StartCoroutine(GuardAudio(DemoRewind()));
            return true;
        }
        if (operation == "video-demo-probe" || operation == "video-demo")
        {
            busy = true;
            StartCoroutine(GuardAudio(VideoProbe(request)));
            return true;
        }
        if (operation == "video-demo-probe-mux" || operation == "video-demo-mux")
        {
            busy = true;
            StartCoroutine(GuardAudio(VideoMux(request)));
            return true;
        }
        if (operation == "fx-refraction")
        {
            busy = true;
            StartCoroutine(GuardAudio(RefractionCapture()));
            return true;
        }
        if (operation == "catalog-capture")
        {
            busy = true;
            StartCoroutine(GuardAudio(CatalogCapture()));
            return true;
        }
        if (operation == "geometry-capture")
        {
            busy = true;
            StartCoroutine(GuardAudio(GeometryCapture(request)));
            return true;
        }
        if (operation == "support-capture")
        {
            busy = true;
            StartCoroutine(GuardAudio(SupportCapture((bool?)request["contextIconOnly"] ?? false)));
            return true;
        }
        if (operation == "video-probe" || operation == "video-mux")
        {
            busy = true;
            StartCoroutine(GuardAudio(operation == "video-probe" ? VideoProbe(request) : VideoMux(request)));
            return true;
        }
        if (operation == "visual-setup")
        {
            var settings = BoomboxPlugin.Instance.Settings;
            settings.ShowVisualEffects.Value =
                settings.ShowSpeakerEffects.Value =
                settings.ShowBodyGlow.Value =
                settings.ShowSigurdGlow.Value =
                true;
            settings.DecorationMovement.Value =
                settings.DecorationMusicMovement.Value =
                settings.DecorationParticles.Value =
                false;
            settings.ShowOthersWaves.Value = true;
            settings.Effect.CreatureDamage.Value = false;
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            var deck = decks[(string)request["key"]];
            if (role == "client-a")
            {
                deck.State.decorationPlacements.Clear();
                deck.State.unlockedDecorationPrefabs = new List<string> {
                    "TrophySkeleton",
                    "MushroomYellow",
                    BoomboxItemData.DevicePrefab
                };
                deck.State.decorationPlacements.Add(
                    new BoomboxDecorationPlacement
                    {
                        slot = 17,
                        prefab = "TrophySkeleton",
                        effect = "Damage",
                        effectParticles = false
                    }
                );
                deck.State.decorationPlacements.Add(
                    new BoomboxDecorationPlacement
                    {
                        slot = 0,
                        prefab = "MushroomYellow",
                        effect = "Stamina",
                        effectParticles = false
                    }
                );
                deck.State.decorationPlacements.Add(
                    new BoomboxDecorationPlacement
                    {
                        slot = 12,
                        prefab = BoomboxItemData.DevicePrefab,
                        effect = "Health",
                        effectParticles = false
                    }
                );
                BoomboxPlugin.Instance.Save();
                deck.VisualPresenter.RefreshState(true);
            }
            return true;
        }
        if (operation != "capture")
            return false;
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("Capture requires a real graphics device");
        string kind = (string)request["view"];
        if (kind != "front" && kind != "quarter" && kind != "side" && kind != "context")
            throw new InvalidOperationException("Unknown fixed capture angle");
        bool remote = (bool?)request["remote"] ?? false;
        GameObject model;
        GameObject subject;
        string sourceId;
        BoomboxRemoteView remoteView = null;
        if (remote)
        {
            sourceId = (string)request["source"];
            remoteView = BoomboxRemoteNetwork.Current.Views.Single(v => v.SourceId == sourceId);
            model = remoteView.Model;
            subject = remoteView.gameObject;
        }
        else
        {
            var deck = decks[(string)request["key"]];
            sourceId = deck.State.id;
            model = deck.Visual;
            subject = deck.gameObject;
        }
        if (model == null || Camera.main == null)
            throw new InvalidOperationException("Actual model or scene camera is missing");
        var cameraObject = new GameObject("Boombox QA native capture camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        var lightObject = new GameObject("Boombox QA capture light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        light.color = Color.white;
        light.shadows = LightShadows.None;
        var target = new RenderTexture(1280, 960, 24);
        var pixels = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var layers = new List<KeyValuePair<GameObject, int>>();
        string name = (remote ? "remote-" : "own-") + kind + ".png";
        try
        {
            var modelState = model.GetComponent<VikingBoomboxVisualState>();
            Vector3 center = model.transform.TransformPoint(modelState.BodyBounds.center) + model.transform.up * .06f;
            Vector3 direction = kind == "side"
                ? model.transform.right
                : kind == "quarter" || kind == "context"
                    ? (model.transform.forward + model.transform.right * .65f).normalized
                    : model.transform.forward;
            camera.fieldOfView = 34f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 300f;
            camera.transform.position = center + direction * 1.8f;
            camera.transform.LookAt(center, model.transform.up);
            lightObject.transform.rotation = Quaternion.LookRotation(
                -model.transform.forward + model.transform.up * -.5f + model.transform.right * -.25f
            );
            bool isolated = kind != "context";
            if (isolated)
            {
                foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                {
                    layers.Add(new KeyValuePair<GameObject, int>(part.gameObject, part.gameObject.layer));
                    part.gameObject.layer = 30;
                }
                camera.cullingMask = 1 << 30;
                light.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .105f, .14f, 1f);
            }
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
            pixels.Apply();
            var bytes = pixels.EncodeToPNG();
            if (bytes.Length > 16 * 1024 * 1024)
                throw new InvalidOperationException("Single capture exceeds declared bound");
            string folder = Path.Combine(BepInEx.Paths.ConfigPath, "capture");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
            var appearance = remote
                ? BoomboxRemoteNetwork.Current.States.Single(s => s.Id == sourceId).Appearance
                : decks[(string)request["key"]].State;
            lastCapture = new JObject
            {
                ["file"] = name,
                ["width"] = 1280,
                ["height"] = 960,
                ["bytes"] = bytes.Length,
                ["graphics"] = SystemInfo.graphicsDeviceType.ToString(),
                ["device"] = SystemInfo.graphicsDeviceName,
                ["modelOnly"] = isolated,
                ["source"] = sourceId,
                ["owner"] = appearance.owner,
                ["viewer"] = Player.m_localPlayer.GetPlayerID(),
                ["remote"] = remote,
                ["foreignAudioSources"] = remote ? subject.GetComponentsInChildren<AudioSource>(true).Length : 0,
                ["foreignAudioDecks"] = remote ? subject.GetComponentsInChildren<AudioDeck>(true).Length : 0,
                ["position"] = Point(model.transform.position),
                ["camera"] = Point(camera.transform.position),
                ["fieldOfView"] = camera.fieldOfView,
                ["appearance"] = new JArray(
                    appearance.decorationPlacements.Select(
                        d => new JObject { ["slot"] = d.slot, ["prefab"] = d.prefab, ["effect"] = d.effect }
                    )
                ),
                ["actualMeshTriangles"] = model.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh
                    != null).Sum(
                    m => m.sharedMesh.triangles.Length / 3
                ),
                ["baseModelTriangles"] = 17567,
                ["revision"] = remoteView?.PresentedRevision ?? 0L,
                ["utc"] = DateTime.UtcNow.ToString("O")
            };
        }
        finally
        {
            foreach (var layer in layers)
                if (layer.Key != null)
                    layer.Key.layer = layer.Value;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(pixels);
            UnityEngine.Object.Destroy(cameraObject);
            UnityEngine.Object.Destroy(lightObject);
        }
        return true;
    }
}
