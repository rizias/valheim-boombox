using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxCassettePanel : IDisposable
    {
        private readonly BoomboxPlugin plugin;
        private readonly RectTransform window;
        private readonly RectTransform body;
        private readonly RectTransform details;
        private readonly Image cassetteIcon;
        private readonly TMP_Text cassetteName;
        private readonly TMP_Text subtitle;
        private readonly TMP_Text title;
        private readonly TMP_Text preview;
        private readonly TMP_Text nextPreview;
        private readonly TMP_Text costLabel;
        private readonly TMP_Text station;
        private readonly TMP_Text notice;
        private readonly Button upgrade;
        private readonly Button eject;
        private readonly Button swap;
        private readonly Button chooseDevice;
        private readonly Button back;
        private readonly Dictionary<string, Button> properties = new Dictionary<string, Button>();
        private readonly GameObject[] costs;
        private readonly ScrollRect inventory;
        private readonly RectTransform inventoryContent;
        private string selected = "Purple";
        private enum ViewMode
        {
            Properties, Cassettes, Devices
        }
        private ViewMode mode = ViewMode.Properties;
        private string inventorySignature;
        private string language;
        private int frame = -1;
        internal bool Alive => window != null;


        internal BoomboxCassettePanel(BoomboxPlugin owner, RectTransform parent)
        {
            plugin = owner;
            window = BoomboxNativeUI.Rect("BoomboxCassetteWindow", parent);
            BoomboxNativeUI.Place(window, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            cassetteIcon = BoomboxNativeUI.Image("CassetteIcon", window, null, Color.white);
            cassetteIcon.type = Image.Type.Simple;
            cassetteIcon.preserveAspect = true;
            cassetteName = BoomboxNativeUI.Text("CassetteName", window, 22, true);
            subtitle = BoomboxNativeUI.FixedText("CassetteState", window, 15);
            eject = BoomboxNativeUI.CleanButton(
            "CassetteEject",
            window,
            BoomboxText.Get("text.eject"),
            () =>
                {
                    plugin.Items.Extract(plugin.Current);
                    inventorySignature = null;
                }
            );
            swap = BoomboxNativeUI.CleanButton(
            "CassetteReplace",
            window,
            BoomboxText.Get("text.choose_cassette"),
            () =>
                {
                    mode = ViewMode.Cassettes;
                    inventorySignature = null;
                }
            );
            chooseDevice = BoomboxNativeUI.CleanButton(
            "ChooseDevice",
            window,
            BoomboxText.Get("text.boomboxes"),
            () =>
                {
                    mode = ViewMode.Devices;
                    inventorySignature = null;
                }
            );
            body = BoomboxNativeUI.Rect("CassetteBody", window);
            foreach (string property in new[] { "Purple", "Green", "Yellow", "Red", "Blue" })
            {
                string key = property;
                var button = BoomboxNativeUI.CleanButton(
                "Property" + key,
                body,
                key,
                () =>
                    {
                        selected = key;
                        plugin.Notice = "";
                    }
                );
                button.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Left;
                button.GetComponentInChildren<TMP_Text>().margin = new Vector4(14, 2, 12, 2);
                properties[key] = button;
            }
            details = BoomboxNativeUI.Rect("PropertyDetails", body);
            var detailBackground = BoomboxNativeUI.Image("Background", details, null, new Color(.065f, .085f, .07f, 1));
            detailBackground.rectTransform.anchorMin = Vector2.zero;
            detailBackground.rectTransform.anchorMax = Vector2.one;
            detailBackground.rectTransform.offsetMin = detailBackground.rectTransform.offsetMax = Vector2.zero;
            title = BoomboxNativeUI.Text("SelectedProperty", details, 25, true);
            preview = BoomboxNativeUI.Text("PropertyPreview", details, 21);
            nextPreview = BoomboxNativeUI.Text("NextPropertyPreview", details, 21);
            costLabel = BoomboxNativeUI.Text("UpgradeCost", details, 18, true);
            costLabel.text = BoomboxText.Get("text.upgrade_cost");
            costs = new GameObject[4];
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = UnityEngine.Object.Instantiate(
                    InventoryGui.instance.m_recipeRequirementList[i
                        % InventoryGui.instance.m_recipeRequirementList.Length],
                    details,
                    false
                );
                costs[i].name = "BoomboxRequirement" + i;
                costs[i].SetActive(true);
            }
            station = BoomboxNativeUI.Text("StationRequirement", details, 16);
            station.alignment = TextAlignmentOptions.Center;
            upgrade = BoomboxNativeUI.CleanButton(
                "CassetteUpgrade",
                details,
                BoomboxText.Get("text.upgrade"),
                () => BoomboxWorkstation.Upgrade(plugin.Current, selected)
            );
            notice = BoomboxNativeUI.Text("Notice", window, 14);
            inventory = BoomboxNativeUI.Scroll("CassetteInventory", body, out inventoryContent);
            back = BoomboxNativeUI.CleanButton(
                "BackToProperties",
                window,
                BoomboxText.Get("text.back"),
                () => mode = ViewMode.Properties
            );
        }

        private bool? emptyLayout;

        private void Layout()
        {
            var rect = new Rect(0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            float width = rect.width - 48;
            float height = rect.height - 250;
            float left = Mathf.Min(280, width * .3f);
            float right = width - left - 20;
            BoomboxNativeUI.Place(cassetteIcon.rectTransform, 28, 120, 84, 60);
            BoomboxNativeUI.Place(cassetteName.rectTransform, 126, 119, width - 360, 33);
            BoomboxNativeUI.Place(subtitle.rectTransform, 126, 154, width - 360, 35);
            BoomboxNativeUI.Place((RectTransform)eject.transform, rect.width - 150, 124, 126, 38);
            BoomboxNativeUI.Place((RectTransform)swap.transform, rect.width - 347, 124, 190, 38);
            BoomboxNativeUI.Place((RectTransform)chooseDevice.transform, rect.width - 183, 168, 159, 30);
            BoomboxNativeUI.Place((RectTransform)back.transform, 24, rect.height - 49, 100, 30);
            BoomboxNativeUI.Place(body, 24, 205, width, height);
            BoomboxNativeUI.Place(details, left + 20, 0, right, height);
            int row = 0;
            foreach (var button in properties.Values)
                BoomboxNativeUI.Place((RectTransform)button.transform, 0, row++ * 72, left, 64);
            BoomboxNativeUI.Place(title.rectTransform, 20, 18, right - 40, 64);
            BoomboxNativeUI.Place(preview.rectTransform, 20, 87, (right - 55) / 2, 110);
            BoomboxNativeUI.Place(nextPreview.rectTransform, 35 + (right - 55) / 2, 87, (right - 55) / 2, 110);
            BoomboxNativeUI.Place(costLabel.rectTransform, 20, 206, right - 40, 28);
            for (int i = 0; i < costs.Length; i++)
                BoomboxNativeUI.Place(
                    (RectTransform)costs[i].transform,
                    20 + i * (right - 40) / 4,
                    247,
                    (right - 40) / 4 - 5,
                    95
                );
            BoomboxNativeUI.Place(station.rectTransform, 20, height - 97, right - 40, 30);
            BoomboxNativeUI.Place((RectTransform)upgrade.transform, 20, height - 60, right - 40, 44);
            BoomboxNativeUI.Place(notice.rectTransform, 135, rect.height - 47, rect.width - 159, 34);
            BoomboxNativeUI.Place((RectTransform)inventory.transform, 0, 0, width, height);
        }

        internal void Show()
        {
            if (!window.gameObject.activeSelf)
                window.gameObject.SetActive(true);
            bool empty = plugin.Current?.State.insertedCassette == null;
            if (!emptyLayout.HasValue || emptyLayout.Value != empty)
            {
                Layout();
                emptyLayout = empty;
            }
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                Refresh();
            }
            AlignEmptyState(empty);
            if (empty)
                PlaceEmptyBlock(BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
        }

        private void AlignEmptyState(bool empty)
        {
            var alignment = empty ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
            cassetteName.alignment = alignment;
            subtitle.alignment = alignment;
            var line = inventoryContent.Cast<Transform>().Where(child => child.gameObject.activeSelf
                && child.name == "NoCassette").Select(
                child => child.GetComponent<TMP_Text>()
            ).FirstOrDefault();
            if (line == null)
                return;
            line.alignment = alignment;
            line.margin = empty ? new Vector4(0, 0, -20, 0) : Vector4.zero;
            var element = line.GetComponent<LayoutElement>();
            float height = empty ? line.GetPreferredValues(line.text, 600, 0).y : 80;
            if (Mathf.Approximately(element.preferredHeight, height))
                return;
            element.preferredHeight = height;
            LayoutRebuilder.ForceRebuildLayoutImmediate(inventoryContent);
        }

        private void PlaceEmptyBlock(float windowWidth, float windowHeight)
        {
            float groupHeight = Mathf.Min(510, 101 + LayoutUtility.GetPreferredHeight(inventoryContent));
            float top = windowHeight * .5f - groupHeight * .5f;
            BoomboxNativeUI.Place(cassetteName.rectTransform, 24, top, windowWidth - 48, 33);
            BoomboxNativeUI.Place(subtitle.rectTransform, 24, top + 35, windowWidth - 48, 35);
            BoomboxNativeUI.Place(body, (windowWidth - 600) * .5f, top + 101, 600, groupHeight - 101);
            BoomboxNativeUI.Place((RectTransform)inventory.transform, 0, 0, 600, groupHeight - 101);
        }

        private void Refresh()
        {
            string currentLanguage = Localization.instance.GetSelectedLanguage();
            if (currentLanguage != language)
            {
                language = currentLanguage;
                BoomboxNativeUI.Label(eject, BoomboxText.Get("text.eject"));
                BoomboxNativeUI.Label(swap, BoomboxText.Get("text.choose_cassette"));
                BoomboxNativeUI.Label(chooseDevice, BoomboxText.Get("text.boomboxes"));
                BoomboxNativeUI.Label(back, BoomboxText.Get("text.back"));
                costLabel.text = BoomboxText.Get("text.upgrade_cost");
            }
            var player = Player.m_localPlayer;
            var deck = plugin.Current;
            var cassette = deck?.State.insertedCassette;
            bool owned = plugin.CanControl(deck);
            bool listing = mode != ViewMode.Properties || cassette == null;
            cassetteName.text = cassette == null
                ? BoomboxText.Get("text.no_cassette_installed")
                : BoomboxCassetteWorkshop.Name(cassette);
            subtitle.text = deck == null
                ? BoomboxText.Get("text.take_a_boombox_into_your_inventory")
                : BoomboxText.Get("text.boombox_level") + deck.State.upgradeTier;
            cassetteIcon.sprite = cassette == null
                ? InventoryGui.instance.m_recipeIcon.sprite
                : BoomboxItemPrefabs.CassetteIcon();
            cassetteIcon.enabled = cassette != null;
            eject.gameObject.SetActive(cassette != null);
            eject.interactable = owned;
            swap.gameObject.SetActive(cassette != null && !listing);
            swap.interactable = owned;
            chooseDevice.gameObject.SetActive(
                player != null && player.GetInventory().GetAllItems().Count(BoomboxItemData.IsDevice) > 1
            );
            back.gameObject.SetActive(listing && cassette != null);
            notice.text = (plugin.Notice);
            details.gameObject.SetActive(!listing);
            foreach (var button in properties.Values)
                button.gameObject.SetActive(!listing);
            inventory.gameObject.SetActive(listing);
            if (listing)
            {
                RefreshInventory();
                LayoutRebuilder.ForceRebuildLayoutImmediate(inventoryContent);
                return;
            }
            foreach (var pair in properties)
            {
                int value = cassette.Rank(pair.Key);
                string name = pair.Key == "Purple"
                    ? BoomboxText.Get("config.Speed")
                    : pair.Key == "Red"
                        ? BoomboxText.Get("boombox_property_red")
                        : Localization.instance.Localize("$boombox_property_" + pair.Key.ToLowerInvariant());
                BoomboxNativeUI.Label(
                    pair.Value,
                    name + "  " + value + (value >= BoomboxCassetteWorkshop.MaximumRank
                        ? " · " + BoomboxText.Get("text.max")
                        : "") + "\n<size=14>" + BoomboxCassetteWorkshop.Effect(pair.Key, value) + "</size>",
                    pair.Key == selected ? BoomboxNativeUI.Gold : BoomboxNativeUI.TextColor
                );
                BoomboxNativeUI.Select(pair.Value, pair.Key == selected);
            }
            int rank = cassette.Rank(selected);
            bool maximum = rank >= BoomboxCassetteWorkshop.MaximumRank;
            title.text = Localization.instance.Localize("$boombox_property_" + selected.ToLowerInvariant());
            preview.text = BoomboxText.Get("text.current") + " · " + rank + "\n\n"
                + BoomboxCassetteWorkshop.Effect(selected, rank);
            nextPreview.text = maximum
                ? ""
                : BoomboxText.Get("text.after_upgrade") + " · " + (rank + 1) + "\n\n<color=#ffc66c>"
                    + BoomboxCassetteWorkshop.Effect(
                selected,
                rank + 1
            )
                    + "</color>";
            costLabel.gameObject.SetActive(!maximum);
            foreach (var cost in costs)
                cost.SetActive(!maximum);
            var required = maximum ? null : BoomboxWorkstation.CostFor(deck, selected);
            if (!maximum)
                BoomboxCraftingRequirements.Draw(costs, required, "BoomboxCassetteRequirements");
            var nearby = maximum ? null : BoomboxAmplifier.FindStation(player, required);
            bool atStation = nearby != null && nearby.GetLevel(true) >= required.StationLevel;
            station.text = maximum
                ? ""
                : (atStation ? "" : BoomboxText.Get("text.requires")) + BoomboxAmplifier.StationName(required.StationPrefab)
                    + " "
                    + required.StationLevel;
            station.color = atStation ? BoomboxNativeUI.TextColor : new Color(1f, .5f, .35f);
            BoomboxNativeUI.Label(
                upgrade,
                maximum
                    ? BoomboxText.Get("text.maximum_rank")
                    : BoomboxText.Get("text.upgrade_to") + (rank + 1)
            );
            upgrade.interactable = !maximum && owned && atStation
            && BoomboxCraftingRequirements.CanAfford(player, required, "BoomboxCassetteRequirements");
        }

        private void RefreshInventory()
        {
            var player = Player.m_localPlayer;
            if (player == null)
                return;
            var items = player.GetInventory().GetAllItems().Where(mode == ViewMode.Devices
                ? new Func<ItemDrop.ItemData, bool>(BoomboxItemData.IsDevice)
                : BoomboxItemData.IsCassette).ToArray();
            string next = mode + ":" + Localization.instance.GetSelectedLanguage() + ":" + string.Join(
                "|",
                items.Select(i => i.m_customData.TryGetValue(BoomboxItemData.Key, out string data)
                    ? data
                    : i.m_dropPrefab.name)
            );
            if (next == inventorySignature)
                return;
            inventorySignature = next;
            foreach (Transform child in inventoryContent)
            {
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
            if (items.Length == 0)
            {
                var empty = BoomboxNativeUI.Text("NoCassette", inventoryContent, 19);
                empty.text = mode == ViewMode.Devices
                    ? BoomboxText.Get("text.no_boomboxes_in_inventory")
                    : BoomboxText.Get("text.no_cassettes_in_inventory");
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
            }
            foreach (var item in items)
            {
                var data = BoomboxItemData.Read(item);
                bool device = BoomboxItemData.IsDevice(item);
                string text = device
                    ? BoomboxText.Get("text.boombox_2") + data.deck.upgradeTier
                    : BoomboxCassetteWorkshop.Name(data.cassette) + "\n<size=14>"
                        + BoomboxCassetteWorkshop.Describe(data.cassette)
                        + "</size>";
                var button = BoomboxNativeUI.CleanButton(
                device
                        ? "SelectDevice"
                        : "InstallCassette",
                inventoryContent,
                text,
                () =>
                {
                    if (device)
                        plugin.Open(plugin.Items.ForItem(item));
                    else
                        plugin.Items.Insert(plugin.Current, item);
                    mode = ViewMode.Properties;
                    inventorySignature = null;
                }
                );
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 76;
                button.interactable = device || plugin.CanControl(plugin.Current);
            }
        }

        internal void Hide()
        {
            if (window != null && window.gameObject.activeSelf)
                window.gameObject.SetActive(false);
        }
        public void Dispose()
        {
            if (window != null)
                UnityEngine.Object.Destroy(window.gameObject);
        }
    }
}
