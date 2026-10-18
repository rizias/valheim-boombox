using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxFolderBrowser
    {
        private readonly List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();
        private readonly List<Button> buttons = new List<Button>();
        private readonly RectTransform content;
        private readonly ScrollRect list;
        private readonly Button drives, up, addThis, music;
        private readonly TMP_Text location, message;
        private readonly Func<string, bool> add;
        private string current;
        private string error;
        private bool loaded;
        private bool bound;
        private string language;


        internal BoomboxFolderBrowser(RectTransform root, BoomboxMenuLabels labels, Func<string, bool> addFolder)
        {
            add = addFolder;
            var title = labels.Add(
                BoomboxNativeControls.Line("BrowserLabel", root, 15),
                () => BoomboxText.Get("text.choose_a_folder_on_this_computer")
            );
            BoomboxNativeUI.Place(title.rectTransform, 24, 184, 500, 24);
            drives = labels.Add(
                BoomboxNativeControls.TextButton("BrowserDrives", root, "", () => Browse(null)),
                () => BoomboxText.Get("text.drives")
            );
            BoomboxNativeUI.Place((RectTransform)drives.transform, 24, 212, 110, 32);
            up = labels.Add(
                BoomboxNativeControls.TextButton("BrowserUp", root, "", () => Browse(Path.GetDirectoryName(current))),
                () => BoomboxText.Get("text.up")
            );
            BoomboxNativeUI.Place((RectTransform)up.transform, 138, 212, 110, 32);
            location = BoomboxNativeControls.Line("BrowserPath", root, 14);
            BoomboxNativeUI.Place(location.rectTransform, 256, 212, 268, 32);
            list = BoomboxNativeControls.List("BrowserList", root, out content);
            BoomboxNativeUI.Place((RectTransform)list.transform, 24, 252, 500, 300);
            message = BoomboxNativeControls.Line("BrowserEmpty", root, 15);
            BoomboxNativeUI.Place(message.rectTransform, 24, 252, 480, 32);
            addThis = labels.Add(
                BoomboxNativeControls.TextButton("BrowserAdd", root, "", () => add(current)),
                () => BoomboxText.Get("text.add_this_folder")
            );
            BoomboxNativeUI.Place((RectTransform)addThis.transform, 24, 560, 248, 32);
            music = labels.Add(
                BoomboxNativeControls.TextButton(
                    "BrowserMusic",
                    root,
                    "",
                    () => add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic))
                ),
                () => BoomboxText.Get("text.music_folder")
            );
            BoomboxNativeUI.Place((RectTransform)music.transform, 276, 560, 248, 32);
        }

        internal void Refresh(bool idle)
        {
            string selected = Localization.instance.GetSelectedLanguage();
            if (!loaded || selected != language)
            {
                language = selected;
                Browse(current);
            }
            if (!bound)
                Bind();
            drives.interactable = up.interactable = current != null;
            location.text = current ?? BoomboxText.Get("text.select_a_drive");
            message.text = error ?? (entries.Count == 0 ? BoomboxText.Get("text.no_subfolders") : "");
            addThis.interactable = idle && current != null;
            music.interactable = idle && Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        }

        private void Browse(string target)
        {
            loaded = true;
            bound = false;
            current = target;
            entries.Clear();
            error = null;
            try
            {
                if (target == null)
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                        entries.Add(
                            new KeyValuePair<string, string>(
                                drive.RootDirectory.FullName,
                                drive.Name
                                    + (string.IsNullOrEmpty(drive.VolumeLabel) || drive.VolumeLabel == drive.Name
                                        ? ""
                                        : "   " + drive.VolumeLabel)
                                    + "   "
                                    + (drive.TotalFreeSpace / (1024f * 1024f * 1024f)).ToString("0")
                                    + BoomboxText.Get("text.gb_free")
                            )
                        );
                else
                    foreach (var directory in new DirectoryInfo(target).GetDirectories().Where(
                        d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
                    ).OrderBy(
                        d => d.Name,
                        StringComparer.CurrentCultureIgnoreCase
                    ))
                        entries.Add(new KeyValuePair<string, string>(directory.FullName, directory.Name));
            }
            catch (Exception failure) when (failure is IOException
                || failure is UnauthorizedAccessException
                || failure is System.Security.SecurityException)
            {
                error = failure.Message;
            }
        }

        private void Bind()
        {
            bound = true;
            for (int i = 0; i < entries.Count; i++)
            {
                while (buttons.Count <= i)
                    buttons.Add(Entry(buttons.Count));
                string target = entries[i].Key;
                BoomboxNativeControls.Show(buttons[i], true);
                BoomboxNativeUI.Label(buttons[i], entries[i].Value);
                buttons[i].onClick = new Button.ButtonClickedEvent();
                buttons[i].onClick.AddListener(() => Browse(target));
            }
            for (int i = entries.Count; i < buttons.Count; i++)
                BoomboxNativeControls.Show(buttons[i], false);
            list.verticalNormalizedPosition = 1f;
        }

        private Button Entry(int index)
        {
            var button = BoomboxNativeControls.TextButton("BrowseEntry" + index, content, "", () => { });
            var text = button.GetComponentInChildren<TMP_Text>();
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 16;
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            return button;
        }
    }
}
