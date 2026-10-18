using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    public static class BoomboxMigration
    {
        public const string WorldReceiptKey = "local.valheim.boombox.migration";

        public static void Run(BoomboxPlugin plugin)
        {
            if (!BoomboxItemPrefabs.Ready)
                throw new InvalidOperationException(BoomboxText.Get("text.boombox_items_are_not_registered_yet"));
            var player = Player.m_localPlayer;
            var pending = plugin.Store.World.decks.Where(
                d => d.owner == plugin.Store.PlayerId && !plugin.Store.World.migratedDecks.Contains(d.id)
            ).ToArray();
            if (pending.Length == 0)
                return;
            BoomboxEquipment.Register();
            var savedWorldItems = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(
                BoomboxItemData.DevicePrefab,
                savedWorldItems,
                ref index
            ))
            {
            }
            foreach (var state in pending)
            {
                var existing = FindInventoryItem(player, state.id);
                if (existing == null && !savedWorldItems.Any(zdo => MatchesWorldItem(zdo, state.id)))
                    Deliver(plugin, player, state);
                plugin.Store.World.migratedDecks.Add(state.id);
                plugin.Store.Save();
            }
        }

        private static ItemDrop.ItemData FindInventoryItem(
            Player player,
            string id
        ) => player.GetInventory().GetAllItems().FirstOrDefault(
            i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == id
        );

        private static bool MatchesWorldItem(ZDO zdo, string id)
        {
            if (zdo == null)
                return false;
            if (zdo.GetString(WorldReceiptKey, "") == id)
                return true;
            var item = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
            ItemDrop.LoadFromZDO(item, zdo, -1);
            return item.m_customData.ContainsKey(BoomboxItemData.Key) && BoomboxItemData.Read(item).deck.id == id;
        }

        private static void Deliver(BoomboxPlugin plugin, Player player, DeckState state)
        {
            var inventory = player.GetInventory();
            if (state.insertedCassette == null && state.tracks.Count > 0)
            {
                var recording = plugin.Store.Library.cassettes.FirstOrDefault(c => c.id == state.cassetteId);
                state.insertedCassette = new RecordedCassette
                {
                    id = string.IsNullOrEmpty(state.cassetteId) ? Guid.NewGuid().ToString("N") : state.cassetteId,
                    name = recording?.name ?? "",
                    prefab = BoomboxItemData.ArchivePrefab,
                    tracks = new List<string>(state.tracks)
                };
                state.cassetteId = state.insertedCassette.id;
            }
            var item = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
            BoomboxItemData.Write(item, new BoomboxItemPayload { deck = state });
            bool inInventory = false;
            ItemDrop drop = null;
            bool committed = false;
            try
            {
                if (state.onBack)
                {
                    inInventory = inventory.AddItem(item);
                    var slot = BoomboxEquipment.SlotPosition;
                    if (!inInventory && slot.HasValue && inventory.GetItemAt(slot.Value.x, slot.Value.y) == null)
                        inInventory = inventory.AddItem(item, slot.Value);
                }
                if (inInventory)
                {
                    item = FindInventoryItem(player, state.id);
                    player.EquipItem(item, false);
                    committed = BoomboxMigrationSave.SaveProfile();
                }
                else
                {
                    Vector3 position = state.onBack
                        ? player.transform.position + player.transform.forward
                        : state.Position;
                    var payload = BoomboxItemData.Read(item);
                    payload.deck.onBack = false;
                    payload.deck.Position = position;
                    BoomboxItemData.Write(item, payload);
                    drop = ItemDrop.DropItem(item, 1, position, Quaternion.Euler(0, state.yaw, 0));
                    if (drop == null)
                        throw new InvalidOperationException(BoomboxText.Get("text.could_not_migrate_the_previous_boombox_the"));
                    var view = drop.GetComponent<ZNetView>();
                    if (view == null || !view.IsValid() || !view.IsOwner())
                        throw new InvalidOperationException(
                            BoomboxText.Get("text.could_not_confirm_ownership_of_the_new")
                        );
                    view.GetZDO().Set(WorldReceiptKey, state.id);
                    AccessTools.Method(typeof(ItemDrop), "Save").Invoke(drop, null);
                    committed = BoomboxMigrationSave.SaveWorld();
                }
                if (!committed)
                    throw new InvalidOperationException(
                        BoomboxText.Get("text.migration_save_was_not_confirmed_the_original")
                    );
                if (drop != null)
                    BoomboxItems.RegisterDrop(drop);
            }
            finally
            {
                if (!committed)
                {
                    if (inInventory && item != null)
                    {
                        player.UnequipItem(item, false);
                        inventory.RemoveItem(item);
                    }
                    if (drop != null && ZNetScene.instance != null)
                        ZNetScene.instance.Destroy(drop.gameObject);
                }
            }
        }
    }

    public static class BoomboxMigrationSave
    {
        internal sealed class Attempt
        {
            public object Owner;
            public volatile bool Observed;
            public volatile bool Success;
        }
        private static volatile Attempt requestedWorld;
        [ThreadStatic] private static Attempt worldThread;
        [ThreadStatic] private static Attempt profileCall;

        internal static bool SaveProfile()
        {
            var attempt = new Attempt { Owner = Game.instance.GetPlayerProfile() };
            var previous = profileCall;
            profileCall = attempt;
            try
            {
                Game.instance.SavePlayerProfile(false, false);
            }
            finally
            {
                profileCall = previous;
            }
            return attempt.Observed && attempt.Success;
        }

        internal static bool SaveWorld()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return false;
            var priorThread = (System.Threading.Thread)AccessTools.Field(typeof(ZNet), "m_saveThread")
                .GetValue(ZNet.instance);
            if (priorThread != null && priorThread.IsAlive)
                priorThread.Join();
            var attempt = new Attempt { Owner = ZNet.instance };
            var previous = requestedWorld;
            requestedWorld = attempt;
            try
            {
                AccessTools.Method(typeof(ZNet), "Save", new[] { typeof(bool), typeof(bool), typeof(bool) }).Invoke(
                    ZNet.instance,
                    new object[] { true, false, false }
                );
            }
            finally
            {
                requestedWorld = previous;
            }
            return attempt.Observed && attempt.Success;
        }

        internal static Attempt BeginWorldThread(ZNet owner)
        {
            var previous = worldThread;
            var request = requestedWorld;
            worldThread = request != null && ReferenceEquals(request.Owner, owner) ? request : null;
            return previous;
        }
        internal static void EndWorldThread(Attempt previous) => worldThread = previous;
        internal static void WorldResult(bool success)
        {
            if (worldThread != null)
            {
                worldThread.Success = success;
                worldThread.Observed = true;
            }
        }
        internal static void ProfileResult(PlayerProfile owner, bool success)
        {
            if (profileCall != null && ReferenceEquals(profileCall.Owner, owner))
            {
                profileCall.Success = success;
                profileCall.Observed = true;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerProfile), "Save", new Type[] { })]
    internal static class BoomboxMigrationProfileSaved
    {
        private static void Postfix(
            PlayerProfile __instance,
            bool __result
        ) => BoomboxMigrationSave.ProfileResult(__instance, __result);
    }

    [HarmonyPatch(typeof(ZNet), "SaveWorldThread")]
    internal static class BoomboxMigrationWorldSaved
    {
        private static void Prefix(
            ZNet __instance,
            out BoomboxMigrationSave.Attempt __state
        ) => __state = BoomboxMigrationSave.BeginWorldThread(__instance);
        private static Exception Finalizer(BoomboxMigrationSave.Attempt __state, Exception __exception)
        {
            BoomboxMigrationSave.EndWorldThread(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "EndSave")]
    internal static class BoomboxMigrationSaveResult
    {
        private static void Postfix(bool __1) => BoomboxMigrationSave.WorldResult(__1);
    }
}
