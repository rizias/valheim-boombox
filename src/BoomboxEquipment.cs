using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace VikingBoombox
{
    public static class BoomboxEquipment
    {
        public const string SlotId = "ValheimBoombox";
        private static Type api;
        private static MethodInfo addSlotAfter, findSlot, isGridPosition;
        private static PropertyInfo gridPosition;
        private static bool registered;
        private static bool absent;
        private static readonly Dictionary<Humanoid, ItemDrop.ItemData> equipped =
            new Dictionary<Humanoid, ItemDrop.ItemData>();
        public static bool Known => Player.m_localPlayer != null
            && Player.m_localPlayer.IsKnownMaterial("$boombox_name");

        public static void Register()
        {
            Prune();
            if (registered || absent)
                return;
            try
            {
                if (api == null)
                {
                    api = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("ExtraSlots.API", false))
                        .FirstOrDefault(
                        t => t != null
                    );
                    if (api == null)
                    {
                        absent = true;
                        return;
                    }
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                    addSlotAfter = api.GetMethod(
                        "AddSlotAfter",
                        flags,
                        null,
                        new[] {
                        typeof(string),
                        typeof(Func<string>),
                        typeof(Func<ItemDrop.ItemData, bool>),
                        typeof(Func<bool>),
                        typeof(string[])
                        },
                        null
                    );
                    findSlot = api.GetMethod("FindSlot", flags, null, new[] { typeof(string) }, null);
                    isGridPosition = api.GetMethod(
                        "IsGridPositionASlot",
                        flags,
                        null,
                        new[] { typeof(Vector2i) },
                        null
                    );
                    gridPosition = findSlot?.ReturnType.GetProperty(
                        "GridPosition",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                    if (addSlotAfter?.ReturnType != typeof(bool)
                        || findSlot == null
                        || isGridPosition?.ReturnType != typeof(bool)
                        || gridPosition?.PropertyType != typeof(Vector2i)
                        || gridPosition.GetGetMethod() == null)
                        throw new MissingMethodException("Unsupported ExtraSlots API shape");
                }
                registered = UseSlotApi(() => (bool)addSlotAfter.Invoke(
                        null,
                        new object[] {
                        SlotId,
                        new Func<string>(() => Known ? BoomboxText.Get("text.boombox") : ""),
                        new Func<ItemDrop.ItemData, bool>(BoomboxItemData.IsDevice),
                        new Func<bool>(() => true),
                        new[] { "HipLantern" }
                    }
                    ));
                if (!registered)
                    registered = SlotPosition.HasValue;
            }
            catch (Exception error)
            {
                DisableSlotApi(error);
            }
        }

        private static void DisableSlotApi(Exception error)
        {
            api = null;
            addSlotAfter = findSlot = isGridPosition = null;
            gridPosition = null;
            registered = false;
            absent = true;
            BoomboxPlugin.Instance?.LogError(
                "ExtraSlots integration unavailable; using vanilla equipment: " + error.GetBaseException().Message
            );
        }

        private static T UseSlotApi<T>(Func<T> action)
        {
            if (api == null)
                return default(T);
            try
            {
                return action();
            }
            catch (Exception error)
            {
                DisableSlotApi(error);
                return default(T);
            }
        }
        public static Vector2i? SlotPosition
        {
            get
            {
                return UseSlotApi(() =>
                {
                    var slot = findSlot.Invoke(null, new object[] { SlotId });
                    return slot == null ? (Vector2i?)null : (Vector2i)gridPosition.GetValue(slot, null);
                });
            }
        }

        private static void Prune()
        {
            foreach (var entry in equipped.ToArray())
                if (entry.Key == null || entry.Value == null || !entry.Key.GetInventory().ContainsItem(entry.Value))
                    equipped.Remove(entry.Key);
        }

        public static ItemDrop.ItemData Get(Humanoid owner)
        {
            Prune();
            return owner != null && equipped.TryGetValue(owner, out var item) ? item : null;
        }

        public static bool Equip(Humanoid owner, ItemDrop.ItemData item)
        {
            Register();
            if (owner == null
                || !BoomboxItemData.IsDevice(item)
                || !owner.GetInventory().ContainsItem(item)
                || owner.InAttack()
                || owner.InDodge()
                || owner.IsDead()
                || owner.IsSwimming()
                && !owner.IsOnGround())
                return false;
            var previous = Get(owner);
            if (previous == item)
                return false;
            if (previous != null)
                owner.UnequipItem(previous, false);
            equipped[owner] = item;
            item.m_equipped = true;
            var data = BoomboxItemData.Read(item);
            if (owner is Player player)
                data.deck.owner = player.GetPlayerID();
            data.deck.onBack = true;
            BoomboxItemData.Write(item, data);
            var slot = SlotPosition;
            if (slot.HasValue
                && item.m_gridPos != slot.Value
                && owner.GetInventory().GetItemAt(slot.Value.x, slot.Value.y) == null)
            {
                item.m_gridPos = slot.Value;
                AccessTools.Method(typeof(Inventory), "Changed").Invoke(
                    owner.GetInventory(),
                    new object[] { false, false }
                );
            }
            AccessTools.Method(typeof(Humanoid), "SetupEquipment").Invoke(owner, null);
            if (owner == Player.m_localPlayer)
                BoomboxLocalization.Tutorial("equipment");
            return true;
        }

        public static void Unequip(Humanoid owner, ItemDrop.ItemData item)
        {
            if (owner == null || item == null)
                return;
            if (equipped.TryGetValue(owner, out var current) && current == item)
                equipped.Remove(owner);
            item.m_equipped = false;
            AccessTools.Method(typeof(Humanoid), "SetupEquipment").Invoke(owner, null);
        }

        public static void Clear() => equipped.Clear();

        internal static bool IsSlotPosition(
            Vector2i position
        ) => SlotPosition.HasValue && SlotPosition.Value == position;
        internal static bool IsExtraPosition(
            Vector2i position
        ) => UseSlotApi(() => (bool)isGridPosition.Invoke(null, new object[] { position }));
        internal static bool IsQueued(
            Player player,
            ItemDrop.ItemData item
        ) => (bool)AccessTools.Method(typeof(Player), "IsEquipActionQueued").Invoke(player, new object[] { item });
        internal static void Queue(Player player, ItemDrop.ItemData item)
        {
            if (item != null && !IsQueued(player, item))
                AccessTools.Method(typeof(Player), "QueueEquipAction").Invoke(player, new object[] { item });
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "IsEquipable")]
    internal static class BoomboxEquipable
    {
        private static void Postfix(ItemDrop.ItemData __instance, ref bool __result)
        {
            if (BoomboxItemData.IsDevice(__instance))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "EquipItem")]
    internal static class BoomboxEquip
    {
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!BoomboxItemData.IsDevice(item))
                return true;
            __result = BoomboxEquipment.Equip(__instance, item);
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "IsItemEquiped")]
    internal static class BoomboxEquipped
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (BoomboxItemData.IsDevice(item))
                __result = BoomboxEquipment.Get(__instance) == item;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "UnequipItem")]
    internal static class BoomboxUnequip
    {
        private static void Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (BoomboxItemData.IsDevice(item))
                BoomboxEquipment.Unequip(__instance, item);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "UnequipAllItems")]
    internal static class BoomboxUnequipAll
    {
        private static void Prefix(Humanoid __instance)
        {
            var item = BoomboxEquipment.Get(__instance);
            if (item != null)
                __instance.UnequipItem(item, false);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class BoomboxSlotDrag
    {
        internal sealed class Drag
        {
            public string Id;
            public bool WasEquipped;
            public bool WasQueued;
            public bool ToSlot;
            public ItemDrop.ItemData Displaced;
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("shudnal.ExtraSlots")]
        private static void Prefix(InventoryGui __instance, InventoryGrid __0, Vector2i __2, out Drag __state)
        {
            __state = null;
            var player = Player.m_localPlayer;
            if (player == null || __0 != __instance.m_playerGrid)
                return;
            var dragObject = (UnityEngine.GameObject)AccessTools.Field(typeof(InventoryGui), "m_dragGo")
                .GetValue(__instance);
            var item = (ItemDrop.ItemData)AccessTools.Field(typeof(InventoryGui), "m_dragItem").GetValue(__instance);
            if (dragObject == null || !BoomboxItemData.IsDevice(item) || !BoomboxEquipment.SlotPosition.HasValue)
                return;
            __state = new Drag
            {
                Id = BoomboxItemData.Read(item).deck.id,
                WasEquipped = GetEquipped(player, item),
                WasQueued = BoomboxEquipment.IsQueued(player, item),
                ToSlot = BoomboxEquipment.IsSlotPosition(__2)
            };
            var inventory = player.GetInventory();
            var occupant = inventory.GetItemAt(__2.x, __2.y);
            if (__state.ToSlot
                && occupant != item
                && BoomboxItemData.IsDevice(occupant)
                && GetEquipped(player, occupant))
            {
                __state.Displaced = occupant;
                player.UnequipItem(occupant, false);
            }
            bool emptyBagTarget = !BoomboxEquipment.IsExtraPosition(__2)
                && __2.x >= 0
                && __2.x < inventory.GetWidth()
                && __2.y >= 0
                && __2.y < inventory.GetHeight()
                && inventory.GetItemAt(__2.x, __2.y) == null;
            if (!emptyBagTarget)
                return;
            if (__state.WasQueued)
                player.RemoveEquipAction(item);
            if (__state.WasEquipped)
                player.UnequipItem(item, false);
        }

        private static bool GetEquipped(Player player, ItemDrop.ItemData item) => BoomboxEquipment.Get(player) == item;

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("shudnal.ExtraSlots")]
        private static void Postfix(Drag __state)
        {
            if (__state == null || Player.m_localPlayer == null)
                return;
            var player = Player.m_localPlayer;
            RestoreDisplaced(__state);
            var actual = player.GetInventory().GetAllItems().FirstOrDefault(
                i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == __state.Id
            );
            if (actual == null)
                return;
            if (!BoomboxEquipment.IsExtraPosition(actual.m_gridPos))
            {
                player.RemoveEquipAction(actual);
                if (GetEquipped(player, actual))
                    player.UnequipItem(actual, false);
                return;
            }
            if (!BoomboxEquipment.IsSlotPosition(actual.m_gridPos))
                return;
            if (__state.WasEquipped)
                player.EquipItem(actual, false);
            else if (__state.ToSlot || __state.WasQueued)
                BoomboxEquipment.Queue(player, actual);
        }

        private static void RestoreDisplaced(Drag state)
        {
            var player = Player.m_localPlayer;
            if (state?.Displaced != null && player != null && player.GetInventory().ContainsItem(state.Displaced)
            && BoomboxEquipment.IsSlotPosition(state.Displaced.m_gridPos) && !GetEquipped(player, state.Displaced))
                player.EquipItem(state.Displaced, false);
        }

        private static void Finalizer(Drag __state) => RestoreDisplaced(__state);
    }
}
