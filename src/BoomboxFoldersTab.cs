using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxFoldersTab
    {
        private readonly BoomboxPlugin plugin;
        private readonly BoomboxFolderBrowser browser;
        private readonly BoomboxMenuLabels labels = new BoomboxMenuLabels();
        private readonly List<KeyValuePair<RectTransform, Button>> folderRows =
            new List<KeyValuePair<RectTransform, Button>>();
        private readonly RectTransform root, folderContent;
        private readonly TMP_InputField path;
        private readonly Button add, scan, cancel, computerAudio;
        private readonly TMP_Text errors, notice;
        private string foldersSignature;


        internal BoomboxFoldersTab(BoomboxPlugin owner, RectTransform window)
        {
            plugin = owner;
            root = BoomboxNativeUI.Rect("BoomboxFoldersWindow", window);
            BoomboxNativeUI.Place(root, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            var title = labels.Add(
                BoomboxNativeControls.Line("FoldersTitle", root, 22, true),
                () => BoomboxText.Get("text.music_folders_on_this_computer")
            );
            BoomboxNativeUI.Place(title.rectTransform, 24, 116, 1012, 32);
            var hint = labels.Add(
                BoomboxNativeControls.Line("FoldersHint", root, 15),
                () => BoomboxText.Get("text.add_a_folder_and_the_music_scan")
            );
            BoomboxNativeUI.Place(hint.rectTransform, 24, 150, 1012, 24);
            browser = new BoomboxFolderBrowser(root, labels, folder => AddFolder(folder));
            var manual = labels.Add(
                BoomboxNativeControls.Line("ManualLabel", root, 15),
                () => BoomboxText.Get("text.or_type_a_path")
            );
            BoomboxNativeUI.Place(manual.rectTransform, 24, 604, 500, 24);
            path = labels.Add(
                BoomboxNativeControls.Input("FolderPath", root, 260),
                () => BoomboxText.Get("text.folder_path_for_example_d_music")
            );
            BoomboxNativeUI.Place((RectTransform)path.transform, 24, 632, 330, 32);
            add = labels.Add(
            BoomboxNativeControls.TextButton(
            "FolderAdd",
            root,
            "",
            () =>
                    {
                        if (AddFolder(path.text))
                            path.text = "";
                    }
            ),
            () => BoomboxText.Get("text.add_folder")
            );
            BoomboxNativeUI.Place((RectTransform)add.transform, 358, 632, 166, 32);
            var listLabel = labels.Add(
                BoomboxNativeControls.Line("FoldersListLabel", root, 15),
                () => BoomboxText.Get("text.selected_folders")
            );
            BoomboxNativeUI.Place(listLabel.rectTransform, 544, 184, 492, 24);
            var folderList = BoomboxNativeControls.List("FolderList", root, out folderContent);
            BoomboxNativeUI.Place((RectTransform)folderList.transform, 544, 212, 492, 256);
            scan = labels.Add(
                BoomboxNativeControls.TextButton("FolderScan", root, "", plugin.RefreshCatalog),
                () => BoomboxText.Get("text.scan_selected_folders")
            );
            BoomboxNativeUI.Place((RectTransform)scan.transform, 544, 476, 492, 32);
            cancel = labels.Add(
                BoomboxNativeControls.TextButton("FolderCancel", root, "", plugin.CancelCatalogRefresh),
                () => BoomboxText.Get("text.cancel_scan")
            );
            BoomboxNativeUI.Place((RectTransform)cancel.transform, 544, 512, 492, 32);
            errors = BoomboxNativeUI.FixedText("FolderErrors", root, 13);
            errors.overflowMode = TextOverflowModes.Ellipsis;
            BoomboxNativeUI.Place(errors.rectTransform, 544, 548, 492, 52);
            var nextStep = labels.Add(
                BoomboxNativeUI.FixedText("FoldersNextStep", root, 16),
                () => BoomboxText.Get("text.after_scanning_open_library_press_beside_a")
            );
            BoomboxNativeUI.Place(nextStep.rectTransform, 544, 604, 492, 52);
            computerAudio = BoomboxNativeControls.TextButton(
                "ComputerAudioToggle",
                root,
                "",
                () => BoomboxDesktopAudio.Allowed = !BoomboxDesktopAudio.Allowed
            );
            BoomboxNativeUI.Place((RectTransform)computerAudio.transform, 544, 664, 492, 32);
            notice = BoomboxNativeControls.Line("FoldersNotice", root, 14);
            BoomboxNativeUI.Place(notice.rectTransform, 24, 712, 1012, 28);
        }

        internal void Show(bool visible)
        {
            BoomboxNativeControls.Show(root, visible);
            if (visible)
                Refresh();
        }

        private void Refresh()
        {
            labels.Refresh();
            BoomboxNativeUI.Label(
                computerAudio,
                BoomboxText.Get("text.computer_audio_reaction") + (BoomboxDesktopAudio.Allowed
                    ? BoomboxText.Get("text.allowed")
                    : BoomboxText.Get("text.off_2"))
            );
            bool idle = !plugin.CatalogRefreshing;
            browser.Refresh(idle);
            add.interactable = idle && !string.IsNullOrWhiteSpace(path.text);
            scan.interactable = idle && plugin.MusicLibrary.folders.Count > 0;
            BoomboxNativeControls.Show(cancel, !idle);
            string next = string.Join("|", plugin.MusicLibrary.folders) + "#" + idle + "#"
                + Localization.instance.GetSelectedLanguage();
            if (next != foldersSignature)
            {
                foldersSignature = next;
                BindFolders(idle);
            }
            errors.text = string.Join(
                "\n",
                (plugin.Catalog.errors ?? new List<MusicCatalogError>()).Take(3).Select(
                    e => e.path + ": " + (e.error)
                )
            );
            notice.text = (plugin.Notice);
        }

        private void BindFolders(bool idle)
        {
            var folders = plugin.MusicLibrary.folders.ToArray();
            for (int i = 0; i < folders.Length; i++)
            {
                while (folderRows.Count <= i)
                    folderRows.Add(FolderRow(folderRows.Count));
                string folder = folders[i];
                var row = folderRows[i];
                BoomboxNativeControls.Show(row.Key, true);
                row.Key.GetComponentInChildren<TMP_Text>().text = folder;
                row.Value.onClick = new Button.ButtonClickedEvent();
                row.Value.onClick.AddListener(() => plugin.MusicLibrary.RemoveFolder(folder));
                row.Value.interactable = idle;
            }
            for (int i = folders.Length; i < folderRows.Count; i++)
                BoomboxNativeControls.Show(folderRows[i].Key, false);
        }

        private KeyValuePair<RectTransform, Button> FolderRow(int index)
        {
            var row = BoomboxNativeControls.Row("FolderRow" + index, folderContent, 32);
            var text = BoomboxNativeControls.Line("FolderPathText", row, 14);
            BoomboxNativeControls.Stretch(text.rectTransform, 4, 110, 0, 32);
            var remove = labels.Add(
            BoomboxNativeControls.TextButton("FolderRemove", row, "", () => { }),
            () => BoomboxText.Get("text.remove")
            );
            BoomboxNativeControls.Right((RectTransform)remove.transform, 0, 0, 100, 32);
            return new KeyValuePair<RectTransform, Button>(row, remove);
        }

        internal bool AddFolder(string folder)
        {
            try
            {
                bool added = plugin.MusicLibrary.AddFolder(folder);
                plugin.Notice = (added ? BoomboxText.Get("text.folder_added") : BoomboxText.Get("text.folder_already_listed")) + folder;
                if (added)
                    plugin.RefreshCatalog();
                return added;
            }
            catch (Exception error) when (error is IOException
                || error is ArgumentException
                || error is UnauthorizedAccessException
                || error is NotSupportedException
                || error is System.Security.SecurityException)
            {
                plugin.Notice = error.Message;
                return false;
            }
        }

    }
}
