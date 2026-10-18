using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CaseFullPickup()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var inventory = player.GetInventory();
        var proof =
            new JObject
            {
                ["case"] = "C-13",
                ["scope"] =
                    "Full isolated inventory; actual hovered world item and E input. Fixture placement and inventory fill are preparation."
            };
        lastCapture = proof;
        var filler = new List<ItemDrop.ItemData>();
        var input = InputSystem.settings.backgroundBehavior;
        var distance = AccessTools.Field(typeof(GameCamera), "m_distance");
        float oldDistance = (float)distance.GetValue(GameCamera.instance), start = Time.realtimeSinceStartup;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            player.transform.position += Vector3.up * 20;
            Floor(player.transform.position);
            Physics.SyncTransforms();
            yield return null;
            var first = CaseDevice("C13-worn", "Red", 2);
            var second = CaseDevice("C13-world", "Blue", 7);
            var before = CaseStable(second);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.6f);
            float uiReady = Time.realtimeSinceStartup + 2;
            while (UiRaycast(UiInventoryItem(first), true).gameObject == null && Time.realtimeSinceStartup < uiReady)
                yield return null;
            var click = UiDispatch(UiInventoryItem(first), true);
            proof["equip"] = click;
            Need((bool)click["hitsTarget"], "C13 equip raycast missed");
            yield return UiAwait(() => BoomboxEquipment.Get(player) == first, "C13 occupied slot");
            InventoryGui.instance.Hide();
            plugin.Items.Update();
            var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
                .GetValue(plugin);
            var deck = registry[(string)before["id"]];
            var position = player.transform.position + player.transform.forward * 1.2f;
            Need(plugin.Items.PutDown(deck, position, Quaternion.identity, null), "C13 fixture placement failed");
            var drop = deck.WorldDrop;
            var prefab = ObjectDB.instance.GetItemPrefab("Wood");
            var wood = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            wood.m_dropPrefab = prefab;
            wood.m_stack = wood.m_shared.m_maxStackSize;
            wood.m_customData["boombox.qa.fixture"] = "C13-fill";
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
            Need(!inventory.CanAddItem(drop.m_itemData), "C13 full inventory precondition not met");
            int count = inventory.GetAllItems().Count;
            distance.SetValue(GameCamera.instance, 0f);
            GameObject hover = null;
            var aiming = new JArray();
            proof["aiming"] = aiming;
            var collider = drop.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
            var center = collider.bounds.center;
            for (int i = 0; i < 10; i++)
            {
                var aim = center - GameCamera.instance.transform.position;
                player.transform.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
                player.SetLookDir(aim, 0);
                AccessTools.Field(typeof(Player), "m_lookPitch").SetValue(
                    player,
                    Mathf.Atan2(-aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg
                );
                yield return new WaitForSecondsRealtime(.15f);
                hover = player.GetHoverObject();
                aiming.Add(new JObject
                {
                    ["center"] = Point(center),
                    ["camera"] = Point(GameCamera.instance.transform.position),
                    ["forward"] = Point(GameCamera.instance.transform.forward),
                    ["hover"] = hover?.name
                });
                if (hover != null && hover.GetComponentInParent<ItemDrop>() == drop)
                    break;
            }
            Need(hover != null && hover.GetComponentInParent<ItemDrop>() == drop, "C13 native aim missed world item");
            plugin.Notice = "";
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(.5f);
            proof["notice"] = plugin.Notice;
            proof["hover"] = hover.name;
            proof["filledCells"] = filler.Count;
            Need(plugin.Notice.Contains("нет места"), "C13 input did not produce full-inventory refusal");
            Need(
                drop != null && inventory.GetAllItems().Count == count && BoomboxEquipment.Get(player) == first,
                "C13 pickup changed inventory"
            );
            Need(JToken.DeepEquals(before, CaseStable(drop.m_itemData)), "C13 world payload changed");
            int copies = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Count(
                d => BoomboxItemData.IsDevice(d.m_itemData)
                    && BoomboxItemData.Read(d.m_itemData).deck.id == (string)before["id"]
            );
            Need(copies == 1, "C13 world item duplicated");
            proof["worldCopies"] = copies;
            proof["before"] = before;
            proof["after"] = CaseStable(drop.m_itemData);
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            foreach (var item in filler)
                inventory.RemoveItem(item);
            distance.SetValue(GameCamera.instance, oldDistance);
            InputSystem.settings.backgroundBehavior = input;
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
        }
    }
}
