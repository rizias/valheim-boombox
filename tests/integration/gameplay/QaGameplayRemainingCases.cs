using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private ItemDrop.ItemData CaseDevice(string tag, string property, int rank)
    {
        var item = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
        var data = BoomboxItemData.Read(item);
        var tape = BoomboxItemData.Read(BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab)).cassette;
        tape.properties[property] = rank;
        data.deck.insertedCassette = tape;
        data.deck.cassetteId = tape.id;
        data.deck.owner = Player.m_localPlayer.GetPlayerID();
        data.deck.playing = false;
        item.m_customData["boombox.qa.fixture"] = tag;
        BoomboxItemData.Write(item, data);
        Need(Player.m_localPlayer.GetInventory().AddItem(item), "Case fixture inventory full");
        Player.m_localPlayer.AddKnownItem(item);
        BoomboxPlugin.Instance.Items.Update();
        return item;
    }
    private IEnumerator CaseReadyInventory()
    {
        var player = Player.m_localPlayer;
        UiParkWindow();
        Prepare(player);
        Freeze(player, true);
        yield return null;
        if (CinematicsManager.IsPlaying())
            CinematicsManager.Stop();
        if (Valkyrie.m_instance != null)
            Valkyrie.m_instance.DropPlayer(true);
        player.SetIntro(false);
        player.SetSleeping(false);
        var animator = player.GetComponentInChildren<Animator>();
        for (int n = 0; n < 8 && player.InCutscene(); n++)
        {
            animator.Update(3f);
            yield return null;
        }
        Need(!player.InCutscene(), "Case actor not settled");
    }
    private static JObject CaseStable(ItemDrop.ItemData item)
    {
        var state = BoomboxItemData.Read(item).deck;
        return new JObject
        {
            ["id"] = state.id,
            ["tier"] = state.upgradeTier,
            ["cassette"] = state.insertedCassette == null
                ? JValue.CreateNull()
                : JObject.FromObject(state.insertedCassette),
            ["tracks"] = new JArray(state.tracks),
            ["volume"] = state.volume
        };
    }
    private IEnumerator RemainingCase(string id)
    {
        if (id == "UserFixes")
        {
            yield return CaseUserFixes();
            yield break;
        }
        if (id == "C13")
        {
            yield return CaseFullPickup();
            yield break;
        }
        if (id == "C16")
        {
            yield return CaseUiScale();
            yield break;
        }
        if (id == "D06")
        {
            yield return CaseFullEject();
            yield break;
        }
        if (id == "E08")
        {
            yield return CasePlaylistMetadata();
            yield break;
        }
        if (id == "F10")
        {
            yield return CaseCassetteCombinations();
            yield break;
        }
        if (id == "G06" || id == "G07")
        {
            yield return CaseEnemyWave(id);
            yield break;
        }
        if (id == "G08")
        {
            yield return CaseOrdinaryCombat();
            yield break;
        }
        if (id == "H07")
        {
            yield return CaseRecoverySwitches();
            yield break;
        }
        if (id == "H08save" || id == "H08load")
        {
            yield return CaseRecoveryReload(id == "H08save");
            yield break;
        }
        if (id == "K01")
        {
            yield return CaseTransportMotion();
            yield break;
        }
        if (id == "K02")
        {
            yield return CaseTransportDrop();
            yield break;
        }
        if (id == "K03save" || id == "K03load")
        {
            yield return CaseTransportPersistence(id == "K03save");
            yield break;
        }
        if (id == "K07" || id == "K08" || id == "K10")
        {
            yield return CaseLegacyMigration(id);
            yield break;
        }
        if (id.StartsWith("K09", StringComparison.Ordinal))
        {
            yield return CaseMigrationReceipt(id.Substring(3));
            yield break;
        }
        if (id == "L01save" || id == "L01load")
        {
            yield return CaseTutorials(id == "L01save");
            yield break;
        }
        if (id == "L03")
        {
            yield return CaseTutorials(false, true);
            yield break;
        }
        if (id == "L05")
        {
            yield return CaseLanguageFallback();
            yield break;
        }
        if (id == "L07save" || id == "L07load")
        {
            yield return CaseLongPlaylist(id == "L07save");
            yield break;
        }
        if (id == "L08save" || id == "L08load")
        {
            yield return CaseJourney(id == "L08save");
            yield break;
        }
        if (id == "C09")
        {
            yield return CaseShortcuts();
            yield break;
        }
        if (id == "C09manual")
        {
            yield return CaseShortcuts(true);
            yield break;
        }
        if (id != "C05" && id != "C06")
            throw new InvalidOperationException("Case not yet implemented: " + id);
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        float start = Time.realtimeSinceStartup;
        var proof =
            new JObject
            {
                ["case"] = "C-" + id.Substring(1),
                ["scope"] =
                    "Fixture items; actual inventory pointer selection/drop and ExtraSlots equip. No direct OnSelectedItem/EquipItem call for tested actions."
            };
        lastCapture = proof;
        var steps = new JArray();
        proof["steps"] = steps;
        var inputMode = InputSystem.settings.backgroundBehavior;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            Need(BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("shudnal.ExtraSlots"), "ExtraSlots not loaded");
            var first = CaseDevice("C05-A", "Red", 2);
            var second = CaseDevice("C05-B", "Blue", 7);
            var beforeA = CaseStable(first);
            var beforeB = CaseStable(second);
            Need(BoomboxEquipment.SlotPosition.HasValue, "Boombox slot missing");
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.35f);
            float gridReady = Time.realtimeSinceStartup + 2f;
            while (UiRaycast(UiInventoryItem(first), true).gameObject == null && Time.realtimeSinceStartup < gridReady)
                yield return null;
            Canvas.ForceUpdateCanvases();
            var firstRect = UiInventoryItem(first);
            var corners = new Vector3[4];
            firstRect.GetWorldCorners(corners);
            proof["firstCell"] = new JObject
            {
                ["grid"] = first.m_gridPos.ToString(),
                ["active"] = firstRect.gameObject.activeInHierarchy,
                ["corners"] = new JArray(corners.Select(c => c.ToString())),
                ["screen"] = Screen.width + "x" + Screen.height,
                ["inventoryVisible"] = InventoryGui.IsVisible()
            };
            var equip = UiDispatch(UiInventoryItem(first), true);
            steps.Add(equip);
            Need((bool)equip["hitsTarget"], "First equip raycast missed");
            yield return UiAwait(() => BoomboxEquipment.Get(player) == first, "first device equipped");
            proof["slotBefore"] = first.m_gridPos.ToString();
            if (id == "C06")
            {
                yield return CaseRejectedSlots(first, second, steps, proof);
                Need(
                    JToken.DeepEquals(beforeA, CaseStable(first)) && JToken.DeepEquals(beforeB, CaseStable(second)),
                    "Rejected drag changed payload"
                );
                proof["before"] = new JArray(beforeA, beforeB);
                proof["after"] = new JArray(CaseStable(first), CaseStable(second));
                proof["seconds"] = Time.realtimeSinceStartup - start;
                proof["passed"] = true;
                yield break;
            }
            var select = UiDispatch(UiInventoryItem(second), false);
            steps.Add(select);
            Need((bool)select["hitsTarget"], "Second item selection raycast missed");
            yield return null;
            var held = (ItemDrop.ItemData)AccessTools.Field(typeof(InventoryGui), "m_dragItem")
                .GetValue(InventoryGui.instance);
            Need(
                held != null && BoomboxItemData.Read(held).deck.id == (string)beforeB["id"],
                "Native drag did not pick second device"
            );
            var drop = UiDispatch(UiInventoryItem(first), false);
            steps.Add(drop);
            Need((bool)drop["hitsTarget"], "Occupied slot raycast missed");
            yield return UiAwait(
                () => BoomboxEquipment.Get(player) != null
                && BoomboxItemData.Read(BoomboxEquipment.Get(player)).deck.id == (string)beforeB["id"],
                "second device replaces first"
            );
            var devices = player.GetInventory().GetAllItems().Where(BoomboxItemData.IsDevice).ToArray();
            Need(devices.Count(i => i.m_equipped) == 1, "Not exactly one equipped device");
            foreach (var before in new[] { beforeA, beforeB })
            {
                var matches = devices.Where(i => BoomboxItemData.Read(i).deck.id == (string)before["id"]).ToArray();
                Need(matches.Length == 1, "Device lost or duplicated");
                Need(
                    JToken.DeepEquals(before, CaseStable(matches[0])),
                    "Device/cassette identity or properties changed"
                );
            }
            proof["before"] = new JArray(beforeA, beforeB);
            proof["after"] = new JArray(devices.Select(CaseStable));
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
            InputSystem.settings.backgroundBehavior = inputMode;
        }
    }

    private static RectTransform CaseCell(Vector2i position)
    {
        var element = AccessTools.Method(typeof(InventoryGrid), "GetElement").Invoke(
            InventoryGui.instance.m_playerGrid,
            new object[] { position.x, position.y, Player.m_localPlayer.GetInventory().GetWidth() }
        );
        return element == null
            ? null
            : (RectTransform)AccessTools.Method(element.GetType(), "GetElementRectTransform").Invoke(element, null);
    }

    private IEnumerator CaseRejectedSlots(
        ItemDrop.ItemData first,
        ItemDrop.ItemData second,
        JArray steps,
        JObject proof
    )
    {
        var inventory = Player.m_localPlayer.GetInventory();
        var wood = ObjectDB.instance.GetItemPrefab("Wood").GetComponent<ItemDrop>().m_itemData.Clone();
        wood.m_stack = 1;
        wood.m_customData["boombox.qa.fixture"] = "C06-wood";
        var empty = Enumerable.Range(0, 4).SelectMany(
            y => Enumerable.Range(0, inventory.GetWidth()).Select(x => new Vector2i(x, y))
        ).FirstOrDefault(
            p => inventory.GetItemAt(p.x, p.y) == null
        );
        Need(inventory.GetItemAt(empty.x, empty.y) == null, "C06 fixture space unavailable");
        var add = AccessTools.Method(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) }
        );
        Need(
            (bool)add.Invoke(inventory, new object[] { wood, 1, empty.x, empty.y, false }),
            "C06 raw material fixture rejected"
        );
        wood = inventory.GetItemAt(empty.x, empty.y);
        Need(
            wood != null && wood.m_customData.ContainsKey("boombox.qa.fixture"),
            "C06 material merged with existing stack"
        );
        yield return new WaitForSecondsRealtime(.2f);
        var woodPosition = wood.m_gridPos;
        var secondPosition = second.m_gridPos;
        int count = inventory.GetAllItems().Count;
        void Click(RectTransform target)
        {
            var route = UiDispatch(target, false);
            steps.Add(route);
            Need((bool)route["hitsTarget"], "C06 native raycast missed");
        }
        try
        {
            Click(UiInventoryItem(wood));
            yield return null;
            Click(UiInventoryItem(first));
            yield return new WaitForSecondsRealtime(.3f);
            proof["materialAfter"] = new JObject
            {
                ["equippedFirst"] = BoomboxEquipment.Get(Player.m_localPlayer) == first,
                ["position"] = wood.m_gridPos.ToString(),
                ["originalPosition"] = woodPosition.ToString(),
                ["exists"] = inventory.ContainsItem(wood),
                ["count"] = wood.m_stack
            };
            Need(
                BoomboxEquipment.Get(Player.m_localPlayer) == first
                    && wood.m_gridPos == woodPosition
                    && inventory.ContainsItem(wood)
                    && wood.m_stack == 1,
                "Wrong material changed occupied slot"
            );
            Click(UiInventoryItem(wood));
            yield return null;
            var api = AccessTools.TypeByName("ExtraSlots.API");
            var slots = (IEnumerable)AccessTools.Method(api, "GetEquipmentSlots").Invoke(null, null);
            RectTransform target = null;
            string targetName = null;
            foreach (var slot in slots)
            {
                var type = slot.GetType();
                if ((bool)AccessTools.Method(type, "ItemFits").Invoke(slot, new object[] { second }))
                    continue;
                var position = (Vector2i)AccessTools.Property(type, "GridPosition").GetValue(slot, null);
                var rect = CaseCell(position);
                if (rect == null || !rect.gameObject.activeInHierarchy || UiRaycast(rect, false).gameObject == null)
                    continue;
                target = rect;
                targetName = slot.ToString();
                break;
            }
            Need(target != null, "No visible incompatible equipment slot");
            proof["incompatibleSlot"] = targetName;
            Click(UiInventoryItem(second));
            yield return null;
            Click(target);
            yield return new WaitForSecondsRealtime(.3f);
            Need(
                BoomboxEquipment.Get(Player.m_localPlayer) == first && second.m_gridPos == secondPosition
                && inventory.GetAllItems().Count == count
                && inventory.ContainsItem(second),
                "Wrong special slot accepted device or changed inventory"
            );
            Click(UiInventoryItem(second));
            yield return null;
            proof["materialRejected"] = true;
            proof["incompatibleSlotRejected"] = true;
        }
        finally
        {
            inventory.RemoveItem(wood);
        }
    }
}
