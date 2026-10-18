using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxLibraryTab
    {
        private const int PageSize = 50;
        private readonly BoomboxPlugin plugin;
        private readonly Func<BoomboxMenuPopup> popup;
        private readonly List<string> selection;
        private readonly BoomboxLibraryData data = new BoomboxLibraryData();
        private readonly BoomboxMenuLabels labels = new BoomboxMenuLabels();
        private readonly List<BoomboxLibraryRow> rows = new List<BoomboxLibraryRow>();
        private readonly RectTransform root, content;
        private readonly TMP_InputField search;
        private readonly Button folder, addSelected, previousPage, nextPage;
        private readonly TMP_Text selected, empty, page, notice;
        private string folderFilter;
        private int pageIndex;
        private string signature;
        private int dataVersion;
        private readonly List<Action<BoomboxLibraryRow>> binding = new List<Action<BoomboxLibraryRow>>();
        private readonly System.Diagnostics.Stopwatch buildClock = new System.Diagnostics.Stopwatch();
        private int boundRows;
        private bool building;


        internal BoomboxLibraryTab(
            BoomboxPlugin owner,
            RectTransform window,
            Func<BoomboxMenuPopup> menuPopup,
            List<string> sharedSelection
        )
        {
            plugin = owner;
            popup = menuPopup;
            selection = sharedSelection;
            root = BoomboxNativeUI.Rect("BoomboxLibraryWindow", window);
            root.gameObject.SetActive(false);
            BoomboxNativeUI.Place(root, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            var title = labels.Add(
                BoomboxNativeControls.Line("LibraryTitle", root, 22, true),
                () => BoomboxText.Get("text.music_library")
            );
            BoomboxNativeUI.Place(title.rectTransform, 24, 116, 1012, 32);
            search = labels.Add(
                BoomboxNativeControls.Input("LibrarySearch", root, 80),
                () => BoomboxText.Get("text.search_by_title_album_or_file")
            );
            search.onValueChanged.AddListener(value => pageIndex = 0);
            BoomboxNativeUI.Place((RectTransform)search.transform, 24, 156, 560, 32);
            folder = BoomboxNativeControls.TextButton("LibraryFolder", root, "", OpenFolders);
            BoomboxNativeUI.Place((RectTransform)folder.transform, 596, 156, 440, 32);
            var selectResults = labels.Add(
                BoomboxNativeControls.TextButton("LibrarySelectResults", root, "", SelectResults),
                () => BoomboxText.Get("text.select_results")
            );
            BoomboxNativeUI.Place((RectTransform)selectResults.transform, 24, 196, 200, 32);
            var clearSelection = labels.Add(
                BoomboxNativeControls.TextButton("LibraryClearSelection", root, "", selection.Clear),
                () => BoomboxText.Get("text.clear_selection")
            );
            BoomboxNativeUI.Place((RectTransform)clearSelection.transform, 228, 196, 160, 32);
            selected = BoomboxNativeControls.Line("LibrarySelected", root, 16);
            BoomboxNativeUI.Place(selected.rectTransform, 400, 196, 190, 32);
            addSelected = labels.Add(
                BoomboxNativeControls.TextButton(
                    "LibraryAddSelected",
                    root,
                    "",
                    () => OpenPlaylists(addSelected, selection.ToList(), true)
                ),
                () => BoomboxText.Get("text.add_selected_to_playlist")
            );
            BoomboxNativeUI.Place((RectTransform)addSelected.transform, 596, 196, 440, 32);
            var list = BoomboxNativeControls.List("LibraryList", root, out content);
            BoomboxNativeUI.Place((RectTransform)list.transform, 24, 236, 1012, 424);
            empty = labels.Add(
                BoomboxNativeControls.Line("LibraryEmpty", root, 16),
                () => BoomboxText.Get("text.no_tracks_add_folders_and_scan_in_the_folders_tab")
            );
            BoomboxNativeUI.Place(empty.rectTransform, 24, 236, 1012, 32);
            previousPage = BoomboxNativeControls.IconButton("LibraryPreviousPage", root, "back", () => pageIndex--);
            BoomboxNativeUI.Place((RectTransform)previousPage.transform, 24, 668, 40, 32);
            page = BoomboxNativeControls.Line("LibraryPage", root, 16);
            page.alignment = TextAlignmentOptions.Center;
            BoomboxNativeUI.Place(page.rectTransform, 68, 668, 240, 32);
            nextPage = BoomboxNativeControls.IconButton("LibraryNextPage", root, "play", () => pageIndex++);
            BoomboxNativeUI.Place((RectTransform)nextPage.transform, 312, 668, 40, 32);
            notice = BoomboxNativeControls.Line("LibraryNotice", root, 14);
            BoomboxNativeUI.Place(notice.rectTransform, 24, 712, 1012, 28);
        }

        internal void Show(bool visible)
        {
            BoomboxNativeControls.Show(root, visible);
            if (!visible && building)
            {
                building = false;
                binding.Clear();
                signature = null;
            }
            if (visible)
                Refresh();
        }

        private void Refresh()
        {
            labels.Refresh();
            if (folderFilter != null && !(plugin.Catalog.folders ?? new List<string>()).Contains(folderFilter))
                folderFilter = null;
            if (data.Refresh(plugin, search.text, folderFilter))
                dataVersion++;
            var tracks = data.Tracks;
            int pages = Math.Max(1, (tracks.Length + PageSize - 1) / PageSize);
            pageIndex = Mathf.Clamp(pageIndex, 0, pages - 1);
            var deck = plugin.Current;
            bool control = deck != null && plugin.CanControl(deck);
            string next = string.Join(
                "|",
                dataVersion,
                pageIndex,
                control,
                selection.Count,
                selection.FirstOrDefault(),
                selection.LastOrDefault()
            );
            if (next != signature)
            {
                signature = next;
                Bind(tracks, pageIndex, control);
            }
            BoomboxNativeUI.Label(folder, BoomboxText.Get("text.folder") + (folderFilter ?? BoomboxText.Get("text.all_folders")));
            BoomboxNativeControls.Show(selected, selection.Count > 0);
            BoomboxNativeControls.Show(addSelected, selection.Count > 0);
            selected.text = BoomboxText.Get("text.selected") + selection.Count;
            BoomboxNativeControls.Show(empty, tracks.Length == 0);
            page.text = (pageIndex + 1) + " / " + pages + " · " + tracks.Length;
            previousPage.interactable = pageIndex > 0;
            nextPage.interactable = pageIndex + 1 < pages;
            ContinueBinding();
            notice.text = building ? BoomboxText.Get("text.preparing_list") : (plugin.Notice);
        }

        private void Bind(MusicTrack[] tracks, int index, bool control)
        {
            binding.Clear();
            boundRows = 0;
            building = true;
            foreach (var old in rows)
                old.Hide();
            int first = index * PageSize, last = Math.Min(tracks.Length, first + PageSize);
            for (int i = first; i < last; i++)
            {
                var track = tracks[i];
                if (!string.IsNullOrEmpty(track.album) && (i == first || tracks[i - 1].AlbumKey != track.AlbumKey)
                && data.Albums.TryGetValue(track.AlbumKey, out var albumTracks))
                {
                    var ids = albumTracks.Select(t => t.id).ToList();
                    binding.Add(header => header.Album(
                            (track.albumArtist ?? track.artist) + " · " + track.album,
                            control,
                            () => AddToSelection(ids),
                            () => plugin.PlayQueue(plugin.Current, ids, null, 0),
                            () => OpenPlaylists(header.AddButton, ids, false)
                        ));
                }
                int position = i;
                string id = track.id;
                string label = (track.trackNumber > 0
                    ? track.trackNumber.ToString("00") + ". "
                    : "") + track.SelectionLabel;
                binding.Add(row => row.Track(
                        label,
                        selection.Contains(id),
                        control && track.available,
                        () => Toggle(id),
                        () => plugin.PlayQueue(plugin.Current, tracks.Select(t => t.id), null, position),
                        () => OpenPlaylists(row.AddButton, new List<string> { id }, false)
                    ));
            }
        }

        private void ContinueBinding()
        {
            if (!building)
                return;
            buildClock.Restart();
            do
            {
                if (boundRows >= binding.Count)
                {
                    building = false;
                    binding.Clear();
                    break;
                }
                var row = Row(boundRows);
                binding[boundRows++](row);
            } while (buildClock.Elapsed.TotalMilliseconds < 2d);
            buildClock.Stop();
        }

        private BoomboxLibraryRow Row(int index)
        {
            while (rows.Count <= index)
                rows.Add(new BoomboxLibraryRow(content, rows.Count, labels));
            return rows[index];
        }

        private void Toggle(string id)
        {
            if (!selection.Remove(id))
                selection.Add(id);
        }

        private void AddToSelection(IEnumerable<string> ids)
        {
            var known = new HashSet<string>(selection);
            foreach (string id in ids)
                if (known.Add(id))
                    selection.Add(id);
        }

        private void SelectResults() => AddToSelection(data.Tracks.Select(t => t.id));

        private void OpenFolders()
        {
            var entries = new List<KeyValuePair<string, Action>> {
                new KeyValuePair<string, Action>(
                BoomboxText.Get("text.all_folders"),
                () =>
                        {
                            folderFilter = null;
                            pageIndex = 0;
                        }
                )
            };
            foreach (string path in plugin.Catalog.folders ?? new List<string>())
            {
                string chosen = path;
                entries.Add(new KeyValuePair<string, Action>(
                path,
                () =>
                    {
                        folderFilter = chosen;
                        pageIndex = 0;
                    }
                ));
            }
            popup().Open((RectTransform)folder.transform, entries, null);
        }

        private void OpenPlaylists(Component anchor, List<string> ids, bool fromSelection)
        {
            var entries = plugin.MusicLibrary.playlists.Select(
                p => new KeyValuePair<string, Action>(
                    p.name + " · " + p.tracks.Count,
                    () => Added(p, ids, fromSelection)
                )
            ).ToList();
            popup().Open(
                (RectTransform)anchor.transform,
                entries,
                name => Added(plugin.MusicLibrary.Create(name, new string[0]), ids, fromSelection)
            );
        }

        private void Added(UserPlaylist playlist, List<string> ids, bool fromSelection)
        {
            BoomboxPlaylistQueue.Append(plugin, playlist, ids);
            if (fromSelection)
                selection.Clear();
            plugin.Notify("notice.playlist_tracks_added", playlist.name, ids.Count);
        }
    }
}
