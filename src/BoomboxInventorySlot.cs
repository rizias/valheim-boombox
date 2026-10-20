using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    public sealed class BoomboxInventorySlot
    {
        public Rect SlotRect
        {
            get;
            private set;
        }
        public Rect PanelRect
        {
            get;
            private set;
        }
        private readonly BoomboxPlugin plugin;
        private float nextLayout;
        private int screenWidth;
        private int screenHeight;
        private bool lastVisible;
        private Button musicButton;
        private BoomboxInventoryPlayer player;
        public BoomboxInventorySlot(BoomboxPlugin owner)
        {
            plugin = owner;
        }
        public bool InventoryVisible
        {
            get
            {
                InventoryGui.IsVisible();
                return plugin.NativeInventoryVisible
                    && InventoryGui.instance != null
                    && InventoryGui.instance.m_player != null
                    && InventoryGui.instance.m_player.gameObject.activeInHierarchy;
            }
        }
        public string StateText => plugin.Items.Own == null
            ? BoomboxText.Get("text.empty_2")
            : plugin.Items.Own.StoragePaused
                ? BoomboxText.Get("text.in_backpack")
                : plugin.Items.Own.State.onBack
                    ? BoomboxText.Get("text.equipped")
                    : BoomboxText.Get("text.placed");
        public void Draw()
        {
            RefreshLayout();
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null || gui.m_craftButton == null)
                return;
            if (musicButton == null)
            {
                player?.Dispose();
                player = null;
                musicButton = BoomboxNativeUI.CleanButton("BoomboxMusicButton", gui.m_player, "", plugin.OpenLibrary);
                musicButton.name = "BoomboxMusicButton";
                musicButton.onClick = new Button.ButtonClickedEvent();
                musicButton.onClick.AddListener(() => plugin.OpenLibrary());
                var rect = (RectTransform)musicButton.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
                rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-10, -10);
                rect.sizeDelta = new Vector2(160, 40);
            }
            if (player == null || !player.Alive)
                player = new BoomboxInventoryPlayer(plugin, gui, musicButton);
            musicButton.interactable = true;
            SetButtonText(musicButton, BoomboxText.Get("text.choose_music"));
            player.Update(
                InventoryVisible && (BoomboxEquipment.Known || plugin.Items.Own != null)
            );
        }
        public void ResetPlayer()
        {
            player?.ResetPosition();
            plugin.Settings.PlayerPanelPosition.Value = new Vector2(-1, -1);
            plugin.Settings.ShowPlayerPanel.Value = true;
        }
        private static void SetButtonText(Button button, string text)
        {
            var labels = button.GetComponentsInChildren<TMPro.TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].gameObject.SetActive(i == 0);
                if (i == 0)
                    labels[i].text = text;
            }
        }
        public void RefreshLayout()
        {
            bool visible = InventoryVisible;
            if (Time.unscaledTime < nextLayout && screenWidth == Screen.width && screenHeight == Screen.height
            && visible == lastVisible)
                return;
            nextLayout = Time.unscaledTime + 0.2f;
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            lastVisible = visible;
            var screen = new Rect(12f, 12f, Screen.width - 24f, Screen.height - 24f);
            PanelRect = new Rect(
                screen.xMax - Mathf.Min(940f, screen.width),
                screen.y,
                Mathf.Min(940f, screen.width),
                Mathf.Min(700f, screen.height)
            );
            if (!visible)
                return;
            SlotRect = NativeSlotRect();
        }
        private static Rect ScreenRect(RectTransform transform)
        {
            var corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            var canvas = transform.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            var points = corners.Select(p => RectTransformUtility.WorldToScreenPoint(camera, p)).ToArray();
            return Rect.MinMaxRect(
                points.Min(p => p.x),
                Screen.height - points.Max(p => p.y),
                points.Max(p => p.x),
                Screen.height - points.Min(p => p.y)
            );
        }
        private static Rect NativeSlotRect()
        {
            var position = BoomboxEquipment.SlotPosition;
            var grid = InventoryGui.instance.m_playerGrid;
            if (!position.HasValue || grid == null)
                return new Rect();
            var element = (InventoryElement)AccessTools.Method(typeof(InventoryGrid), "GetElement").Invoke(
                grid,
                new object[] { position.Value.x, position.Value.y, Player.m_localPlayer.GetInventory().GetWidth() }
            );
            return element == null ? new Rect() : ScreenRect(element.GetElementRectTransform());
        }
        public void Dispose()
        {
            player?.Dispose();
            if (musicButton != null)
                Object.Destroy(musicButton.gameObject);
        }
    }
}
