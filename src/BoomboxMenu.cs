using System.Collections.Generic;

namespace VikingBoombox
{
    internal sealed class BoomboxMenu
    {
        private readonly BoomboxPlugin plugin;
        private readonly List<string> selection = new List<string>();
        private BoomboxMenuFrame frame;
        private BoomboxMenuPopup popup;
        private BoomboxLibraryTab library;
        private BoomboxPlaylistsTab playlists;
        private BoomboxFoldersTab folders;
        private BoomboxCassettePanel cassettePanel;
        private BoomboxTrophyPanel trophyPanel;
        private BoomboxEffectsPanel effectsPanel;
        private BoomboxBonusHub bonusPanel;
        private int tab;

        public BoomboxMenu(BoomboxPlugin instance)
        {
            plugin = instance;
        }

        internal void Update()
        {
            plugin.InventorySlot.Draw();
            if (!plugin.MenuOpen)
            {
                HideNative();
                return;
            }
            if (InventoryGui.instance == null)
                return;
            if (frame == null || !frame.Alive)
                Build();
            frame.Show(tab);
            if (tab == 0)
                Library().Show(true);
            else
                library?.Show(false);
            if (tab == 1)
                Playlists().Show(true);
            else
                playlists?.Show(false);
            if (tab == 2)
                Folders().Show(true);
            else
                folders?.Show(false);
            if (tab == 3)
                Cassette().Show();
            else
                cassettePanel?.Hide();
            if (tab == 4)
                Decorations().Show();
            else
                trophyPanel?.Hide();
            if (tab == 5)
                Effects().Show();
            else
                effectsPanel?.Hide();
            if (tab == 7)
                (bonusPanel ?? (bonusPanel = new BoomboxBonusHub(plugin, frame.Window))).Show();
            else
                bonusPanel?.Hide();
        }

        private void Build()
        {
            Dispose();
            frame = new BoomboxMenuFrame(plugin, SelectNativeTab);
        }

        private BoomboxMenuPopup Popup() => popup ?? (popup = new BoomboxMenuPopup(frame.Window));
        private BoomboxLibraryTab Library() => library ?? (library = new BoomboxLibraryTab(plugin, frame.Window, Popup, selection));
        private BoomboxPlaylistsTab Playlists() => playlists ?? (playlists = new BoomboxPlaylistsTab(plugin, frame.Window, selection));
        private BoomboxFoldersTab Folders() => folders ?? (folders = new BoomboxFoldersTab(plugin, frame.Window));

        private BoomboxCassettePanel Cassette()
        {
            if (cassettePanel == null || !cassettePanel.Alive)
                cassettePanel = new BoomboxCassettePanel(plugin, frame.Window);
            return cassettePanel;
        }

        private BoomboxTrophyPanel Decorations()
        {
            if (trophyPanel == null || !trophyPanel.Alive)
                trophyPanel = new BoomboxTrophyPanel(plugin, frame.Window);
            return trophyPanel;
        }

        private BoomboxEffectsPanel Effects()
        {
            if (effectsPanel == null || !effectsPanel.Alive)
                effectsPanel = new BoomboxEffectsPanel(plugin, frame.Window);
            return effectsPanel;
        }

        internal void HideNative()
        {
            popup?.Close();
            library?.Show(false);
            trophyPanel?.Hide();
            effectsPanel?.Hide();
            bonusPanel?.Hide();
            frame?.Hide();
        }

        internal void SelectNativeTab(int value)
        {
            if (value != tab)
                plugin.Notice = "";
            tab = value;
            popup?.Close();
        }

        public void Dispose()
        {
            trophyPanel?.Dispose();
            effectsPanel?.Dispose();
            cassettePanel?.Dispose();
            bonusPanel?.Dispose();
            bonusPanel = null;
            frame?.Dispose();
            trophyPanel = null;
            effectsPanel = null;
            cassettePanel = null;
            frame = null;
            popup = null;
            library = null;
            playlists = null;
            folders = null;
        }

    }
}
