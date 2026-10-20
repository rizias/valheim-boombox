using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxMenuFrame : IDisposable
    {
        internal const float Width = 1060f;
        internal const float Height = 896f;
        internal const float ContentHeight = 760f;
        private readonly GameObject canvas;
        private readonly Dictionary<int, Button> tabs = new Dictionary<int, Button>();
        private readonly BoomboxMenuLabels labels = new BoomboxMenuLabels();
        private readonly BoomboxPlayerBar player;
        private readonly BoomboxPlugin plugin;
        private readonly Button playerToggle;
        private int shownTab = -1;
        private Vector2 position = new Vector2(80, 70);
        internal readonly RectTransform Window;
        internal bool Alive => canvas != null && Window != null;


        internal BoomboxMenuFrame(BoomboxPlugin plugin, Action<int> selectTab)
        {
            this.plugin = plugin;
            canvas = new GameObject(
                "BoomboxMenuCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)
            );
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 1000;
            Window = BoomboxNativeUI.Rect("BoomboxMenuWindow", canvas.transform);
            BoomboxNativeUI.Place(Window, position.x, position.y, Width, Height);
            var background = BoomboxNativeUI.Image(
                "WoodPanel",
                Window,
                BoomboxNativeUI.Wood,
                new Color(.55f, .6f, .65f)
            );
            background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
            background.raycastTarget = true;
            var heading = labels.Add(
                BoomboxNativeControls.Line("WindowTitle", Window, 25, true),
                () => BoomboxText.Get("boombox_name")
            );
            heading.alignment = TextAlignmentOptions.TopLeft;
            BoomboxNativeUI.Place(heading.rectTransform, 24, 20, 440, 36);
            var handle = BoomboxNativeUI.Image("WindowDragHandle", Window, null, Color.clear);
            BoomboxNativeUI.Place(handle.rectTransform, 0, 0, 920, 60);
            handle.raycastTarget = true;
            handle.gameObject.AddComponent<BoomboxWindowDrag>().Moved = delta =>
            {
                position += new Vector2(delta.x, -delta.y);
                Layout();
            };
            var close = labels.Add(
                BoomboxNativeControls.TextButton("Close", Window, "", plugin.CloseMenu),
                () => BoomboxText.Get("text.close")
            );
            BoomboxNativeUI.Place((RectTransform)close.transform, 936, 20, 100, 36);
            playerToggle = BoomboxNativeControls.TextButton(
                "ShowPlayerPanel",
                Window,
                "",
                () => plugin.Settings.ShowPlayerPanel.Value = !plugin.Settings.ShowPlayerPanel.Value
            );
            BoomboxNativeUI.Place((RectTransform)playerToggle.transform, 624, 20, 140, 36);
            var playerReset = labels.Add(
                BoomboxNativeControls.TextButton(
                    "ResetPlayerPanel",
                    Window,
                    "",
                    plugin.InventorySlot.ResetPlayer
                ),
                () => BoomboxText.Get("text.reset_player")
            );
            BoomboxNativeUI.Place((RectTransform)playerReset.transform, 776, 20, 148, 36);
            (int Id, Func<string> Label)[] definitions = {
                (0, () => BoomboxText.Get("text.library")),
                (1, () => BoomboxText.Get("text.playlists_2")),
                (2, () => BoomboxText.Get("text.folders")),
                (3, () => BoomboxText.Get("boombox_cassette_archive")),
                (7, () => BoomboxText.Get("text.bonuses")),
                (4, () => BoomboxText.Get("text.decorations")),
                (5, () => BoomboxText.Get("text.effects"))
            };
            float x = 24;
            foreach (var definition in definitions)
            {
                int index = definition.Id;
                var button = labels.Add(
                    BoomboxNativeControls.TextButton(
                        "Tab" + index,
                        Window,
                        "",
                        () => selectTab(index)
                    ),
                    definition.Label
                );
                tabs.Add(index, button);
                BoomboxNativeUI.Place((RectTransform)button.transform, x, 64, 137, 38);
                x += 145;
            }
            player = new BoomboxPlayerBar(plugin, Window);
        }

        internal void Show(int tab)
        {
            if (!canvas.activeSelf)
                canvas.SetActive(true);
            Layout();
            BoomboxNativeUI.Label(
                playerToggle,
                BoomboxText.Get("text.player_2") + (plugin.Settings.ShowPlayerPanel.Value
                    ? BoomboxText.Get("text.on")
                    : BoomboxText.Get("text.off"))
            );
            if (labels.Refresh() || tab != shownTab)
            {
                shownTab = tab;
                foreach (var entry in tabs)
                {
                    BoomboxNativeUI.Select(entry.Value, entry.Key == tab);
                    entry.Value.GetComponentInChildren<TMP_Text>().color = entry.Key == tab
                        ? BoomboxNativeUI.Gold
                        : BoomboxNativeUI.TextColor;
                }
            }
            player.Refresh();
        }

        internal void Hide()
        {
            if (canvas != null && canvas.activeSelf)
                canvas.SetActive(false);
        }
        private void Layout()
        {
            float desired = Screen.height / 1080f;
            float scale = Mathf.Min(desired, (Screen.width - 32f) / Width, (Screen.height - 48f) / Height);
            position.x = Mathf.Clamp(position.x, 8, Mathf.Max(8, Screen.width - Width * scale - 8));
            position.y = Mathf.Clamp(position.y, 8, Mathf.Max(8, Screen.height - Height * scale - 8));
            Window.anchoredPosition = new Vector2(position.x, -position.y);
            Window.localScale = new Vector3(scale, scale, 1f);
        }
        public void Dispose()
        {
            if (canvas != null)
                UnityEngine.Object.Destroy(canvas);
        }
    }
}
