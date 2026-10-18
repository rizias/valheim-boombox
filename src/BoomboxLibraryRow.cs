using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxLibraryRow
    {
        internal readonly RectTransform Root;
        internal RectTransform AddButton => (RectTransform)add.transform;
        private readonly Button add;
        private readonly BoomboxMenuLabels labels;
        private Button check, main, selectAlbum, playAlbum;
        private TMP_Text album;
        private Action onCheck, onMain, onAdd, onSelectAlbum, onPlayAlbum;


        internal BoomboxLibraryRow(Transform parent, int index, BoomboxMenuLabels labels)
        {
            this.labels = labels;
            Root = BoomboxNativeControls.Row("LibraryRow" + index, parent, 32);
            Root.gameObject.SetActive(false);
            add = BoomboxNativeControls.IconButton("TrackAdd", Root, "plus", () => onAdd?.Invoke());
            BoomboxNativeControls.Right((RectTransform)add.transform, 0, 0, 32, 32);
        }

        private void EnsureTrack()
        {
            if (main != null)
                return;
            check = BoomboxNativeControls.IconButton("TrackCheck", Root, "check", () => onCheck?.Invoke());
            BoomboxNativeUI.Place((RectTransform)check.transform, 0, 0, 32, 32);
            main = BoomboxNativeControls.TextButton("TrackPlay", Root, "", () => onMain?.Invoke());
            var text = main.GetComponentInChildren<TMP_Text>(true);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 16;
            BoomboxNativeControls.Stretch((RectTransform)main.transform, 36, 36, 0, 32);
        }

        private void EnsureAlbum()
        {
            if (album != null)
                return;
            album = BoomboxNativeControls.Line("AlbumTitle", Root, 15, true);
            BoomboxNativeControls.Stretch(album.rectTransform, 4, 250, 0, 32);
            selectAlbum = labels.Add(
                BoomboxNativeControls.TextButton("AlbumSelect", Root, "", () => onSelectAlbum?.Invoke()),
                () => BoomboxText.Get("text.select_album")
            );
            selectAlbum.GetComponentInChildren<TMP_Text>(true).fontSize = 16;
            BoomboxNativeControls.Right((RectTransform)selectAlbum.transform, 72, 0, 170, 32);
            playAlbum = BoomboxNativeControls.IconButton("AlbumPlay", Root, "play", () => onPlayAlbum?.Invoke());
            BoomboxNativeControls.Right((RectTransform)playAlbum.transform, 36, 0, 32, 32);
        }

        internal void Track(string label, bool selected, bool playable, Action toggle, Action play, Action plus)
        {
            EnsureTrack();
            onCheck = toggle;
            onMain = play;
            onAdd = plus;
            add.name = "TrackAdd";
            BoomboxNativeControls.Icon(check, "check", selected);
            BoomboxNativeUI.Select(check, selected);
            var text = main.GetComponentInChildren<TMP_Text>(true);
            text.text = "▶ " + label;
            main.interactable = playable;
            Kind(true);
        }

        internal void Album(string title, bool playable, Action select, Action play, Action plus)
        {
            EnsureAlbum();
            onSelectAlbum = select;
            onPlayAlbum = play;
            onAdd = plus;
            add.name = "AlbumAdd";
            album.text = title;
            playAlbum.interactable = playable;
            Kind(false);
        }

        internal void Hide()
        {
            BoomboxNativeControls.Show(Root, false);
            onCheck = onMain = onAdd = onSelectAlbum = onPlayAlbum = null;
        }

        private void Kind(bool track)
        {
            if (check != null)
                BoomboxNativeControls.Show(check, track);
            if (main != null)
                BoomboxNativeControls.Show(main, track);
            if (album != null)
                BoomboxNativeControls.Show(album, !track);
            if (selectAlbum != null)
                BoomboxNativeControls.Show(selectAlbum, !track);
            if (playAlbum != null)
                BoomboxNativeControls.Show(playAlbum, !track);
            BoomboxNativeControls.Show(Root, true);
        }
    }
}
