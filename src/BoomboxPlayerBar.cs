using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxPlayerBar
    {
        private static readonly BoomboxGameMusicMode[] Modes = {
            BoomboxGameMusicMode.Normal,
            BoomboxGameMusicMode.Quiet,
            BoomboxGameMusicMode.Off
        };
        private readonly BoomboxPlugin plugin;
        private readonly BoomboxMenuLabels labels = new BoomboxMenuLabels();
        private readonly TMP_Text track, status, empty, volumeValue, time, repeat;
        private readonly Button previous, play, next;
        private readonly Button[] modes = new Button[3];
        private readonly Slider volume, seek;
        private readonly BoomboxSliderHold seekHold;
        private float? dragged;
        private MusicLiveStream draggedStream;
        private readonly Component[] controls;


        internal BoomboxPlayerBar(BoomboxPlugin owner, RectTransform window)
        {
            plugin = owner;
            track = BoomboxNativeControls.Line("PlayerTrack", window, 20, true);
            BoomboxNativeUI.Place(track.rectTransform, 24, 772, 560, 30);
            status = BoomboxNativeControls.Line("PlayerStatus", window, 14);
            status.alignment = TextAlignmentOptions.MidlineRight;
            BoomboxNativeUI.Place(status.rectTransform, 596, 772, 440, 30);
            empty = labels.Add(
                BoomboxNativeControls.Line("PlayerEmpty", window, 16),
                () => BoomboxText.Get("text.take_a_boombox_into_your_inventory_and")
            );
            BoomboxNativeUI.Place(empty.rectTransform, 24, 772, 1012, 30);
            previous = BoomboxNativeControls.IconButton("PlayerPrevious", window, "previous", () => Step(-1));
            BoomboxNativeUI.Place((RectTransform)previous.transform, 24, 808, 40, 36);
            play = BoomboxNativeControls.TextButton("PlayerPlay", window, "", () => plugin.Current?.TogglePlayback());
            BoomboxNativeUI.Place((RectTransform)play.transform, 68, 808, 120, 36);
            next = BoomboxNativeControls.IconButton("PlayerNext", window, "next", () => Step(1));
            BoomboxNativeUI.Place((RectTransform)next.transform, 192, 808, 40, 36);
            var volumeLabel = labels.Add(
                BoomboxNativeControls.Line("PlayerVolumeLabel", window, 16),
                () => BoomboxText.Get("text.volume")
            );
            BoomboxNativeUI.Place(volumeLabel.rectTransform, 244, 808, 100, 36);
            volume = BoomboxNativeControls.Slider("PlayerVolume", window, SetVolume);
            BoomboxNativeUI.Place((RectTransform)volume.transform, 348, 816, 180, 20);
            volumeValue = BoomboxNativeControls.Line("PlayerVolumeValue", window, 16);
            volumeValue.alignment = TextAlignmentOptions.Center;
            BoomboxNativeUI.Place(volumeValue.rectTransform, 532, 808, 56, 36);
            time = BoomboxNativeControls.Line("PlayerTime", window, 16);
            time.alignment = TextAlignmentOptions.Center;
            BoomboxNativeUI.Place(time.rectTransform, 594, 808, 110, 36);
            seek = BoomboxNativeControls.Slider("PlayerSeek", window, Seek);
            BoomboxNativeUI.Place((RectTransform)seek.transform, 718, 816, 318, 20);
            seekHold = seek.gameObject.AddComponent<BoomboxSliderHold>();
            seekHold.Released = Release;
            var musicLabel = labels.Add(
                BoomboxNativeControls.Line("GameMusicLabel", window, 16),
                () => BoomboxText.Get("text.game_music_during_playback")
            );
            BoomboxNativeUI.Place(musicLabel.rectTransform, 24, 850, 330, 32);
            Func<string>[] names = {
                () => BoomboxText.Get("text.normal"),
                () => BoomboxText.Get("text.quiet"),
                () => BoomboxText.Get("text.off_3")
            };
            string[] ids = { "GameMusicNormal", "GameMusicQuiet", "GameMusicOff" };
            for (int i = 0; i < modes.Length; i++)
            {
                var choice = Modes[i];
                modes[i] = labels.Add(
                    BoomboxNativeControls.TextButton(ids[i], window, "", () => BoomboxGameMusic.Mode = choice),
                    names[i]
                );
                BoomboxNativeUI.Place((RectTransform)modes[i].transform, 358 + i * 128, 850, 124, 32);
            }
            repeat = BoomboxNativeControls.Line("PlayerRepeat", window, 15);
            repeat.alignment = TextAlignmentOptions.MidlineRight;
            BoomboxNativeUI.Place(repeat.rectTransform, 750, 850, 286, 32);
            controls = new Component[] {
                track,
                status,
                previous,
                play,
                next,
                volumeLabel,
                volume,
                volumeValue,
                time,
                seek,
                musicLabel,
                modes[0],
                modes[1],
                modes[2],
                repeat
            };
        }

        internal void Refresh()
        {
            labels.Refresh();
            var deck = plugin.Current;
            BoomboxNativeControls.Show(empty, deck == null);
            foreach (var control in controls)
                BoomboxNativeControls.Show(control, deck != null);
            if (deck == null)
                return;
            var state = deck.State;
            track.text = deck.TrackLabel ?? BoomboxText.Get("text.select_a_track_from_the_library");
            status.text = deck.StoragePaused
                ? BoomboxText.Get("text.stored_in_inventory_sound_is_off_equip")
                : (deck.Status);
            bool transport = plugin.CanControl(deck)
                && !deck.StoragePaused
                && state.insertedCassette != null
                && state.tracks.Count > 0;
            previous.interactable = play.interactable = next.interactable = volume.interactable = transport;
            BoomboxNativeUI.Label(play, state.playing ? BoomboxText.Get("text.pause") : BoomboxText.Get("text.play"));
            volume.SetValueWithoutNotify(state.volume);
            volumeValue.text = Mathf.RoundToInt(state.volume * 100f) + " %";
            if (!seekHold.Held && !MousePressed || deck.Stream != draggedStream)
                Drop();
            var clip = deck.Source != null ? deck.Source.clip : null;
            BoomboxNativeControls.Show(time, clip != null);
            BoomboxNativeControls.Show(seek, clip != null);
            if (clip != null)
            {
                time.text = FormatTime(seekHold.Held || dragged.HasValue ? seek.value : state.seconds) + " / " + FormatTime(deck.TrackSeconds);
                seek.interactable = plugin.CanControl(deck) && !deck.Loading;
                float length = Mathf.Max(.01f, deck.TrackSeconds);
                if (seek.value > length)
                    seek.SetValueWithoutNotify(length);
                seek.maxValue = length;
                if (!seekHold.Held && !dragged.HasValue)
                    seek.SetValueWithoutNotify(state.seconds);
            }
            var mode = BoomboxGameMusic.Mode;
            for (int i = 0; i < modes.Length; i++)
            {
                BoomboxNativeUI.Select(modes[i], Modes[i] == mode);
                modes[i].GetComponentInChildren<TMP_Text>().color = Modes[i] == mode
                    ? BoomboxNativeUI.Gold
                    : BoomboxNativeUI.TextColor;
            }
            repeat.text = state.repeat
                ? BoomboxText.Get("text.playlist_repeat_on")
                : BoomboxText.Get("text.playlist_repeat_off");
        }

        private void Step(int delta)
        {
            var deck = plugin.Current;
            if (deck != null && plugin.CanControl(deck))
                deck.SelectTrack(deck.State.trackIndex + delta);
        }

        private void SetVolume(float value)
        {
            var deck = plugin.Current;
            if (deck == null || !plugin.CanControl(deck) || Mathf.Abs(value - deck.State.volume) <= .001f)
                return;
            deck.State.volume = value;
            plugin.Save();
        }

        private static bool MousePressed => Mouse.current != null && Mouse.current.leftButton.isPressed;

        private void Seek(float value)
        {
            var deck = plugin.Current;
            if (deck == null || !plugin.CanControl(deck))
                return;
            if (seekHold.Held || MousePressed)
            {
                dragged = value;
                draggedStream = deck.Stream;
                return;
            }
            if (Mathf.Abs(value - deck.State.seconds) <= .025f)
                return;
            deck.Seek(value);
        }

        private void Release()
        {
            var deck = plugin.Current;
            float? target = dragged;
            var stream = draggedStream;
            Drop();
            if (target.HasValue && deck != null && deck.Stream == stream && plugin.CanControl(deck)
            && Mathf.Abs(target.Value - deck.State.seconds) > .025f)
                deck.Seek(target.Value);
        }

        private void Drop()
        {
            dragged = null;
            draggedStream = null;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return total / 60 + ":" + (total % 60).ToString("00");
        }
    }
}
