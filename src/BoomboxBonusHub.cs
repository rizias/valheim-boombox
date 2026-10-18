using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxBonusHub : IDisposable
    {
        private readonly BoomboxPlugin plugin;
        private readonly RectTransform root;
        private readonly TMP_Text heading;
        private readonly Button[] pages = new Button[3];
        private int page;
        private BoomboxBonusPanel active;
        private BoomboxGameplayPanel gameplay;
        private BoomboxPartyPanel party;
        internal BoomboxBonusHub(BoomboxPlugin owner, RectTransform parent)
        {
            plugin = owner;
            root = BoomboxNativeUI.Rect("BoomboxBonusHub", parent);
            BoomboxNativeUI.Place(root, 0, 0, 1060, 760);
            heading = BoomboxNativeUI.FixedText("BonusesTitle", root, 24, true);
            BoomboxNativeUI.Place(heading.rectTransform, 24, 118, 1000, 32);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                pages[i] = BoomboxNativeUI.CleanButton("BonusPage" + i, root, "", () => page = index);
                BoomboxNativeUI.Place((RectTransform)pages[i].transform, 24 + i * 340, 160, 326, 36);
            }
        }
        internal void Select(int index) => page = index;
        internal void Show()
        {
            root.gameObject.SetActive(true);
            heading.text = BoomboxText.Get("text.bonuses");
            string[] labels = {
                BoomboxText.Get("text.bonuses_and_hud"),
                BoomboxText.Get("text.device_gameplay"),
                BoomboxText.Get("text.party_world_rules")
            };
            for (int i = 0; i < 3; i++)
            {
                BoomboxNativeUI.Label(pages[i], labels[i]);
                BoomboxNativeUI.Select(pages[i], i == page);
            }
            if (page == 0)
                (active ?? (active = new BoomboxBonusPanel(plugin, root, true))).Show();
            else
                active?.Hide();
            if (page == 1)
                (gameplay ?? (gameplay = new BoomboxGameplayPanel(plugin, root))).Show();
            else
                gameplay?.Hide();
            if (page == 2)
                (party ?? (party = new BoomboxPartyPanel(plugin, root, true))).Show();
            else
                party?.Hide();
        }
        internal void Hide()
        {
            root.gameObject.SetActive(false);
            active?.Hide();
            gameplay?.Hide();
            party?.Hide();
        }
        public void Dispose()
        {
            active?.Dispose();
            gameplay?.Dispose();
            party?.Dispose();
            if (root != null)
                UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
