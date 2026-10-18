using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator EffectPreviewCapture()
    {
        yield return AwaitAudio();
        var plugin = BoomboxPlugin.Instance;
        var settings = plugin.Settings.Effect;
        var old = settings.Variant.Value;
        var frames = new JArray();
        var cases = new JArray();
        var proof = new JObject { ["candidate"] = productSha, ["frames"] = frames, ["cases"] = cases };
        lastCapture = proof;
        string language = Localization.instance.GetSelectedLanguage();
        float started = Time.realtimeSinceStartup;
        bool damage = settings.CreatureDamage.Value;
        settings.CreatureDamage.Value = false;
        try
        {
            UiParkWindow(true);
            Localization.instance.SetLanguage("Russian");
            InventoryGui.instance.Show(null, 1);
            yield return null;
            UiDispatch(UiButton("BoomboxMusicButton"), false);
            yield return null;
            UiDispatch(UiButton("Tab5"), false);
            yield return new WaitForSecondsRealtime(.2f);
            var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
            var panel = AccessTools.Field(menu.GetType(), "effectsPanel").GetValue(menu);
            var preview = AccessTools.Field(panel.GetType(), "preview").GetValue(panel);
            int begun = BoomboxMusicNetwork.Current.BegunEvents;
            var host = (GameObject)AccessTools.Field(preview.GetType(), "host").GetValue(preview);
            Need(
                Mathf.Abs(UiButton("ShowEffects").anchoredPosition.y - UiButton("ShowOthersWaves").anchoredPosition.y)
                    < .1f,
                "Toggle rows not aligned"
            );
            var terrain = host.transform.Find("EffectPreviewGround").GetComponent<Renderer>();
            Need(
                terrain.sharedMaterial.shader
                    == Heightmap.FindHeightmap(Player.m_localPlayer.transform.position).m_material.shader,
                "Preview must use native terrain shader"
            );
            proof["groundShader"] = terrain.sharedMaterial.shader.name;
            Need(
                host.GetComponentsInChildren<ZNetView>(true).Length == 0
                    && host.GetComponentsInChildren<AudioSource>(true).Length == 0,
                "Preview has gameplay/audio components"
            );
            foreach (BoomboxMusicEffect variant in System.Enum.GetValues(typeof(BoomboxMusicEffect)))
            {
                settings.Variant.Value = variant;
                yield return new WaitForSecondsRealtime(.85f);
                var raw = GameObject.Find("EffectPreview").GetComponent<RawImage>();
                var first = PreviewPixels((RenderTexture)raw.texture);
                yield return HudFrame("effect-editor-" + variant + ".jpg", frames);
                yield return new WaitForSecondsRealtime(.15f);
                var second = PreviewPixels((RenderTexture)raw.texture);
                int changed = 0;
                for (int i = 0; i < first.Length; i++)
                    if (first[i].r != second[i].r || first[i].g != second[i].g || first[i].b != second[i].b)
                        changed++;
                float animationDeadline = Time.realtimeSinceStartup + 3f;
                while (changed <= 5 && Time.realtimeSinceStartup < animationDeadline)
                {
                    yield return new WaitForSecondsRealtime(.1f);
                    second = PreviewPixels((RenderTexture)raw.texture);
                    changed = 0;
                    for (int i = 0; i < first.Length; i++)
                        if (first[i].r != second[i].r || first[i].g != second[i].g || first[i].b != second[i].b)
                            changed++;
                }
                cases.Add(new JObject { ["variant"] = variant.ToString(), ["changedPixels"] = changed });
                Need(changed > 5, "Preview not animated over a complete rhythm cycle: " + variant);
                if (variant == BoomboxMusicEffect.Strike)
                {
                    var view = AccessTools.Field(preview.GetType(), "view").GetValue(preview);
                    var ground = AccessTools.Field(view.GetType(), "ground").GetValue(view);
                    Need(
                        (float)AccessTools.Property(ground.GetType(), "Size").GetValue(ground) >= 12,
                        "Preview ring clipped by map extent"
                    );
                    var camera = (Camera)AccessTools.Field(preview.GetType(), "camera").GetValue(preview);
                    for (int i = 0; i < 32; i++)
                    {
                        float angle = i * Mathf.PI / 16;
                        var edge = camera.WorldToViewportPoint(
                            host.transform.position + new Vector3(Mathf.Sin(angle) * 6, 0, Mathf.Cos(angle) * 6)
                        );
                        Need(
                            edge.x > .02f && edge.x < .98f && edge.y > .02f && edge.y < .98f,
                            "Ring clipped by preview viewport"
                        );
                    }
                }
            }
            settings.Variant.Value = BoomboxMusicEffect.RunesAround;
            yield return new WaitForSecondsRealtime(.2f);
            float saved = settings.Current.Brightness.Value;
            try
            {
                UiDispatch(UiButton("BrightnessLess"), false);
                yield return null;
                var frame = AccessTools.Field(preview.GetType(), "frame").GetValue(preview);
                yield return new WaitForSecondsRealtime(.08f);
                Need(
                    (float)AccessTools.Field(frame.GetType(), "Brightness").GetValue(frame)
                        == settings.Current.Brightness.Value
                        && settings.Current.Brightness.Value < saved,
                    "Brightness not live"
                );
            }
            finally
            {
                settings.Current.Brightness.Value = saved;
            }
            var color = settings.Current.Color.Value;
            float amount = settings.Amount.Value, threshold = settings.Current.Threshold.Value;
            try
            {
                var palette = GameObject.Find("EffectColorPalette").GetComponent<RectTransform>();
                var point = palette.TransformPoint(
                    new Vector3(
                        palette.rect.xMin + palette.rect.width * .75f,
                        palette.rect.yMin + palette.rect.height * .65f,
                        0
                    )
                );
                var pointer = new UnityEngine.EventSystems.PointerEventData(
                    UnityEngine.EventSystems.EventSystem.current
                )
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, point),
                    button = UnityEngine.EventSystems.PointerEventData.InputButton.Left
                };
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    palette.gameObject,
                    pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler
                );
                Need(settings.Current.Color.Value != color, "Palette input did not change color");
                UiDispatch(UiButton("AmountLess"), false);
                UiDispatch(UiButton("StrikeThresholdMore"), false);
                yield return new WaitForSecondsRealtime(.1f);
                var frame = AccessTools.Field(preview.GetType(), "frame").GetValue(preview);
                Need(
                    (Color)AccessTools.Field(frame.GetType(), "Color").GetValue(frame)
                        == settings.Current.Color.Value.linear,
                    "Color not live"
                );
                Need(
                    (float)AccessTools.Field(frame.GetType(), "Amount").GetValue(frame) == settings.Amount.Value,
                    "Amount not live"
                );
                Need(settings.Current.Threshold.Value > threshold, "Threshold button failed");
                UiDispatch(UiButton("ResetEffect"), false);
                yield return new WaitForSecondsRealtime(.1f);
                Need(
                    settings.Current.Color.Value == (Color)settings.Current.Color.DefaultValue
                    && settings.Current.Threshold.Value == (float)settings.Current.Threshold.DefaultValue,
                    "Reset preview values failed"
                );
            }
            finally
            {
                settings.Current.Color.Value = color;
                settings.Amount.Value = amount;
                settings.Current.Threshold.Value = threshold;
                settings.Current.Brightness.Value = saved;
            }
            var cameraView = (Camera)AccessTools.Field(preview.GetType(), "camera").GetValue(preview);
            var position = cameraView.transform.position;
            var previewRect = GameObject.Find("EffectPreview");
            var drag = new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current
            )
            {
                delta = new Vector2(70, 15),
                scrollDelta = new Vector2(0, 6)
            };
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                previewRect,
                drag,
                UnityEngine.EventSystems.ExecuteEvents.dragHandler
            );
            Need(Vector3.Distance(position, cameraView.transform.position) > .1f, "Preview drag did not rotate");
            float distance = Vector3.Distance(cameraView.transform.position, host.transform.position);
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                previewRect,
                drag,
                UnityEngine.EventSystems.ExecuteEvents.scrollHandler
            );
            Need(
                Mathf.Abs(distance - Vector3.Distance(cameraView.transform.position, host.transform.position)) > .01f,
                "Preview wheel did not zoom"
            );
            yield return HudFrame("effect-editor-interaction.jpg", frames);
            UiDispatch(UiButton("ResetEffectView"), false);
            Need(
                Vector3.Distance(position, cameraView.transform.position) < .01f,
                "Preview reset did not restore camera"
            );
            UiDispatch(UiButton("Tab0"), false);
            yield return null;
            float hideDeadline = Time.realtimeSinceStartup + 1;
            while (host.activeSelf && Time.realtimeSinceStartup < hideDeadline)
                yield return null;
            float before = (float)AccessTools.Field(preview.GetType(), "lastRender").GetValue(preview);
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                !host.activeSelf
                    && (float)AccessTools.Field(preview.GetType(), "lastRender").GetValue(preview) == before,
                "Hidden preview still renders"
            );
            UiDispatch(UiButton("Tab5"), false);
            yield return new WaitForSecondsRealtime(.15f);
            Need(
                UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(
                    t => t.name == "BoomboxEffectPreviewScene"
                ) == 1,
                "Duplicate preview scene"
            );
            Need(BoomboxMusicNetwork.Current.BegunEvents == begun, "Preview emitted network events");
            proof["seconds"] = Time.realtimeSinceStartup - started;
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            settings.Variant.Value = old;
            settings.CreatureDamage.Value = damage;
            Localization.instance.SetLanguage(language);
            UiParkWindow();
        }
    }
    private static Color32[] PreviewPixels(RenderTexture texture)
    {
        var active = RenderTexture.active;
        var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixels32();
        }
        finally
        {
            RenderTexture.active = active;
            Destroy(pixels);
        }
    }
}
