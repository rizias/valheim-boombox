using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxPlaylistsTab
    {
        private readonly BoomboxPlugin plugin;
        private readonly List<string> selection;
        private readonly BoomboxMenuLabels labels = new BoomboxMenuLabels();
        private readonly List<BoomboxPlaylistEntry> entries = new List<BoomboxPlaylistEntry>();
        private readonly List<BoomboxPlaylistTrackRow> rows = new List<BoomboxPlaylistTrackRow>();
        private readonly RectTransform root, entryContent, trackContent;
        private readonly TMP_InputField newName, name;
        private readonly Button create, rename, play, addSelected, delete, confirmDelete, cancelDelete;
        private readonly TMP_Text state, empty, hint, question, notice;
        private readonly Component[] editor;
        private readonly BoomboxPlaylistDrag drag;
        private UserPlaylist edited;
        private bool confirming;
        private string listSignature, trackSignature;


        internal BoomboxPlaylistsTab(BoomboxPlugin owner, RectTransform window, List<string> sharedSelection)
        {
            plugin = owner;
            selection = sharedSelection;
            root = BoomboxNativeUI.Rect("BoomboxPlaylistsWindow", window);
            BoomboxNativeUI.Place(root, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            var title = labels.Add(
                BoomboxNativeControls.Line("PlaylistsTitle", root, 22, true),
                () => BoomboxText.Get("text.my_playlists")
            );
            BoomboxNativeUI.Place(title.rectTransform, 24, 116, 330, 32);
            newName = labels.Add(
                BoomboxNativeControls.Input("PlaylistNewName", root, 60),
                () => BoomboxText.Get("text.new_playlist_name")
            );
            BoomboxNativeUI.Place((RectTransform)newName.transform, 24, 156, 330, 32);
            create = labels.Add(
                BoomboxNativeControls.TextButton("PlaylistCreate", root, "", Create),
                () => BoomboxText.Get("text.create_playlist")
            );
            BoomboxNativeUI.Place((RectTransform)create.transform, 24, 196, 330, 32);
            var entryList = BoomboxNativeControls.List("PlaylistList", root, out entryContent);
            BoomboxNativeUI.Place((RectTransform)entryList.transform, 24, 236, 330, 424);
            var nameLabel = labels.Add(
                BoomboxNativeControls.Line("PlaylistNameLabel", root, 16),
                () => BoomboxText.Get("text.name")
            );
            BoomboxNativeUI.Place(nameLabel.rectTransform, 374, 116, 100, 32);
            name = labels.Add(
                BoomboxNativeControls.Input("PlaylistName", root, 60),
                () => BoomboxText.Get("text.playlist_name")
            );
            BoomboxNativeUI.Place((RectTransform)name.transform, 478, 116, 368, 32);
            rename = labels.Add(
                BoomboxNativeControls.TextButton("PlaylistRename", root, "", Rename),
                () => BoomboxText.Get("text.rename")
            );
            BoomboxNativeUI.Place((RectTransform)rename.transform, 850, 116, 186, 32);
            play = labels.Add(
                BoomboxNativeControls.TextButton(
                    "PlaylistPlay",
                    root,
                    "",
                    () => plugin.ListenPlaylist(plugin.Current, edited.tracks, edited.id)
                ),
                () => BoomboxText.Get("text.play_playlist_2")
            );
            BoomboxNativeUI.Place((RectTransform)play.transform, 374, 156, 220, 32);
            state = BoomboxNativeControls.Line("PlaylistState", root, 16, true);
            BoomboxNativeUI.Place(state.rectTransform, 606, 156, 430, 32);
            addSelected = BoomboxNativeControls.TextButton("PlaylistAddSelected", root, "", AddSelected);
            BoomboxNativeUI.Place((RectTransform)addSelected.transform, 374, 196, 662, 32);
            var trackList = BoomboxNativeControls.List("PlaylistTracks", root, out trackContent);
            BoomboxNativeUI.Place((RectTransform)trackList.transform, 374, 236, 662, 416);
            drag = new BoomboxPlaylistDrag(
                trackList,
                trackContent,
                (from, to) => BoomboxPlaylistQueue.Move(plugin, edited, from, to)
            );
            empty = labels.Add(
                BoomboxNativeControls.Line("PlaylistEmpty", root, 18),
                () => BoomboxText.Get("text.add_tracks_with_the_button_in_the_library")
            );
            empty.alignment = TextAlignmentOptions.Center;
            BoomboxNativeUI.Place(empty.rectTransform, 374, 400, 662, 32);
            hint = labels.Add(
                BoomboxNativeUI.FixedText("PlaylistHint", root, 14),
                () => BoomboxText.Get("text.drag_a_track_to_change_the_order")
            );
            BoomboxNativeUI.Place(hint.rectTransform, 374, 660, 450, 40);
            delete = labels.Add(
                BoomboxNativeControls.TextButton("PlaylistDelete", root, "", () => confirming = true),
                () => BoomboxText.Get("text.delete_playlist")
            );
            BoomboxNativeUI.Place((RectTransform)delete.transform, 836, 660, 200, 32);
            question = labels.Add(
                BoomboxNativeUI.FixedText("PlaylistDeleteQuestion", root, 14),
                () => BoomboxText.Get("text.delete_this_playlist_music_files_will_remain_unchanged")
            );
            BoomboxNativeUI.Place(question.rectTransform, 374, 660, 420, 40);
            confirmDelete = labels.Add(
                BoomboxNativeControls.TextButton("PlaylistDeleteConfirm", root, "", Delete),
                () => BoomboxText.Get("text.delete")
            );
            BoomboxNativeUI.Place((RectTransform)confirmDelete.transform, 800, 660, 110, 32);
            cancelDelete = labels.Add(
                BoomboxNativeControls.TextButton("PlaylistDeleteCancel", root, "", () => confirming = false),
                () => BoomboxText.Get("text.cancel")
            );
            BoomboxNativeUI.Place((RectTransform)cancelDelete.transform, 916, 660, 120, 32);
            notice = BoomboxNativeControls.Line("PlaylistNotice", root, 14);
            BoomboxNativeUI.Place(notice.rectTransform, 24, 712, 1012, 28);
            editor = new Component[] { nameLabel, name, rename, play, state, trackList };
        }

        internal void Show(bool visible)
        {
            BoomboxNativeControls.Show(root, visible);
            if (!visible)
            {
                drag.Cancel();
                return;
            }
            Refresh();
        }

        private void Refresh()
        {
            labels.Refresh();
            var library = plugin.MusicLibrary;
            if (edited != null && !library.playlists.Contains(edited))
            {
                edited = null;
                confirming = false;
            }
            var deck = plugin.Current;
            bool control = deck != null && plugin.CanControl(deck);
            create.interactable = !string.IsNullOrWhiteSpace(newName.text);
            string loaded = deck?.State.playlistId;
            string list = string.Join(
                "|",
                library.playlists.Select(p => p.id + ":" + p.name + ":" + p.tracks.Count)
            )
                + "#"
                + edited?.id
                + "#"
                + loaded
                + "#"
                + deck?.State.playing
                + "#"
                + Localization.instance.GetSelectedLanguage();
            if (list != listSignature)
            {
                listSignature = list;
                BindEntries(loaded, deck != null && deck.State.playing);
            }
            foreach (var component in editor)
                BoomboxNativeControls.Show(component, edited != null);
            BoomboxNativeControls.Show(addSelected, edited != null && selection.Count > 0);
            BoomboxNativeControls.Show(empty, edited != null && edited.tracks.Count == 0);
            BoomboxNativeControls.Show(delete, edited != null && !confirming);
            BoomboxNativeControls.Show(question, edited != null && confirming);
            BoomboxNativeControls.Show(confirmDelete, edited != null && confirming);
            BoomboxNativeControls.Show(cancelDelete, edited != null && confirming);
            BoomboxNativeControls.Show(hint, edited != null && !confirming);
            notice.text = (plugin.Notice);
            if (edited == null)
                return;
            bool isLoaded = loaded == edited.id;
            state.text = !isLoaded
                ? ""
                : deck.State.playing
                    ? BoomboxText.Get("text.now_playing")
                    : BoomboxText.Get("text.in_the_boombox_paused");
            rename.interactable = !string.IsNullOrWhiteSpace(name.text);
            play.interactable = control && edited.tracks.Count > 0;
            BoomboxNativeUI.Label(
                addSelected,
                BoomboxText.Get("text.add_selected_tracks") + selection.Count
            );
            int currentIndex = isLoaded ? deck.State.trackIndex : -1;
            string tracks = edited.id + "#" + string.Join("|", edited.tracks) + "#" + currentIndex + "#" + control + "#"
                + plugin.CatalogRevision
                + "#"
                + Localization.instance.GetSelectedLanguage();
            if (tracks != trackSignature)
            {
                trackSignature = tracks;
                BindTracks(currentIndex, control);
            }
            drag.Tick();
        }

        private void BindEntries(string loaded, bool playing)
        {
            var playlists = plugin.MusicLibrary.playlists;
            for (int i = 0; i < playlists.Count; i++)
            {
                while (entries.Count <= i)
                    entries.Add(new BoomboxPlaylistEntry(entryContent, entries.Count));
                var playlist = playlists[i];
                entries[i].Bind(
                    playlist.name + " · " + playlist.tracks.Count,
                    playlist == edited,
                    playlist.id == loaded,
                    playing,
                    () => Edit(playlist)
                );
            }
            for (int i = playlists.Count; i < entries.Count; i++)
                entries[i].Hide();
        }

        private void BindTracks(int currentIndex, bool control)
        {
            var tracks = edited.tracks;
            drag.Count = tracks.Count;
            for (int i = 0; i < tracks.Count; i++)
            {
                while (rows.Count <= i)
                    rows.Add(new BoomboxPlaylistTrackRow(trackContent, rows.Count, drag));
                int index = i;
                string label = (i + 1) + ". "
                    + (plugin.FindTrack(tracks[i])?.Label ?? BoomboxText.Get("text.unavailable_track"));
                rows[i].Bind(label, i == currentIndex, control, i == 0, i + 1 == tracks.Count,
                    () => plugin.PlayQueue(plugin.Current, edited.tracks, edited.id, index),
                    () => BoomboxPlaylistQueue.Move(plugin, edited, index, index - 1),
                    () => BoomboxPlaylistQueue.Move(plugin, edited, index, index + 1),
                    () => BoomboxPlaylistQueue.Remove(plugin, edited, index));
            }
            for (int i = tracks.Count; i < rows.Count; i++)
                rows[i].Hide();
        }

        private void Edit(UserPlaylist playlist)
        {
            edited = playlist;
            confirming = false;
            name.text = playlist.name;
            trackSignature = null;
        }

        private void Create()
        {
            if (string.IsNullOrWhiteSpace(newName.text))
                return;
            Edit(plugin.MusicLibrary.Create(newName.text, new string[0]));
            newName.text = "";
        }

        private void Rename()
        {
            if (edited == null || string.IsNullOrWhiteSpace(name.text))
                return;
            BoomboxPlaylistQueue.Rename(plugin, edited, name.text);
        }

        private void AddSelected()
        {
            if (edited == null || selection.Count == 0)
                return;
            BoomboxPlaylistQueue.Append(plugin, edited, selection.ToList());
            selection.Clear();
        }

        private void Delete()
        {
            if (edited != null)
                plugin.MusicLibrary.Delete(edited);
            edited = null;
            confirming = false;
        }
    }
}
