using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal static class BoomboxNativeUI
    {
        internal static readonly Color Gold = new Color(1f, .72f, .28f);
        internal static readonly Color TextColor = new Color(.92f, .86f, .73f);
        internal static RectTransform Rect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.layer = LayerMask.NameToLayer("UI");
            obj.transform.SetParent(parent, false);
            return (RectTransform)obj.transform;
        }
        internal static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }
        internal static Image Image(string name, Transform parent, Sprite sprite, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = sprite == null ? UnityEngine.UI.Image.Type.Simple : UnityEngine.UI.Image.Type.Sliced;
            image.raycastTarget = false;
            return image;
        }
        internal static TMP_Text Text(string name, Transform parent, float size, bool heading = false)
        {
            var source = heading ? InventoryGui.instance.m_recipeName : InventoryGui.instance.m_recipeDecription;
            var text = UnityEngine.Object.Instantiate(source, parent, false);
            text.name = name;
            text.gameObject.SetActive(true);
            text.enabled = true;
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.color = heading ? Gold : TextColor;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.margin = Vector4.zero;
            text.overflowMode = TextOverflowModes.Truncate;
            text.text = "";
            text.raycastTarget = false;
            return text;
        }
        internal static Button Button(string name, Transform parent, string label, Action action)
        {
            var button = UnityEngine.Object.Instantiate(InventoryGui.instance.m_craftButton, parent, false);
            button.name = name;
            button.gameObject.SetActive(true);
            button.interactable = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => action());
            foreach (var old in button.GetComponents<ButtonTextColor>())
                UnityEngine.Object.DestroyImmediate(old);
            foreach (var old in button.GetComponents<ButtonImageColor>())
                UnityEngine.Object.DestroyImmediate(old);
            var sprites = button.spriteState;
            sprites.selectedSprite = null;
            button.spriteState = sprites;
            var texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
                texts[i].gameObject.SetActive(i == 0);
            if (texts.Length > 0)
            {
                texts[0].enableAutoSizing = false;
                texts[0].fontSize = 18;
                texts[0].alignment = TextAlignmentOptions.Center;
                texts[0].color = TextColor;
                texts[0].text = label;
                texts[0].richText = true;
                texts[0].overflowMode = TextOverflowModes.Truncate;
                var textRect = texts[0].rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.pivot = new Vector2(.5f, .5f);
                textRect.offsetMin = new Vector2(8, 2);
                textRect.offsetMax = new Vector2(-8, -2);
                textRect.localScale = Vector3.one;
            }
            var nativeTooltip = button.GetComponent<UITooltip>();
            if (nativeTooltip != null)
                UnityEngine.Object.Destroy(nativeTooltip);
            button.gameObject.AddComponent<BoomboxButtonStyle>();
            return button;
        }
        internal static Button CleanButton(string name, Transform parent, string label, Action action)
        {
            var button = Button(name, parent, label, action);
            KeepNative(button.gameObject, button.GetComponentInChildren<TMP_Text>(true));
            return button;
        }
        internal static TMP_Text FixedText(string name, Transform parent, float size, bool heading = false)
        {
            var text = Text(name, parent, size, heading);
            foreach (var fitter in text.GetComponents<ContentSizeFitter>())
                UnityEngine.Object.DestroyImmediate(fitter);
            return text;
        }
        internal static void KeepNative(GameObject clone, params UnityEngine.Object[] keep)
        {
            var targets = new HashSet<Transform>();
            foreach (var item in keep)
            {
                var target = item is GameObject gameObject
                    ? gameObject.transform
                    : item is Component component
                        ? component.transform
                        : null;
                if (target != null)
                    targets.Add(target);
            }
            var ancestors = new HashSet<Transform>();
            foreach (var target in targets)
                for (var node = target; node != null && node != clone.transform; node = node.parent)
                    ancestors.Add(node);
            Prune(clone.transform, targets, ancestors);
            RemoveForeign(clone);
        }
        internal static void RemoveForeign(GameObject clone)
        {
            var own = typeof(BoomboxNativeUI).Assembly;
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform)
                    continue;
                var assembly = component.GetType().Assembly;
                if (assembly != own
                    && !assembly.IsDynamic
                    && assembly.Location.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.Destroy(component);
            }
        }
        private static void Prune(Transform node, HashSet<Transform> targets, HashSet<Transform> ancestors)
        {
            for (int i = node.childCount - 1; i >= 0; i--)
            {
                var child = node.GetChild(i);
                if (targets.Contains(child))
                    continue;
                if (ancestors.Contains(child))
                    Prune(child, targets, ancestors);
                else
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }
        internal static void Label(Button button, string label, Color? color = null)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
                return;
            text.text = label;
            var style = button.GetComponent<BoomboxButtonStyle>();
            if (style != null)
                style.NormalColor = color ?? TextColor;
            else
                text.color = color ?? TextColor;
        }
        internal static void Select(Button button, bool selected)
        {
            var source = InventoryGui.instance.m_craftButton;
            var sprite = selected ? source.spriteState.highlightedSprite : source.image.sprite;
            if (button.image.sprite != sprite)
                button.image.sprite = sprite;
            var style = button.GetComponent<BoomboxButtonStyle>();
            if (style != null)
                style.NormalColor = selected ? Gold : TextColor;
        }
        private static Sprite wood;
        internal static Sprite Wood
        {
            get
            {
                if (wood == null)
                    wood = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == "woodpanel_trophys");
                return wood;
            }
        }

        internal static ScrollRect Scroll(
            string name,
            Transform parent,
            out RectTransform content
        ) => Scroll(name, parent, out content, 0);
        internal static ScrollRect ScrollGrid(
            string name,
            Transform parent,
            out RectTransform content,
            int columns
        ) => Scroll(name, parent, out content, columns);
        private static ScrollRect Scroll(string name, Transform parent, out RectTransform content, int columns)
        {
            var rect = Rect(name, parent);
            var scroll = rect.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect("Viewport", rect);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-20, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1);
            content.sizeDelta = Vector2.zero;
            if (columns > 0)
            {
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(56, 56);
                grid.spacing = new Vector2(6, 6);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
            }
            else
            {
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 8;
            }
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35;
            var bar = UnityEngine.Object.Instantiate(InventoryGui.instance.m_recipeListScroll, rect, false);
            bar.name = "Scrollbar";
            bar.gameObject.SetActive(true);
            bar.onValueChanged = new Scrollbar.ScrollEvent();
            foreach (var graphic in bar.GetComponentsInChildren<Graphic>(true))
                graphic.enabled = true;
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(1, 0);
            barRect.anchorMax = Vector2.one;
            barRect.pivot = new Vector2(1, 1);
            barRect.offsetMin = new Vector2(-14, 0);
            barRect.offsetMax = Vector2.zero;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }
    }
}
