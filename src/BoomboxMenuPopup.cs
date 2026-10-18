using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxMenuPopup
    {
        private const float PanelWidth = 320f;
        private const float EntryHeight = 32f;
        private readonly RectTransform window;
        private readonly RectTransform root;
        private readonly RectTransform panel;
        private readonly ScrollRect list;
        private readonly RectTransform content;
        private readonly Button create;
        private readonly TMP_InputField name;
        private readonly Button confirm;
        private readonly BoomboxMenuLabels labels = new BoomboxMenuLabels();
        private Action<string> creating;
        private Vector2 anchorTop;
        private float anchorBottom;


        internal BoomboxMenuPopup(RectTransform frameWindow)
        {
            window = frameWindow;
            root = Stretched("MenuPopup", window);
            var blocker = Stretched("PopupBlocker", root);
            var shade = blocker.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0f);
            var outside = blocker.gameObject.AddComponent<Button>();
            outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(Close);
            panel = BoomboxNativeUI.Rect("PlaylistPopup", root);
            var background = BoomboxNativeUI.Image(
                "Background",
                panel,
                BoomboxNativeUI.Wood,
                new Color(.42f, .46f, .5f)
            );
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
            background.raycastTarget = true;
            list = BoomboxNativeControls.List("PopupEntries", panel, out content);
            create = labels.Add(
                BoomboxNativeControls.TextButton("PopupNewPlaylist", panel, "", StartNaming),
                () => BoomboxText.Get("text.new_playlist_2")
            );
            name = labels.Add(
                BoomboxNativeControls.Input("PopupName", panel, 60),
                () => BoomboxText.Get("text.playlist_name")
            );
            confirm = labels.Add(
                BoomboxNativeControls.TextButton("PopupCreate", panel, "", Create),
                () => BoomboxText.Get("text.create")
            );
            root.gameObject.SetActive(false);
        }

        internal void Open(RectTransform anchor, IList<KeyValuePair<string, Action>> entries, Action<string> create)
        {
            labels.Refresh();
            creating = create;
            foreach (Transform child in content)
            {
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
            for (int i = 0; i < entries.Count; i++)
            {
                var action = entries[i].Value;
                var button = BoomboxNativeControls.TextButton(
                "PopupEntry" + i,
                content,
                entries[i].Key,
                () =>
                    {
                        Close();
                        action();
                    }
                );
                var text = button.GetComponentInChildren<TMP_Text>();
                text.alignment = TextAlignmentOptions.MidlineLeft;
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = EntryHeight;
            }
            var corners = new Vector3[4];
            anchor.GetWorldCorners(corners);
            var low = window.InverseTransformPoint(corners[0]);
            var high = window.InverseTransformPoint(corners[1]);
            anchorTop = new Vector2(low.x, -high.y);
            anchorBottom = -low.y;
            name.text = "";
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            Layout(false, entries.Count);
        }

        internal void Close()
        {
            if (root == null || !root.gameObject.activeSelf)
                return;
            root.gameObject.SetActive(false);
            creating = null;
        }

        private static RectTransform Stretched(string name, Transform parent)
        {
            var rect = BoomboxNativeUI.Rect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void StartNaming()
        {
            Layout(true, 0);
            name.ActivateInputField();
        }

        private void Create()
        {
            string value = name.text?.Trim();
            if (string.IsNullOrEmpty(value) || creating == null)
                return;
            var action = creating;
            Close();
            action(value);
        }

        private void Layout(bool naming, int count)
        {
            BoomboxNativeControls.Show(list, !naming);
            BoomboxNativeControls.Show(create, !naming && creating != null);
            BoomboxNativeControls.Show(name, naming);
            BoomboxNativeControls.Show(confirm, naming);
            float listHeight = Mathf.Min(8, count) * (EntryHeight + 4f);
            float height = naming
                ? 96f
                : 16f + listHeight + (creating != null ? EntryHeight + 4f : 0f);
            float x = Mathf.Clamp(anchorTop.x, 8f, BoomboxMenuFrame.Width - PanelWidth - 8f);
            float y = anchorBottom + 4f + height > BoomboxMenuFrame.Height - 8f
                ? Mathf.Max(8f, anchorTop.y - 4f - height)
                : anchorBottom + 4f;
            BoomboxNativeUI.Place(panel, x, y, PanelWidth, height);
            BoomboxNativeUI.Place((RectTransform)list.transform, 8, 8, PanelWidth - 16, listHeight);
            BoomboxNativeUI.Place((RectTransform)create.transform, 8, 8 + listHeight, PanelWidth - 16, EntryHeight);
            BoomboxNativeUI.Place((RectTransform)name.transform, 8, 12, PanelWidth - 16, EntryHeight);
            BoomboxNativeUI.Place((RectTransform)confirm.transform, 8, 52, PanelWidth - 16, EntryHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }
    }
}
