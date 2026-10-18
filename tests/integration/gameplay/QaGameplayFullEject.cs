using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CaseFullEject()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var inventory = player.GetInventory();
        var proof =
            new JObject
            {
                ["case"] = "D-06",
                ["scope"] =
                    "Actual cassette Eject button with full bag, then one freed slot; fixture cassette payload and raw inventory fill."
            };
        lastCapture = proof;
        var filler = new List<ItemDrop.ItemData>();
        var steps = new JArray();
        proof["steps"] = steps;
        var input = InputSystem.settings.backgroundBehavior;
        string tapeId = null;
        float start = Time.realtimeSinceStartup;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            var item = CaseDevice("D06", "Blue", 7);
            var tape = BoomboxItemData.Read(item).deck.insertedCassette;
            tapeId = tape.id;
            var before = JObject.FromObject(tape);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.6f);
            float uiReady = Time.realtimeSinceStartup + 2;
            while (UiRaycast(UiInventoryItem(item), true).gameObject == null && Time.realtimeSinceStartup < uiReady)
                yield return null;
            var equip = UiDispatch(UiInventoryItem(item), true);
            steps.Add(equip);
            Need((bool)equip["hitsTarget"], "D06 equip raycast missed");
            yield return UiAwait(() => BoomboxEquipment.Get(player) == item, "D06 equip");
            var prefab = ObjectDB.instance.GetItemPrefab("Wood");
            var wood = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            wood.m_dropPrefab = prefab;
            wood.m_stack = wood.m_shared.m_maxStackSize;
            wood.m_customData["boombox.qa.fixture"] = "D06-fill";
            var api = AccessTools.TypeByName("ExtraSlots.API");
            var slots = ((IEnumerable)AccessTools.Method(api, "GetExtraSlots")
                .Invoke(null, null)).Cast<object>()
                .ToArray();
            var add = AccessTools.Method(
                typeof(Inventory),
                "AddItem",
                new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) }
            );
            for (int y = 0; y < inventory.GetHeight(); y++)
                for (int x = 0; x < inventory.GetWidth(); x++)
                {
                    if (inventory.GetItemAt(x, y) != null)
                        continue;
                    var pos = new Vector2i(x, y);
                    var slot = slots.FirstOrDefault(
                        s => (Vector2i)AccessTools.Property(s.GetType(), "GridPosition").GetValue(s, null) == pos
                    );
                    if (slot != null
                        && !(bool)AccessTools.Method(slot.GetType(), "ItemFits").Invoke(slot, new object[] { wood }))
                        continue;
                    var raw = wood.Clone();
                    if ((bool)add.Invoke(inventory, new object[] { raw, raw.m_stack, x, y, false }))
                        filler.Add(inventory.GetItemAt(x, y));
                }
            Need(
                !inventory.CanAddItem(BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab)),
                "D06 full bag precondition failed"
            );
            int count = inventory.GetAllItems().Count;
            void Click(string name)
            {
                var route = UiDispatch(UiButton(name), false);
                steps.Add(route);
                Need((bool)route["hitsTarget"], "D06 raycast missed " + name);
            }
            Click("BoomboxMusicButton");
            yield return new WaitForSecondsRealtime(.25f);
            Click("Tab3");
            yield return new WaitForSecondsRealtime(.25f);
            Need(plugin.Current.State.id == BoomboxItemData.Read(item).deck.id, "D06 menu selected wrong device");
            plugin.Notice = "";
            Click("CassetteEject");
            yield return new WaitForSecondsRealtime(.25f);
            Need(
                plugin.Current.State.insertedCassette != null
                && JToken.DeepEquals(before, JObject.FromObject(plugin.Current.State.insertedCassette))
                && inventory.GetAllItems().Count == count,
                "Full bag extraction lost cassette or modified inventory"
            );
            proof["refusal"] = plugin.Notice;
            Need(!string.IsNullOrWhiteSpace(plugin.Notice), "D06 full bag refusal has no feedback");
            var released = filler.First(i => i.m_gridPos.y < 4);
            inventory.RemoveItem(released);
            filler.Remove(released);
            yield return null;
            Click("CassetteEject");
            yield return new WaitForSecondsRealtime(.3f);
            var copies = inventory.GetAllItems().Where(
                i => BoomboxItemData.IsCassette(i) && BoomboxItemData.Read(i).cassette.id == tapeId
            ).ToArray();
            Need(
                plugin.Current.State.insertedCassette == null && copies.Length == 1,
                "D06 extraction did not return exactly one cassette"
            );
            Need(
                JToken.DeepEquals(before, JObject.FromObject(BoomboxItemData.Read(copies[0]).cassette)),
                "D06 extracted payload changed"
            );
            proof["before"] = before;
            proof["after"] = JObject.FromObject(BoomboxItemData.Read(copies[0]).cassette);
            proof["copies"] = copies.Length;
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            foreach (var item in filler)
                inventory.RemoveItem(item);
            if (tapeId != null)
                foreach (var item in inventory.GetAllItems().Where(
                    i => BoomboxItemData.IsCassette(i) && BoomboxItemData.Read(i).cassette.id == tapeId
                ).ToArray())
                    inventory.RemoveItem(item);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
            InputSystem.settings.backgroundBehavior = input;
        }
    }
}
