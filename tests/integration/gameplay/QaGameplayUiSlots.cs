using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator UiSlots(JObject request)
    {
        Application.runInBackground = true;
        UiParkWindow();
        uiClicks = new JArray();
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        string deviceId = (string)request["carry"] ?? UiIdentity("device");
        var item = player.GetInventory().GetAllItems().Single(
            i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == deviceId
        );
        int start = plugin.Settings.DecorationSlotPriceStart.Value,
            step = plugin.Settings.DecorationSlotPriceStep.Value,
            cap = plugin.Settings.DecorationSlotPriceCap.Value;
        int[] order = { 17, 11, 6, 23, 1, 15, 8, 19, 2, 12, 21, 4, 16, 9, 18, 3, 13, 22, 5, 14, 20, 7, 10 };
        var proof =
            new JObject
            {
                ["scope"] = "UI06 only: actual SlotChip/DecorationAction events, configured balances unchanged",
                ["device"] = BoomboxItemData.Read(item).deck.id,
                ["prices"] = new JObject { ["start"] = start, ["step"] = step, ["cap"] = cap },
                ["order"] = new JArray(order),
                ["preparation"] =
                    "Existing UI-crafted item, raw coins/trophies, native roofed workbench and numeric alive pose; InventoryGui.Show only inventory preparation"
            };
        var rows = new JArray();
        var frames = new JArray();
        proof["opens"] = rows;
        proof["frames"] = frames;
        sample = proof;
        float coreStarted = Time.realtimeSinceStartup;
        var existingNotice = new BoomboxMessage("notice.library_summary", 1, 0);
        bool formatterFixture = true;
        Localization.instance.SetLanguage("Russian");
        string ru = existingNotice.ToString();
        Localization.instance.SetLanguage("English");
        string en = existingNotice.ToString();
        Localization.instance.SetLanguage("Russian");
        string ruAgain = existingNotice.ToString();
        proof["summaryLanguage"] = new JObject
        {
            ["original"] = existingNotice.ToString(),
            ["ru"] = ru,
            ["en"] = en,
            ["ruAgain"] = ruAgain,
            ["noRescan"] = true,
            ["formatterFixtureFromUi03"] = formatterFixture,
            ["renderClaim"] = false
        };
        if (!en.Contains(". Warnings: ") || en.Contains("Предупреждений") || ru != ruAgain)
            throw new InvalidOperationException("Existing summary RU/EN/RU failed");
        Func<int> coins = () => player.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null
            && i.m_dropPrefab.name == "Coins").Sum(
            i => i.m_stack
        );
        int required = Enumerable.Range(1, 23).Sum(count => cap > 0
            ? Math.Min(cap, Math.Max(0, start + step * (count - 1)))
            : Math.Max(0, start + step * (count - 1)));
        UiRawItem("Coins", Math.Max(0, required - coins()));
        UiRawItem("Wood", 4);
        UiRawItem("Resin", 2);
        UiRawItem("TrophySkeleton", 1);
        UiRawItem("MushroomYellow", 1);
        var origin = player.transform.position + Vector3.up * 35;
        var bench = UnityEngine.Object.Instantiate(
            ZNetScene.instance.GetPrefab("piece_workbench"),
            origin,
            Quaternion.identity
        );
        fixtures.Add(bench);
        var roof = bench.GetComponent<CraftingStation>().m_roofCheckPoint.position;
        var points = new[] {
            roof + Vector3.up,
            roof + Vector3.right * 3,
            roof + Vector3.left * 3,
            roof + Vector3.forward * 3,
            roof + Vector3.back * 3,
            origin - Vector3.up * .2f
        };
        var sizes = new[] {
            new Vector3(6, .25f, 6),
            new Vector3(.25f, 4, 6),
            new Vector3(.25f, 4, 6),
            new Vector3(6, 4, .25f),
            new Vector3(6, 4, .25f),
            new Vector3(10, .4f, 10)
        };
        for (int i = 0; i < points.Length; i++)
        {
            var surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            surface.layer = LayerMask.NameToLayer("piece");
            surface.transform.position = points[i];
            surface.transform.localScale = sizes[i];
            surface.GetComponent<MeshRenderer>().enabled = false;
            fixtures.Add(surface);
        }
        player.transform.position = origin + Vector3.forward * 1.5f;
        Freeze(player, true);
        Physics.SyncTransforms();
        InventoryGui.instance.Show(null, 1);
        yield return new WaitForSecondsRealtime(.4f);
        UiDispatch(UiButton("BoomboxMusicButton"), false);
        yield return new WaitForSecondsRealtime(.2f);
        UiDispatch(UiButton("Tab4"), false);
        yield return new WaitForSecondsRealtime(.2f);
        var deck = plugin.Current;
        if (deck.Item != item || player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost))
            throw new InvalidOperationException("UI06 actual owned item or paid-cost precondition failed");
        proof["before"] = JObject.FromObject(deck.State);
        proof["coinsBefore"] = coins();
        var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
        var panel = AccessTools.Field(menu.GetType(), "trophyPanel").GetValue(menu);
        foreach (int slot in order.Where(s => !deck.State.unlockedDecorationSlots.Contains(s)))
        {
            int group = slot >= 17
                ? 0
                : slot >= 11
                    ? 1
                    : 2;
            var groupEvent = UiDispatch(UiButton("Group" + group), false);
            yield return new WaitForSecondsRealtime(.16f);
            var slotButton = UiSlot(slot);
            var slotEvent = UiDispatch(slotButton, false);
            yield return new WaitForSecondsRealtime(.16f);
            int selected = (int)AccessTools.Field(panel.GetType(), "selectedSlot").GetValue(panel);
            int count = deck.State.unlockedDecorationSlots.Count, raw = Math.Max(
                0,
                start + step * (count - 1)
            ), expected = cap > 0
                ? Math.Min(raw, cap)
                : raw;
            var costs = (GameObject[])AccessTools.Field(panel.GetType(), "costs").GetValue(panel);
            var visibleAmounts = costs.Where(c => c.activeInHierarchy)
                .Select(c => c.transform.Find("res_amount")
                .GetComponent<TMP_Text>())
                .Where(
                t => t.gameObject.activeInHierarchy
            )
                .Select(
                t => t.text
            )
                .ToArray();
            var row = new JObject
            {
                ["slot"] = slot,
                ["group"] = group,
                ["chip"] = slotButton.name,
                ["selected"] = selected,
                ["countBefore"] = count,
                ["expected"] = expected,
                ["visibleAmounts"] = new JArray(visibleAmounts),
                ["groupEvent"] = groupEvent,
                ["slotEvent"] = slotEvent,
                ["coinsBefore"] = coins()
            };
            rows.Add(row);
            if (selected != slot || expected > 0 && !visibleAmounts.Contains(expected.ToString()))
                throw new InvalidOperationException("Native slot selection/visible price mismatch for " + slot);
            if (count == 1)
                yield return UiFrame("ui-06-first-free.png", frames);
            if (count == 23)
                yield return UiFrame("ui-06-last-price.png", frames);
            row["action"] = UiDispatch(UiButton("DecorationAction"), false);
            yield return UiAwait(() => deck.State.unlockedDecorationSlots.Contains(slot), "unlock slot " + slot);
            row["coinsAfter"] = coins();
            row["countAfter"] = deck.State.unlockedDecorationSlots.Count;
            row["passed"] = (int)row["coinsBefore"] - coins() == expected
                && deck.State.unlockedDecorationSlots.Count == count + 1;
            if (!(bool)row["passed"])
                throw new InvalidOperationException("Native slot cost delta mismatch for " + slot);
        }
        int coinsBeforeRepeat = coins();
        UiDispatch(UiButton("DecorationAction"), false);
        yield return new WaitForSecondsRealtime(.16f);
        proof["repeatNoCharge"] = coins() == coinsBeforeRepeat && deck.State.unlockedDecorationSlots.Count == 24;
        proof["repeatScope"] =
            "Repeated selected native DecorationAction did not charge coins; not a direct second UnlockSlot API call";
        proof["allSlots"] = new JArray(deck.State.unlockedDecorationSlots.OrderBy(s => s));
        proof["coinsAfter"] = coins();
        UiDispatch(UiButton("Group0"), false);
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiSlot(17), false);
        yield return new WaitForSecondsRealtime(.16f);
        bool deerKnown = player.IsMaterialKnown(
            ObjectDB.instance.GetItemPrefab("TrophyDeer").GetComponent<ItemDrop>().m_itemData.m_shared.m_name
        );
        proof["unknownFilter"] = new JObject
        {
            ["deerKnown"] = deerKnown,
            ["deerShown"] = UnityEngine.Object.FindObjectsByType<InventoryElement>(FindObjectsSortMode.None).Any(
                e => e.name == "Design_TrophyDeer" && e.gameObject.activeInHierarchy
            )
        };
        UiDispatch(UiElement("Design_TrophySkeleton"), false);
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiButton("DecorationAction"), false);
        yield return UiAwait(
            () => deck.State.unlockedDecorationPrefabs.Contains("TrophySkeleton"),
            "unlock known design"
        );
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiButton("DecorationAction"), false);
        yield return UiAwait(
            () => deck.State.decorationPlacements.Any(p => p.slot == 17 && p.prefab == "TrophySkeleton"),
            "install known design"
        );
        proof["installed"] = JObject.FromObject(deck.State.decorationPlacements.Single(p => p.slot == 17));
        UiDispatch(UiButton("ClearSlot"), false);
        yield return UiAwait(() => !deck.State.decorationPlacements.Any(p => p.slot == 17), "clear design");
        proof["cleared"] = true;
        UiDispatch(UiButton("Group2"), false);
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiSlot(0), false);
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiElement("Design_MushroomYellow"), false);
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiButton("DecorationAction"), false);
        yield return UiAwait(
            () => deck.State.unlockedDecorationPrefabs.Contains("MushroomYellow"),
            "unlock hanging design"
        );
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiButton("DecorationAction"), false);
        yield return UiAwait(
            () => deck.State.decorationPlacements.Any(p => p.slot == 0 && p.prefab == "MushroomYellow"),
            "install hanging design"
        );
        UiDispatch(UiElement("Design_TrophySkeleton"), false);
        yield return new WaitForSecondsRealtime(.16f);
        UiDispatch(UiButton("DecorationAction"), false);
        yield return UiAwait(
            () => deck.State.decorationPlacements.Any(p => p.slot == 0 && p.prefab == "TrophySkeleton"),
            "replace hanging design"
        );
        proof["replaced"] = true;
        proof["after"] = JObject.FromObject(deck.State);
        yield return UiFrame("ui-06-all24.png", frames);
        UiDispatch(UiButton("Close"), false);
        yield return new WaitForSecondsRealtime(.16f);
        Game.instance.SavePlayerProfile(false, false);
        proof["nativeProfileSaveRequested"] = true;
        proof["coreSeconds"] = Time.realtimeSinceStartup - coreStarted;
        proof["utc"] = DateTime.UtcNow.ToString("O");
    }

    private static RectTransform UiSlot(int index)
    {
        var catalog = typeof(BoomboxDecorationSlot).Assembly.GetType("VikingBoombox.BoomboxDecorationCatalog");
        var slots = (BoomboxDecorationSlot[])AccessTools.Field(catalog, "Slots").GetValue(null);
        string number = slots[index].Number.ToString();
        var element = UnityEngine.Object.FindObjectsByType<InventoryElement>(FindObjectsSortMode.None)
            .Single(e => e.gameObject.activeInHierarchy && e.name.StartsWith("SlotChip", StringComparison.Ordinal)
                && e.m_amount.text == number);
        return (RectTransform)element.m_button.transform;
    }

    private static RectTransform UiElement(
        string name
    ) => (RectTransform)UnityEngine.Object.FindObjectsByType<InventoryElement>(FindObjectsSortMode.None).Single(
        e => e.gameObject.activeInHierarchy && e.name == name
    ).m_button.transform;
    private static void UiRawItem(string name, int count)
    {
        var prefab = ObjectDB.instance.GetItemPrefab(name);
        var player = Player.m_localPlayer;
        while (count > 0)
        {
            var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab;
            int amount = Math.Min(count, item.m_shared.m_maxStackSize);
            item.m_stack = amount;
            if (!player.GetInventory().AddItem(item))
                throw new InvalidOperationException("UI raw preparation inventory full: " + name);
            player.AddKnownItem(item);
            count -= amount;
        }
    }
}
