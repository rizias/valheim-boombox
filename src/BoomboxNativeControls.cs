using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal static class BoomboxNativeControls
    {
        private static readonly List<TMP_InputField> fields = new List<TMP_InputField>();
        internal static bool Focused => BoomboxPlugin.Instance != null && BoomboxPlugin.Instance.MenuOpen
        && fields.Any(f => f != null && f.isActiveAndEnabled && f.isFocused);

        internal static TMP_InputField Input(string name, Transform parent, int limit)
        {
            var source = (TMP_InputField)AccessTools.Field(typeof(BuildUi), "m_searchField")
                .GetValue(Hud.instance.m_buildUi);
            var input = UnityEngine.Object.Instantiate(source, parent, false);
            input.name = name;
            input.gameObject.SetActive(true);
            BoomboxNativeUI.KeepNative(input.gameObject, input.textViewport, input.textComponent, input.placeholder);
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onSelect = new TMP_InputField.SelectionEvent();
            input.onDeselect = new TMP_InputField.SelectionEvent();
            input.characterLimit = limit;
            input.text = "";
            fields.RemoveAll(f => f == null);
            fields.Add(input);
            return input;
        }

        internal static void Placeholder(TMP_InputField input, string text)
        {
            if (input.placeholder is TMP_Text placeholder)
                placeholder.text = text;
        }

        internal static Button TextButton(string name, Transform parent, string label, Action action)
        {
            var button = BoomboxNativeUI.CleanButton(name, parent, label, action);
            var text = button.GetComponentInChildren<TMP_Text>(true);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        internal static Button IconButton(string name, Transform parent, string icon, Action action)
        {
            var button = TextButton(name, parent, "", action);
            var image = BoomboxNativeUI.Image(
                "Icon",
                button.transform,
                BoomboxPlayerIcons.Get(icon),
                BoomboxNativeUI.TextColor
            );
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(18, 18);
            rect.anchoredPosition = Vector2.zero;
            return button;
        }

        internal static void Icon(Button button, string icon, bool visible = true)
        {
            var image = button.transform.Find("Icon")?.GetComponent<Image>();
            if (image == null)
                return;
            var sprite = BoomboxPlayerIcons.Get(icon);
            if (image.sprite != sprite)
                image.sprite = sprite;
            if (image.enabled != visible)
                image.enabled = visible;
        }

        internal static TMP_Text Line(string name, Transform parent, float size, bool heading = false)
        {
            var text = BoomboxNativeUI.FixedText(name, parent, size, heading);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            return text;
        }

        internal static Slider Slider(string name, Transform parent, Action<float> changed)
        {
            var slider = UnityEngine.Object.Instantiate(
                InventoryGui.instance.m_splitDialog.m_splitSlider,
                parent,
                false
            );
            slider.name = name;
            slider.gameObject.SetActive(true);
            BoomboxNativeUI.RemoveForeign(slider.gameObject);
            foreach (var tooltip in slider.GetComponentsInChildren<UITooltip>(true))
                UnityEngine.Object.Destroy(tooltip);
            slider.onValueChanged = new Slider.SliderEvent();
            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = true;
            slider.onValueChanged.AddListener(value => changed(value));
            return slider;
        }

        internal static ScrollRect List(string name, Transform parent, out RectTransform content)
        {
            var scroll = BoomboxNativeUI.Scroll(name, parent, out content);
            content.GetComponent<VerticalLayoutGroup>().spacing = 4;
            return scroll;
        }

        internal static RectTransform Row(string name, Transform parent, float height)
        {
            var row = BoomboxNativeUI.Rect(name, parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return row;
        }

        internal static void Stretch(RectTransform rect, float left, float right, float y, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.offsetMin = new Vector2(left, -y - height);
            rect.offsetMax = new Vector2(-right, -y);
            rect.localScale = Vector3.one;
        }

        internal static void Right(RectTransform rect, float right, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-right, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        internal static void Show(Component component, bool visible)
        {
            if (component.gameObject.activeSelf != visible)
                component.gameObject.SetActive(visible);
        }
    }

    internal sealed class BoomboxSliderHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        internal bool Held
        {
            get;
            private set;
        }
        internal Action Released;
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                Held = GetComponent<Selectable>().IsInteractable();
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left)
                return;
            Held = false;
            Released?.Invoke();
        }
        private void OnDisable()
        {
            Held = false;
        }
    }
}
