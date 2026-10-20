using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxTrophyCatalog
    {
        private readonly RectTransform window;
        private readonly GameObject elementPrefab;
        private readonly Button[] groups = new Button[3];
        private readonly List<InventoryElement> chips = new List<InventoryElement>();
        private readonly TMP_InputField search;
        private readonly ScrollRect gridScroll;
        private readonly RectTransform gridContent;
        private int group;
        private int lastSelectedSlot;
        private readonly List<KeyValuePair<string, InventoryElement>> cells =
            new List<KeyValuePair<string, InventoryElement>>();
        private string listSignature;
        private string selectionSignature;
        private string listKey;
        private string labelLanguage;
        internal Action<int> SlotSelected;
        internal Action<string> PrefabSelected;

        internal BoomboxDecorationKind Kind => group == 0
            ? BoomboxDecorationKind.Figurine
            : BoomboxDecorationKind.Hanging;
        internal IEnumerable<BoomboxDecorationSlot> GroupSlots => BoomboxDecorationCatalog.Slots
            .Where(s => InGroup(s, group))
            .OrderBy(s => s.Number);

        private static bool InGroup(BoomboxDecorationSlot slot, int index) => index == 0
            ? slot.Kind == BoomboxDecorationKind.Figurine
            : slot.Kind == BoomboxDecorationKind.Hanging && slot.Upper == (index == 1);

        private static string[] GroupNames()
        {
            string[] names = { BoomboxText.Get("text.stands"), BoomboxText.Get("text.upper_pendants"),
                BoomboxText.Get("text.lower_pendants") };
            return names.Select((name, index) => name + " ("
                + BoomboxDecorationCatalog.Slots.Count(slot => InGroup(slot, index)) + ")").ToArray();
        }
        internal static bool SearchFocused => BoomboxNativeControls.Focused;

        internal BoomboxTrophyCatalog(RectTransform parent, float x, float y)
        {
            window = parent;
            elementPrefab = InventoryGui.instance.m_playerGrid.m_elementPrefab;
            var names = GroupNames();
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                groups[i] = BoomboxNativeUI.CleanButton("Group" + i, window, names[i], () => SelectGroup(index));
                groups[i].GetComponentInChildren<TMP_Text>().fontSize = 16;
                BoomboxNativeUI.Place((RectTransform)groups[i].transform, x + i * 202, y, 196, 34);
            }
            for (int i = 0; i < 11; i++)
            {
                var chip = CreateElement("SlotChip" + i);
                BoomboxNativeUI.Place((RectTransform)chip.transform, x + i * 48, y + 42, 44, 44);
                chips.Add(chip);
            }
            search = CreateSearch(x, y + 94, 510, 32);
            gridScroll = BoomboxNativeUI.ScrollGrid("Catalog", window, out gridContent, 8);
            BoomboxNativeUI.Place((RectTransform)gridScroll.transform, x, y + 134, 510, 242);
        }

        private InventoryElement CreateElement(string name)
        {
            var clone = UnityEngine.Object.Instantiate(elementPrefab, window, false);
            clone.name = name;
            clone.SetActive(true);
            var element = clone.GetComponent<InventoryElement>();
            if (element.m_button == null)
                element.m_button = clone.GetComponent<Button>() ?? clone.AddComponent<Button>();
            element.m_button.onClick = new Button.ButtonClickedEvent();
            element.m_button.interactable = true;
            if (element.m_quality != null)
                element.m_quality.gameObject.SetActive(false);
            if (element.m_queued != null)
                element.m_queued.enabled = false;
            if (element.m_noteleport != null)
                element.m_noteleport.enabled = false;
            if (element.m_food != null)
                element.m_food.enabled = false;
            if (element.m_durability != null)
                element.m_durability.gameObject.SetActive(false);
            var binding = clone.transform.Find("binding");
            if (binding != null)
                binding.gameObject.SetActive(false);
            BoomboxNativeUI.RemoveForeign(clone);
            clone.AddComponent<BoomboxTooltipBelow>();
            foreach (var graphic in clone.GetComponentsInChildren<MaskableGraphic>(true))
            {
                graphic.maskable = true;
            }
            return element;
        }

        private TMP_InputField CreateSearch(float x, float y, float width, float height)
        {
            var input = BoomboxNativeControls.Input("Search", window, 40);
            BoomboxNativeControls.Placeholder(input, BoomboxText.Get("text.search_by_name"));
            input.onValueChanged.AddListener(value => listSignature = null);
            BoomboxNativeUI.Place((RectTransform)input.transform, x, y, width, height);
            return input;
        }

        private void SelectGroup(int index)
        {
            group = index;
            listSignature = null;
            if (!GroupSlots.Any(s => s.Index == lastSelectedSlot))
                SlotSelected?.Invoke(GroupSlots.First().Index);
        }

        internal void SelectGroupFor(int slot)
        {
            var data = BoomboxDecorationCatalog.Slots[slot];
            group = data.Kind == BoomboxDecorationKind.Figurine
                ? 0
                : data.Upper
                    ? 1
                    : 2;
            listSignature = null;
        }

        internal void Refresh(DeckState state, int selectedSlot, string selectedPrefab)
        {
            lastSelectedSlot = selectedSlot;
            string language = Localization.instance.GetSelectedLanguage();
            if (language != labelLanguage)
            {
                labelLanguage = language;
                var names = GroupNames();
                for (int i = 0; i < groups.Length; i++)
                    BoomboxNativeUI.Label(groups[i], names[i]);
                if (search.placeholder is TMP_Text placeholder)
                    placeholder.text = BoomboxText.Get("text.search_by_name");
            }
            var items = BoomboxDecorationCatalog.Items(Kind).Where(item => Matches(item, state)).ToArray();
            string list = group + ":" + search.text + ":" + language + ":"
                + string.Join("|", state.unlockedDecorationPrefabs)
                + ":"
                + string.Join("|", items.Select(i => i.name));
            string selection = language + ":" + selectedSlot + ":" + selectedPrefab + ":"
                + string.Join("|", state.unlockedDecorationSlots)
                + ":"
                + string.Join(
                "|",
                state.decorationPlacements.Select(p => p.slot + "=" + p.prefab)
            );
            if (list == listSignature && selection == selectionSignature)
                return;
            if (list != listSignature)
                RebuildGrid(items, state);
            listSignature = list;
            selectionSignature = selection;
            for (int i = 0; i < groups.Length; i++)
                BoomboxNativeUI.Select(groups[i], i == group);
            var slots = GroupSlots.ToArray();
            for (int i = 0; i < chips.Count; i++)
            {
                var chip = chips[i];
                if (i >= slots.Length)
                {
                    chip.gameObject.SetActive(false);
                    continue;
                }
                var slot = slots[i];
                var placement = state.decorationPlacements.FirstOrDefault(p => p.slot == slot.Index);
                bool unlocked = state.unlockedDecorationSlots.Contains(slot.Index);
                chip.gameObject.SetActive(true);
                chip.m_amount.gameObject.SetActive(true);
                chip.m_amount.text = slot.Number.ToString();
                chip.m_amount.color = unlocked ? Color.white : new Color(.55f, .55f, .55f);
                chip.m_icon.enabled = placement != null;
                if (placement != null)
                    chip.m_icon.sprite = BoomboxDecorationCatalog.Icon(placement.prefab);
                chip.m_icon.color = unlocked ? Color.white : new Color(.5f, .5f, .5f);
                chip.m_equiped.enabled = false;
                chip.m_selected.SetActive(slot.Index == selectedSlot);
                chip.m_tooltip.m_topic = BoomboxText.Get("text.slot") + slot.Number;
                chip.m_tooltip.m_text = !unlocked
                    ? BoomboxText.Get("text.locked")
                    : placement == null
                        ? BoomboxText.Get("text.empty")
                        : BoomboxDecorationCatalog.Name(BoomboxDecorationCatalog.Prefab(placement.prefab));
                chip.m_button.onClick = new Button.ButtonClickedEvent();
                int index = slot.Index;
                chip.m_button.onClick.AddListener(() => SlotSelected?.Invoke(index));
            }
            foreach (var entry in cells)
                entry.Value.m_selected.SetActive(entry.Key == selectedPrefab);
        }

        private void RebuildGrid(GameObject[] items, DeckState state)
        {
            string key = group + ":" + search.text;
            float scrollPosition = key == listKey ? gridScroll.verticalNormalizedPosition : 1f;
            listKey = key;
            cells.Clear();
            foreach (Transform child in gridContent)
            {
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
            foreach (var item in items)
            {
                var cell = CreateElement("Design_" + item.name);
                cell.transform.SetParent(gridContent, false);
                cell.m_amount.gameObject.SetActive(false);
                cell.m_icon.enabled = true;
                cell.m_icon.sprite = BoomboxDecorationCatalog.IconOf(item);
                cell.m_icon.color = Color.white;
                cell.m_equiped.enabled = state.unlockedDecorationPrefabs.Contains(item.name);
                cell.m_tooltip.m_topic = BoomboxDecorationCatalog.Name(item);
                cell.m_tooltip.m_text = BoomboxDecorationCatalog.Eligible(item, BoomboxDecorationKind.Figurine)
                    ? BoomboxText.Get("text.fits_stands_and_pendants")
                    : BoomboxText.Get("text.fits_pendants_only");
                string prefab = item.name;
                cell.m_button.onClick.AddListener(() => PrefabSelected?.Invoke(prefab));
                cells.Add(new KeyValuePair<string, InventoryElement>(prefab, cell));
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
            gridScroll.verticalNormalizedPosition = scrollPosition;
        }

        private bool Matches(GameObject item, DeckState state)
        {
            if (!BoomboxDecorationCatalog.Found(item, state))
                return false;
            string query = search.text?.Trim();
            return string.IsNullOrEmpty(query)
            || BoomboxDecorationCatalog.Name(item).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }

    [HarmonyPatch]
    internal static class BoomboxSearchInput
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] { "GetButtonDown", "GetButtonUp", "GetButton" }.Select(
            name => AccessTools.Method(typeof(ZInput), name, new[] { typeof(string) })
        );
        private static bool Prefix(ref bool __result)
        {
            if (!BoomboxTrophyCatalog.SearchFocused)
                return true;
            __result = false;
            return false;
        }
    }
}
