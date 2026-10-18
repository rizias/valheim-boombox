using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxDisplay : MonoBehaviour
    {
        public TextMeshPro Label;
        public TextMeshPro TimeLabel;
        public float Progress
        {
            get;
            private set;
        }
        private readonly List<Material> materials = new List<Material>();
        private string shown;
        private Transform fill;
        private Transform progressBackground;
        private Vector3 progressCentre;
        private Vector3 progressRight;
        private float progressWidth;
        private Material textMaterial;
        private Material progressMaterial;
        private Color progressEmission;
        private Vector3 titlePosition;
        private Vector3 titleUp;
        private float titleFont;
        private float appliedScale = -1;
        private bool appliedTime;

        public void Initialize()
        {
            var deck = GetComponent<AudioDeck>();
            InitializeVisual(deck.Visual);
            Show(deck, 0f);
        }

        public void InitializeVisual(GameObject visualModel)
        {
            if (!VikingBoomboxVisual.CanRender)
                return;
            var visual = visualModel.transform;
            var model = visual.GetComponent<VikingBoomboxVisualState>();
            bool expanded = model.DisplayTitleAnchor != null && model.DisplayProgressAnchor != null;
            var titleAnchor = expanded ? model.DisplayTitleAnchor : model.DisplayAnchor;
            var progressAnchor = expanded ? model.DisplayProgressAnchor : model.DisplayAnchor;
            float width = VikingBoomboxVisualState.DisplayWidth;
            float titleHeight = 0.032f;
            Label = MakeText("NowPlayingTitle", visual, titleAnchor, width, titleHeight, expanded ? 0f : 0.014f);
            Label.alignment = TextAlignmentOptions.MidlineLeft;
            titlePosition = Label.transform.localPosition;
            titleUp = visual.InverseTransformDirection(titleAnchor.up);
            titleFont = Label.fontSize;
            Label.gameObject.AddComponent<BoomboxMarquee>();
            textMaterial = Label.fontSharedMaterial;
            TimeLabel = MakeText("NowPlayingTime", visual, progressAnchor, width, 0.010f, expanded ? -0.001f : -0.012f);
            var normal = visual.InverseTransformDirection(progressAnchor.forward);
            var up = visual.InverseTransformDirection(progressAnchor.up);
            var rotation = Quaternion.LookRotation(normal, up);
            progressRight = rotation * Vector3.left;
            progressCentre = visual.InverseTransformPoint(progressAnchor.position) + up * (expanded ? -0.009f : -0.016f) + normal * 0.003f;
            progressWidth = width;
            var background = BoomboxMaterials.Surface(visualModel, new Color(0.025f, 0.08f, 0.1f), "ScreenGlass");
            var foreground = BoomboxMaterials.Surface(visualModel, new Color(0.2f, 0.8f, 1f), "RuneIce");
            progressMaterial = foreground;
            progressEmission = foreground.HasProperty("_EmissionColor")
                ? foreground.GetColor("_EmissionColor")
                : Color.black;
            materials.Add(background);
            materials.Add(foreground);
            progressBackground = Bar("TrackProgressBackground", visual, rotation, width, background);
            fill = Bar("TrackProgress", visual, rotation, width, foreground);
        }

        public void SyncScroll(
            BoomboxDisplay source
        ) => Label.GetComponent<BoomboxMarquee>().Sync(source.Label.GetComponent<BoomboxMarquee>());

        internal void ShowRemote(bool playing)
        {
            if (Label == null)
                return;
            string value = playing ? BoomboxText.Get("config.Music") : BoomboxText.Get("text.paused");
            if (value != shown)
            {
                shown = value;
                Label.text = value;
                Label.ForceMeshUpdate();
            }
            Label.alignment = TextAlignmentOptions.Midline;
            Label.color = playing
                ? BoomboxPlugin.Instance.Settings.DisplayTextColor.Value
                : BoomboxPlugin.Instance.Settings.DisplayPausedColor.Value;
            TimeLabel.gameObject.SetActive(false);
            progressBackground.gameObject.SetActive(false);
            fill.gameObject.SetActive(false);
        }

        private TextMeshPro MakeText(
            string name,
            Transform root,
            Transform anchor,
            float width,
            float height,
            float offset
        )
        {
            var normal = root.InverseTransformDirection(anchor.forward);
            var up = root.InverseTransformDirection(anchor.up);
            var text = new GameObject(name);
            text.SetActive(false);
            text.transform.SetParent(root, false);
            text.transform.localPosition = root.InverseTransformPoint(anchor.position) + normal * 0.006f + up * offset;
            text.transform.localRotation = Quaternion.LookRotation(normal, up) * Quaternion.Euler(0f, 180f, 0f);
            var label = text.AddComponent<TextMeshPro>();
            label.font = InventoryGui.instance.m_recipeName.font;
            label.rectTransform.sizeDelta = new Vector2(width, height);
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Normal;
            label.richText = false;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.fontSize = 2f;
            float preferred = label.GetPreferredValues("Ag", Mathf.Infinity, Mathf.Infinity).y;
            if (preferred > 0)
                label.fontSize *= height * 0.84f / preferred;
            if (label.font != null)
            {
                var material = new Material(label.font.material);
                var shader = Shader.Find("TextMeshPro/Distance Field");
                if (shader != null && shader.isSupported)
                    material.shader = shader;
                material.DisableKeyword("GLOW_ON");
                Set(material, "_FaceDilate", .12f);
                Set(material, "_PerspectiveFilter", 0f);
                Set(material, "_OutlineWidth", 0f);
                Set(material, "_GlowOuter", 0f);
                Set(material, "_GlowPower", 0f);
                if (material.HasProperty("_OutlineColor"))
                    material.SetColor("_OutlineColor", new Color(0.01f, 0.09f, 0.16f));
                label.fontSharedMaterial = material;
                materials.Add(material);
            }
            text.SetActive(true);
            return label;
        }

        private Transform Bar(string name, Transform parent, Quaternion rotation, float width, Material material)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name;
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = progressCentre;
            bar.transform.localRotation = rotation;
            bar.transform.localScale = new Vector3(width, 0.0014f, 0.0004f);
            bar.GetComponent<Collider>().enabled = false;
            Destroy(bar.GetComponent<Collider>());
            bar.GetComponent<Renderer>().sharedMaterial = material;
            return bar.transform;
        }

        public void Show(AudioDeck deck, float bass)
        {
            if (Label == null)
                return;
            string value = deck.TrackLabel ?? BoomboxText.Get("text.select_music");
            if (value != shown)
            {
                shown = value;
                Label.text = value;
                Label.ForceMeshUpdate();
            }
            var settings = BoomboxPlugin.Instance.Settings;
            float scale = BoomboxSettings.PositiveOr(settings.DisplayTextScale.Value, 1f);
            bool showTime = settings.DisplayShowTime.Value;
            if (scale != appliedScale || showTime != appliedTime)
            {
                appliedScale = scale;
                appliedTime = showTime;
                float height = Mathf.Min((showTime ? .020f : .032f) * scale, showTime ? .024f : .032f);
                Label.fontSize = titleFont * height / .032f;
                Label.rectTransform.sizeDelta = new Vector2(VikingBoomboxVisualState.DisplayWidth, height);
                Label.transform.localPosition = titlePosition - titleUp * (showTime ? 0f : .008f);
                TimeLabel.gameObject.SetActive(showTime);
                Label.ForceMeshUpdate();
            }
            Color color = deck.Loading
                ? settings.DisplayLoadingColor.Value
                : deck.State.playing
                    ? settings.DisplayTextColor.Value
                    : settings.DisplayPausedColor.Value;
            Label.color = TimeLabel.color = color;
            float glow = settings.ShowVisualEffects.Value && settings.ShowBodyGlow.Value
                ? BoomboxSettings.Nonnegative(settings.BodyGlowIntensity.Value)
                : 0f;
            if (textMaterial != null && textMaterial.HasProperty("_GlowColor"))
                textMaterial.SetColor("_GlowColor", color * glow * (1f + bass * 0.25f));
            if (TimeLabel.fontSharedMaterial != null && TimeLabel.fontSharedMaterial.HasProperty("_GlowColor"))
                TimeLabel.fontSharedMaterial.SetColor("_GlowColor", color * glow);
            if (progressMaterial != null && progressMaterial.HasProperty("_EmissionColor"))
                progressMaterial.SetColor("_EmissionColor", progressEmission * glow);
            float total = deck.Source != null && deck.Source.clip != null
                ? deck.TrackSeconds
                : (float)(BoomboxPlugin.Instance.FindTrack(deck.State.TrackId)?.KnownDuration ?? 0);
            float elapsed = deck.State.seconds;
            TimeLabel.text = Clock(elapsed) + " / " + (total > 0 ? Clock(total) : "--:--");
            Progress = total > 0 ? Mathf.Clamp01(elapsed / total) : 0f;
            progressBackground.gameObject.SetActive(total > 0);
            fill.gameObject.SetActive(total > 0 && Progress > 0);
            TimeLabel.gameObject.SetActive(showTime && total > 0);
            fill.localScale = new Vector3(progressWidth * Progress, 0.0014f, 0.0005f);
            fill.localPosition = progressCentre - progressRight * (progressWidth * (1f - Progress) * 0.5f)
                + fill.localRotation * Vector3.forward * .0006f;
        }

        private static string Clock(float seconds)
        {
            int value = Math.Max(0, Mathf.FloorToInt(seconds));
            return value / 60 + ":" + (value % 60).ToString("00");
        }
        private static void Set(Material material, string name, float value)
        {
            if (material.HasProperty(name))
                material.SetFloat(name, value);
        }
        private void OnDestroy()
        {
            foreach (var material in materials)
                if (material != null)
                    Destroy(material);
        }
    }
}
