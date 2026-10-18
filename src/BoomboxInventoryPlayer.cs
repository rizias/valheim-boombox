using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxInventoryPlayer
    {
        private const float Width = 800, Height = 170;
        private readonly BoomboxPlugin plugin;
        private readonly RectTransform parent;
        private readonly GameObject canvas;
        private readonly TMP_Text heading;
        private readonly TMP_Text source;
        private readonly Button bonuses;
        private readonly Button musicButton;
        private float scale;
        private Vector2? dragged;
        private readonly Button previous, play, next, repeat, mute, quieter, louder;
        private readonly TMP_Text volume, title;
        private readonly Image playIcon;
        private readonly Dictionary<Button, Sprite> normalSprites = new Dictionary<Button, Sprite>();
        private readonly GameObject[] elements;
        internal bool Alive => previous != null && title != null;

        internal BoomboxInventoryPlayer(BoomboxPlugin owner, InventoryGui gui, Button music)
        {
            plugin = owner;
            musicButton = music;
            canvas = new GameObject(
                "BoomboxInventoryPlayerCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster)
            );
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 950;
            parent = BoomboxNativeUI.Rect("BoomboxFloatingPlayer", canvas.transform);
            var background = BoomboxNativeUI.Image(
                "Background",
                parent,
                BoomboxNativeUI.Wood,
                new Color(.55f, .6f, .65f)
            );
            BoomboxNativeUI.Place(background.rectTransform, 0, 0, Width, Height);
            background.raycastTarget = true;
            heading = BoomboxNativeUI.FixedText("PlayerHeading", parent, 18, true);
            BoomboxNativeUI.Place(heading.rectTransform, 20, 10, 740, 25);
            var handle = BoomboxNativeUI.Image("PlayerDragHandle", parent, null, Color.clear);
            BoomboxNativeUI.Place(handle.rectTransform, 0, 0, Width, 36);
            handle.raycastTarget = true;
            var drag = handle.gameObject.AddComponent<BoomboxWindowDrag>();
            drag.Moved = delta =>
            {
                var position = parent.anchoredPosition + delta;
                dragged = new Vector2(position.x / Screen.width, -position.y / Screen.height);
                Layout(gui);
            };
            drag.Finished = () =>
            {
                if (dragged.HasValue)
                {
                    plugin.Settings.PlayerPanelPosition.Value = new Vector2(
                        parent.anchoredPosition.x / Screen.width,
                        -parent.anchoredPosition.y / Screen.height
                    );
                    dragged = null;
                }
            };
            music.transform.SetParent(parent, false);
            bonuses = BoomboxNativeUI.CleanButton("OpenBonuses", parent, "", plugin.OpenBonuses);
            source = BoomboxNativeUI.FixedText("PlayerSource", parent, 17);
            BoomboxNativeUI.Place(source.rectTransform, 20, 74, 760, 26);
            previous = IconButton(gui, "BoomboxPlayerPrevious", "previous", () => Step(-1));
            play = IconButton(gui, "BoomboxPlayerPlay", "play", Play);
            next = IconButton(gui, "BoomboxPlayerNext", "next", () => Step(1));
            repeat = IconButton(
                gui,
                "BoomboxPlayerRepeat",
                "repeat",
                () => Change(state => state.repeat = !state.repeat)
            );
            mute = IconButton(gui, "BoomboxPlayerMute", "sound", () => Change(state => state.muted = !state.muted));
            quieter = IconButton(
                gui,
                "BoomboxPlayerQuieter",
                "minus",
                () => Change(state => state.volume = Mathf.Clamp01(Mathf.Round((state.volume - .1f) * 100f) / 100f))
            );
            louder = IconButton(
                gui,
                "BoomboxPlayerLouder",
                "plus",
                () => Change(state => state.volume = Mathf.Clamp01(Mathf.Round((state.volume + .1f) * 100f) / 100f))
            );
            volume = BoomboxNativeUI.FixedText("BoomboxPlayerVolume", parent, 19);
            volume.alignment = TextAlignmentOptions.Center;
            title = BoomboxNativeUI.FixedText("BoomboxPlayerTrack", parent, 26, true);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.overflowMode = TextOverflowModes.Ellipsis;
            playIcon = play.transform.Find("Icon").GetComponent<Image>();
            elements = new[] {
                previous.gameObject,
                play.gameObject,
                next.gameObject,
                repeat.gameObject,
                mute.gameObject,
                quieter.gameObject,
                louder.gameObject,
                volume.gameObject,
                title.gameObject
            };
            canvas.SetActive(false);
        }

        internal void Update(Vector2 music, bool visible)
        {
            bool shown = visible && plugin.Settings.ShowPlayerPanel.Value;
            canvas.SetActive(shown);
            if (!shown)
                return;
            Layout(InventoryGui.instance);
            var deck = visible ? plugin.Items.Own : null;
            heading.text = deck != null && deck.State.playing
                ? BoomboxText.Get("text.now_playing")
                : BoomboxText.Get("text.playback_paused");
            BoomboxNativeUI.Label(bonuses, BoomboxText.Get("text.bonuses"));
            source.text = deck?.State.TrackId == BoomboxSyntheticTrackId.ComputerAudio
                ? BoomboxText.Get("text.windows_audio_analysis_no_duplicate_playback")
                : BoomboxText.Get("text.local_playback");
            foreach (var element in elements)
                if (element.activeSelf != (deck != null))
                    element.SetActive(deck != null);
            if (deck == null)
                return;
            float left = 20f, top = 110f;
            Place((RectTransform)previous.transform, left, top, 44, 44);
            Place((RectTransform)play.transform, left + 50, top, 44, 44);
            Place((RectTransform)next.transform, left + 100, top, 44, 44);
            Place((RectTransform)repeat.transform, left + 150, top, 44, 44);
            Place((RectTransform)mute.transform, left + 200, top, 44, 44);
            Place((RectTransform)quieter.transform, left + 250, top, 44, 44);
            Place(volume.rectTransform, left + 300, top, 60, 44);
            Place((RectTransform)louder.transform, left + 366, top, 44, 44);
            Place(title.rectTransform, left, 39, 760, 34);
            playIcon.sprite = BoomboxPlayerIcons.Get(deck.State.playing ? "pause" : "play");
            volume.text = Mathf.RoundToInt(deck.State.volume * 100f) + " %";
            string name = deck.State.TrackId == null ? null : deck.TrackTitle;
            title.text = string.IsNullOrEmpty(name) ? BoomboxText.Get("text.no_track_selected") : name;
            Highlight(repeat, deck.State.repeat);
            Highlight(mute, deck.State.muted);
        }

        private void Layout(InventoryGui gui)
        {
            scale = Mathf.Min(Screen.height / 1080f, (Screen.width - 16f) / Width);
            var saved = dragged ?? plugin.Settings.PlayerPanelPosition.Value;
            float x = saved.x * Screen.width, y = saved.y * Screen.height;
            if (saved.x < 0 || saved.y < 0)
            {
                var corners = new Vector3[4];
                gui.m_player.GetWorldCorners(corners);
                var sourceCanvas = gui.m_player.GetComponentInParent<Canvas>();
                var camera = sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceCanvas.worldCamera;
                var point = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                x = point.x;
                y = Screen.height - point.y + 80f * scale;
            }
            x = Mathf.Clamp(x, 8f, Mathf.Max(8f, Screen.width - Width * scale - 8f));
            y = Mathf.Clamp(y, 8f, Mathf.Max(8f, Screen.height - Height * scale - 8f));
            BoomboxNativeUI.Place(parent, x, y, Width, Height);
            parent.localScale = Vector3.one * scale;
            BoomboxNativeUI.Place((RectTransform)musicButton.transform, 446, 110, 174, 44);
            BoomboxNativeUI.Place((RectTransform)bonuses.transform, 632, 110, 148, 44);
        }

        private Button IconButton(InventoryGui gui, string name, string icon, Action action)
        {
            var button = UnityEngine.Object.Instantiate(gui.m_tabCraft, parent, false);
            button.name = name;
            button.gameObject.SetActive(true);
            button.interactable = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => action());
            var sprites = button.spriteState;
            sprites.selectedSprite = null;
            button.spriteState = sprites;
            BoomboxNativeUI.KeepNative(button.gameObject);
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
            rect.sizeDelta = new Vector2(24, 24);
            rect.anchoredPosition = Vector2.zero;
            normalSprites[button] = button.image.sprite;
            return button;
        }

        private void Highlight(Button button, bool selected)
        {
            var sprite = selected ? button.spriteState.highlightedSprite : normalSprites[button];
            if (button.image.sprite != sprite)
                button.image.sprite = sprite;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        private AudioDeck Controlled()
        {
            var deck = plugin.Items.Own;
            return deck != null && plugin.CanControl(deck) ? deck : null;
        }

        private void Step(int delta)
        {
            var deck = Controlled();
            if (deck != null && deck.State.tracks.Count > 0)
                deck.SelectTrack(deck.State.trackIndex + delta);
        }

        private void Play()
        {
            var deck = Controlled();
            if (deck == null)
                return;
            if (!deck.State.playing && deck.State.insertedCassette == null)
            {
                Player.m_localPlayer?.Message(
                    MessageHud.MessageType.Center,
                    BoomboxText.Get("text.insert_a_cassette_2")
                );
                return;
            }
            plugin.ToggleOwnPlayback();
        }

        private void Change(Action<DeckState> change)
        {
            var deck = Controlled();
            if (deck == null)
                return;
            change(deck.State);
            plugin.Save();
        }

        internal void Dispose()
        {
            if (elements == null)
                return;
            foreach (var element in elements)
                if (element != null)
                    UnityEngine.Object.Destroy(element);
            if (canvas != null)
                UnityEngine.Object.Destroy(canvas);
        }
    }
}
