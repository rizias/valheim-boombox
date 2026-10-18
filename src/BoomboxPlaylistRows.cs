using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxPlaylistTrackRow
    {
        private readonly RectTransform root;
        private readonly Button main, up, down, remove;
        private Action onMain, onUp, onDown, onRemove;

        internal BoomboxPlaylistTrackRow(Transform parent, int index, BoomboxPlaylistDrag drag)
        {
            root = BoomboxNativeControls.Row("PlaylistTrack" + index, parent, 32);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var handle = root.gameObject.AddComponent<BoomboxDragRow>();
            handle.Owner = drag;
            handle.Index = index;
            main = BoomboxNativeControls.TextButton("PlaylistTrackPlay", root, "", () => onMain?.Invoke());
            var text = main.GetComponentInChildren<TMP_Text>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 16;
            BoomboxNativeControls.Stretch((RectTransform)main.transform, 0, 112, 0, 32);
            up = BoomboxNativeControls.IconButton("PlaylistTrackUp", root, "up", () => onUp?.Invoke());
            BoomboxNativeControls.Right((RectTransform)up.transform, 72, 0, 32, 32);
            down = BoomboxNativeControls.IconButton("PlaylistTrackDown", root, "down", () => onDown?.Invoke());
            BoomboxNativeControls.Right((RectTransform)down.transform, 36, 0, 32, 32);
            remove = BoomboxNativeControls.IconButton("PlaylistTrackRemove", root, "cross", () => onRemove?.Invoke());
            BoomboxNativeControls.Right((RectTransform)remove.transform, 0, 0, 32, 32);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != handle
                && (behaviour is IDragHandler || behaviour is IBeginDragHandler || behaviour is IEndDragHandler
                || behaviour is IInitializePotentialDragHandler))
                    UnityEngine.Object.DestroyImmediate(behaviour);
        }

        internal void Bind(
            string label,
            bool current,
            bool playable,
            bool first,
            bool last,
            Action play,
            Action raise,
            Action lower,
            Action delete
        )
        {
            BoomboxNativeControls.Show(root, true);
            onMain = play;
            onUp = raise;
            onDown = lower;
            onRemove = delete;
            BoomboxNativeUI.Label(main, current ? Golden(label) : label);
            main.interactable = playable;
            up.interactable = !first;
            down.interactable = !last;
        }

        internal void Hide() => BoomboxNativeControls.Show(root, false);

        internal static string Golden(
            string text
        ) => "<color=#" + ColorUtility.ToHtmlStringRGB(BoomboxNativeUI.Gold) + "><noparse>" + text
            + "</noparse></color>";
    }

    internal sealed class BoomboxPlaylistEntry
    {
        private readonly Button button;
        private readonly Image mark;
        private Action onClick;

        internal BoomboxPlaylistEntry(Transform parent, int index)
        {
            button = BoomboxNativeControls.TextButton("PlaylistEntry" + index, parent, "", () => onClick?.Invoke());
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
            var text = button.GetComponentInChildren<TMP_Text>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 16;
            text.rectTransform.offsetMax = new Vector2(-36, -2);
            mark = BoomboxNativeUI.Image("PlayingMark", button.transform, null, BoomboxNativeUI.Gold);
            mark.type = Image.Type.Simple;
            mark.preserveAspect = true;
            BoomboxNativeControls.Right(mark.rectTransform, 12, 9, 18, 18);
        }

        internal void Bind(string label, bool edited, bool loaded, bool playing, Action click)
        {
            BoomboxNativeControls.Show(button, true);
            onClick = click;
            BoomboxNativeUI.Label(button, loaded ? BoomboxPlaylistTrackRow.Golden(label) : label);
            BoomboxNativeUI.Select(button, edited);
            mark.enabled = loaded;
            if (loaded)
                mark.sprite = BoomboxPlayerIcons.Get(playing ? "play" : "pause");
        }

        internal void Hide() => BoomboxNativeControls.Show(button, false);
    }
}
