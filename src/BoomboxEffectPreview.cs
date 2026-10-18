using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxEffectPreview : IDisposable
    {
        private const float Radius = 6f;
        private readonly GameObject host;
        private readonly GameObject model;
        private readonly Camera camera;
        private readonly Light light;
        private readonly Material floorMaterial;
        private readonly Mesh floorMesh;
        private readonly Texture2D groundMask;
        private readonly RenderTexture texture;
        private readonly RawImage image;
        private readonly TMP_Text caption;
        private readonly MusicViewScene scene;
        private readonly MusicPulse pulse = new MusicPulse();
        private readonly MusicMoment moment = new MusicMoment { Silent = false };
        private readonly MusicEffectFrame frame = new MusicEffectFrame
        {
            Radius = Radius,
            Fresh = new List<MusicLiveStrike>(),
            Strikes = new List<MusicLiveStrike>()
        };
        private IMusicEffectView view;
        private BoomboxMusicEffect shown;
        private float lastRender;
        private double time;
        private int beat = -1;
        private float yaw, pitch = 50, zoom = 1;
        private bool closeView;

        internal BoomboxEffectPreview(RectTransform parent, Material groundMaterial)
        {
            var title = BoomboxNativeUI.FixedText("EffectPreviewTitle", parent, 21, true);
            BoomboxNativeUI.Place(title.rectTransform, 510, 212, 526, 32);
            title.text = BoomboxText.Get("text.preview");
            var rect = BoomboxNativeUI.Rect("EffectPreview", parent);
            BoomboxNativeUI.Place(rect, 510, 252, 526, 382);
            image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = true;
            var input = rect.gameObject.AddComponent<BoomboxTrophyPreviewInput>();
            input.Dragged = delta =>
            {
                yaw -= delta.x * .35f;
                pitch = Mathf.Clamp(pitch - delta.y * .25f, 20, 80);
                PositionCamera();
            };
            input.Scrolled = (ticks, point) =>
            {
                zoom = Mathf.Clamp(zoom * Mathf.Exp(ticks * .12f), .55f, 3f);
                PositionCamera();
            };
            var reset = BoomboxNativeUI.CleanButton(
                "ResetEffectView",
                parent,
                BoomboxText.Get("text.reset_view_2"),
                ResetView
            );
            BoomboxNativeUI.Place((RectTransform)reset.transform, 510, 716, 526, 32);
            texture = new RenderTexture(
                788,
                572,
                24
            )
            {
                name = "Boombox effect preview",
                hideFlags = HideFlags.DontSave
            };
            texture.Create();
            image.texture = texture;
            caption = BoomboxNativeUI.FixedText("EffectPreviewCaption", parent, 16);
            BoomboxNativeUI.Place(caption.rectTransform, 510, 644, 526, 70);
            host = new GameObject("BoomboxEffectPreviewScene");
            host.transform.position = new Vector3(10000, 50, 10000);
            model = VikingBoomboxVisual.Create(host.transform);
            model.transform.localPosition = Vector3.up * .8f;
            var cameraObject = new GameObject("EffectPreviewCamera");
            cameraObject.transform.SetParent(host.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = texture;
            camera.cullingMask = 1 << BoomboxTrophyPreview.Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .09f, .075f, 1);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 65;
            camera.fieldOfView = 48;
            camera.aspect = 526f / 382f;
            camera.depthTextureMode = DepthTextureMode.Depth;
            scene = new MusicViewScene(host.transform, camera, BoomboxTrophyPreview.Layer);
            scene.Own(model);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "EffectPreviewGround";
            UnityEngine.Object.Destroy(floor.GetComponent<Collider>());
            scene.Own(floor);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(6.4f, 1, 6.4f);
            floorMaterial = new Material(groundMaterial) { name = "Boombox preview native terrain" };
            groundMask = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Boombox preview earth mask" };
            groundMask.SetPixel(0, 0, Heightmap.m_paintMaskDirt);
            groundMask.Apply(false, true);
            floorMaterial.SetTexture("_ClearedMaskTex", groundMask);
            floorMaterial.SetFloatArray("_depth", new float[4]);
            floorMesh = UnityEngine.Object.Instantiate(floor.GetComponent<MeshFilter>().sharedMesh);
            var colors = new Color32[floorMesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Heightmap.GetBiomeColor(Heightmap.Biome.Meadows);
            floorMesh.colors32 = colors;
            floor.GetComponent<MeshFilter>().sharedMesh = floorMesh;
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var groundProperties = new MaterialPropertyBlock();
            groundProperties.SetFloat("_LodHideDistance", 1000000f);
            floor.GetComponent<Renderer>().SetPropertyBlock(groundProperties);
            var lightObject = new GameObject("EffectPreviewLight");
            lightObject.transform.SetParent(host.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(45, -30, 0);
            light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.cullingMask = 1 << BoomboxTrophyPreview.Layer;
            light.shadows = LightShadows.None;
            light.enabled = false;
            frame.Center = host.transform.position;
            frame.Moment = moment;
        }

        internal void Present(MusicEffectSettings settings)
        {
            host.SetActive(true);
            float now = Time.unscaledTime;
            if (now - lastRender < 1f / 30f)
                return;
            float delta = Mathf.Min(now - lastRender, .1f);
            lastRender = now;
            if (view == null || shown != settings.Variant.Value)
            {
                view?.Hide();
                view?.Destroy();
                pulse.Reset();
                frame.Fresh.Clear();
                frame.Strikes.Clear();
                time = 0;
                beat = -1;
                shown = settings.Variant.Value;
                view = MusicEffectViews.Create(shown, host.transform, model, pulse, scene);
                closeView = shown == BoomboxMusicEffect.Sparks || shown == BoomboxMusicEffect.RunesAround;
                PositionCamera();
            }
            time += delta;
            frame.Time = moment.Time = time;
            frame.Delta = delta;
            int next = (int)(time / .8);
            float age = (float)(time % .8);
            float strength = next % 3 == 0
                ? .95f
                : next % 3 == 1
                    ? .65f
                    : .4f;
            moment.Level = .08f + strength * Mathf.Exp(-age * 5);
            moment.Smooth = new MusicRanges { Whole = 1, Low = .8 + moment.Level, Mid = 1, High = .7 + moment.Level * .4 };
            frame.Fresh.Clear();
            frame.Strikes.RemoveAll(strike => time - strike.Time > 1);
            if (next != beat)
            {
                beat = next;
                if (strength >= settings.Current.Threshold.Value)
                {
                    var strike = new MusicLiveStrike
                    {
                        Time = time,
                        Origin = frame.Center,
                        Strength = strength,
                        Loudest = MusicRange.Low
                    };
                    frame.Fresh.Add(strike);
                    frame.Strikes.Add(strike);
                }
            }
            frame.Brightness = settings.Current.Brightness.Value;
            frame.Color = settings.Current.Color.Value.linear;
            frame.ColorMode = settings.Current.ColorMode?.Value ?? BoomboxSparkColor.Own;
            frame.Amount = Mathf.Clamp(
                settings.Amount.Value,
                .25f,
                shown == BoomboxMusicEffect.Sparks || shown == BoomboxMusicEffect.RunesAround
                    ? 2f
                    : 1f
            );
            pulse.Update(time, moment.Level, Radius, frame.Targets, null);
            view.Update(frame);
            caption.text = BoomboxText.Get("text.drag_to_rotate_scroll_to_zoom_preview");
            light.enabled = true;
            try
            {
                camera.Render();
            }
            finally
            {
                light.enabled = false;
            }
        }

        internal void Hide()
        {
            if (!host.activeSelf)
                return;
            view?.Hide();
            host.SetActive(false);
            lastRender = Time.unscaledTime;
            beat = -1;
            frame.Strikes.Clear();
            frame.Fresh.Clear();
            pulse.Reset();
        }

        private void ResetView()
        {
            yaw = 0;
            pitch = 50;
            zoom = 1;
            PositionCamera();
        }
        private void PositionCamera()
        {
            if (camera == null || host == null)
                return;
            var target = host.transform.position + Vector3.up * (closeView ? .7f : 0);
            var direction = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
            camera.transform.position = target + direction * ((closeView ? 5.3f : 17f) / zoom);
            camera.transform.LookAt(target);
        }

        public void Dispose()
        {
            view?.Hide();
            view?.Destroy();
            UnityEngine.Object.Destroy(host);
            image.texture = null;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(floorMaterial);
            UnityEngine.Object.Destroy(floorMesh);
            UnityEngine.Object.Destroy(groundMask);
        }
    }
}
