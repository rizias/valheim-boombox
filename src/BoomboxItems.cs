using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxItems
    {
        private readonly BoomboxPlugin plugin;
        private static readonly HashSet<ItemDrop> worldItems = new HashSet<ItemDrop>();
        internal static IEnumerable<ItemDrop> WorldDrops => worldItems.Where(d => d != null
            && BoomboxItemData.IsDevice(d.m_itemData)).ToArray();
        private readonly HashSet<string> pendingEquip = new HashSet<string>();
        public BoomboxItems(BoomboxPlugin owner)
        {
            plugin = owner;
        }

        public void Migrate() => BoomboxMigration.Run(plugin);

        public static void RegisterDrop(ItemDrop drop)
        {
            if (drop != null && BoomboxItemData.IsOurs(drop.m_itemData))
                worldItems.Add(drop);
        }

        public AudioDeck Own => plugin.Decks.Values.FirstOrDefault(d => d != null
            && d.Item == BoomboxEquipment.Get(Player.m_localPlayer))
            ?? plugin.Decks.Values.FirstOrDefault(d => d != null
            && d.State.owner == plugin.Store.PlayerId
            && !d.StoragePaused)
            ?? plugin.Decks.Values.FirstOrDefault(
            d => d != null
            && d.Item != null
            && Player.m_localPlayer.GetInventory().ContainsItem(d.Item)
        );

        public AudioDeck Carried => plugin.Decks.Values.FirstOrDefault(
            d => d != null && d.Item != null && d.Item == BoomboxEquipment.Get(Player.m_localPlayer)
        )
        ?? plugin.Decks.Values.FirstOrDefault(
            d => d != null && d.Item != null && Player.m_localPlayer.GetInventory().ContainsItem(d.Item)
        );

        public void Update()
        {
            if (!BoomboxItemPrefabs.Ready || Player.m_localPlayer == null)
                return;
            BoomboxEquipment.Register();
            var inventory = Player.m_localPlayer.GetInventory();
            var live = new HashSet<string>();
            foreach (var item in inventory.GetAllItems().Where(BoomboxItemData.IsDevice))
            {
                var deck = Bind(item, null);
                deck.State.owner = Player.m_localPlayer.GetPlayerID();
                if (deck.State.insertedCassette == null)
                    BoomboxLocalization.Tutorial("assembly");
                if (pendingEquip.Remove(deck.State.id))
                    Player.m_localPlayer.EquipItem(item, false);
                live.Add(deck.State.id);
                bool worn = BoomboxEquipment.Get(Player.m_localPlayer) == item;
                if (deck.State.onBack != worn || deck.StoragePaused == worn || deck.WorldDrop != null)
                {
                    deck.Capture();
                    deck.WorldDrop = null;
                    deck.State.onBack = worn;
                    deck.StoragePaused = !worn;
                    deck.PlatformAnchor.ClearSupport();
                    if (worn)
                        deck.Mount.Follow(Player.m_localPlayer);
                }
                deck.Visual.SetActive(worn);
                deck.GetComponent<BoxCollider>().enabled = false;
            }
            worldItems.RemoveWhere(d => d == null);
            foreach (var drop in worldItems.ToArray())
            {
                if (!drop.gameObject.activeInHierarchy)
                    continue;
                var view = drop.GetComponent<ZNetView>();
                if (view == null || !view.IsValid())
                    continue;
                if (!BoomboxItemData.IsDevice(drop.m_itemData))
                {
                    if (view.IsOwner())
                        ApplyCassetteVisual(drop);
                    continue;
                }
                var payload = BoomboxItemData.Read(drop.m_itemData);
                if (payload.deck.owner != Player.m_localPlayer.GetPlayerID())
                    continue;
                if (live.Contains(payload.deck.id))
                    continue;
                var deck = Bind(drop.m_itemData, drop);
                live.Add(deck.State.id);
                if (deck.WorldDrop != drop || deck.State.onBack || deck.StoragePaused)
                {
                    deck.Capture();
                    deck.State.onBack = false;
                    deck.StoragePaused = false;
                    deck.WorldDrop = drop;
                    deck.Mount.StandOnGround();
                    deck.transform.SetParent(null, true);
                }
                var nativeVisual = drop.transform.Find("attach");
                if (nativeVisual != null)
                    nativeVisual.gameObject.SetActive(false);
                deck.Visual.SetActive(true);
                deck.GetComponent<BoxCollider>().enabled = false;
                if (string.IsNullOrEmpty(deck.State.platformGuid))
                {
                    deck.transform.SetPositionAndRotation(drop.transform.position, drop.transform.rotation);
                    deck.State.Position = drop.transform.position;
                    deck.State.yaw = drop.transform.eulerAngles.y;
                }
                else
                {
                    deck.PlatformAnchor.Follow();
                    FreezePlacedBody(drop);
                    drop.transform.SetPositionAndRotation(deck.transform.position, deck.transform.rotation);
                }
            }
            foreach (var entry in plugin.Decks.ToArray())
            {
                if (live.Contains(entry.Key))
                    continue;
                if (entry.Value != null)
                {
                    Persist(entry.Value);
                    UnityEngine.Object.Destroy(entry.Value.gameObject);
                }
                plugin.Decks.Remove(entry.Key);
            }
        }

        private AudioDeck Bind(ItemDrop.ItemData item, ItemDrop drop)
        {
            var bound = plugin.Decks.Values.FirstOrDefault(d => d != null && ReferenceEquals(d.Item, item));
            if (bound != null)
                return bound;
            var payload = BoomboxItemData.Read(item);
            if (!plugin.Decks.TryGetValue(payload.deck.id, out var deck) || deck == null)
            {
                payload.deck.onBack = drop == null && BoomboxEquipment.Get(Player.m_localPlayer) == item;
                if (drop == null)
                    payload.deck.owner = Player.m_localPlayer.GetPlayerID();
                else if (string.IsNullOrEmpty(payload.deck.platformGuid))
                {
                    payload.deck.Position = drop.transform.position;
                    payload.deck.yaw = drop.transform.eulerAngles.y;
                }
                deck = plugin.SpawnDeck(payload.deck);
                deck.StoragePaused = drop == null && !payload.deck.onBack;
                deck.WorldDrop = drop;
                if (drop != null)
                    deck.Mount.StandOnGround();
            }
            deck.Item = item;
            return deck;
        }
        internal AudioDeck ForItem(ItemDrop.ItemData item) => Bind(item, null);

        public static void Persist(AudioDeck deck)
        {
            if (deck == null || deck.Item == null)
                return;
            deck.Capture();
            BoomboxItemData.Write(deck.Item, new BoomboxItemPayload { deck = deck.State });
        }

        public void Flush()
        {
            foreach (var deck in plugin.Decks.Values)
                Persist(deck);
        }

        public bool PutDown(AudioDeck deck, Vector3 position, Quaternion rotation, Collider support)
        {
            var player = Player.m_localPlayer;
            if (deck?.Item == null || !player.GetInventory().ContainsItem(deck.Item))
                return false;
            Persist(deck);
            var state = deck.State;
            state.onBack = false;
            state.Position = position;
            state.yaw = rotation.eulerAngles.y;
            deck.transform.SetPositionAndRotation(position, rotation);
            deck.PlatformAnchor.ClearSupport();
            if (support != null)
                deck.PlatformAnchor.Bind(support);
            Persist(deck);
            var drop = ItemDrop.DropItem(deck.Item, 1, position, rotation);
            if (drop == null)
                return false;
            var source = deck.Item;
            player.UnequipItem(source, false);
            if (!player.GetInventory().RemoveItem(source))
            {
                ZNetScene.instance.Destroy(drop.gameObject);
                return false;
            }
            RegisterDrop(drop);
            deck.Item = drop.m_itemData;
            deck.WorldDrop = drop;
            deck.StoragePaused = false;
            deck.Mount.StandOnGround();
            FreezePlacedBody(drop);
            Persist(deck);
            plugin.Save();
            return true;
        }

        private static void FreezePlacedBody(ItemDrop drop)
        {
            var body = drop.GetComponent<Rigidbody>();
            if (body == null)
                return;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            var sync = drop.GetComponent<ZSyncTransform>();
            if (sync != null)
            {
                sync.m_syncBodyVelocity = false;
                AccessTools.Field(typeof(ZSyncTransform), "m_isKinematicBody").SetValue(sync, true);
            }
        }

        public bool PickUp(AudioDeck deck)
        {
            if (deck?.WorldDrop == null)
                return false;
            Persist(deck);
            var drop = deck.WorldDrop;
            if (!Player.m_localPlayer.GetInventory().CanAddItem(drop.m_itemData))
            {
                plugin.Notify("text.no_backpack_space_for_the_boombox");
                return false;
            }
            pendingEquip.Add(deck.State.id);
            drop.Pickup(Player.m_localPlayer);
            return true;
        }

        public bool InteractWorld(ItemDrop drop, bool alt)
        {
            RegisterDrop(drop);
            Update();
            var deck = plugin.Decks.Values.FirstOrDefault(d => d != null && d.WorldDrop == drop);
            if (deck == null)
            {
                if (alt || Player.m_localPlayer == null)
                    return false;
                drop.Pickup(Player.m_localPlayer);
                return true;
            }
            if (alt)
            {
                plugin.Open(deck);
                return true;
            }
            return PickUp(deck);
        }

        public bool Insert(AudioDeck deck, ItemDrop.ItemData item)
        {
            var inventory = Player.m_localPlayer.GetInventory();
            if (!plugin.CanControl(deck) || !BoomboxItemData.IsCassette(item) || !inventory.ContainsItem(item))
                return false;
            var cassette = BoomboxItemData.Read(item).cassette;
            BoomboxCassetteWorkshop.PreserveLegacyMusic(cassette);
            var previous = deck.State.insertedCassette;
            ItemDrop.ItemData returned = null;
            if (previous != null)
            {
                returned = BoomboxItemPrefabs.NewItem(previous.prefab);
                BoomboxItemData.Write(returned, new BoomboxItemPayload { cassette = previous });
            }
            var position = item.m_gridPos;
            if (!inventory.RemoveItem(item))
                return false;
            if (returned != null && !inventory.AddItem(returned, position))
            {
                if (!inventory.AddItem(item, position))
                    throw new InvalidOperationException(
                        BoomboxText.Get("text.could_not_restore_the_selected_cassette_after")
                    );
                plugin.Notify("text.replacement_cancelled_the_previous_cassette_could");
                return false;
            }
            deck.State.insertedCassette = cassette;
            deck.State.cassetteId = cassette.id;
            plugin.Notice = previous == null
                ? BoomboxText.Get("text.cassette_inserted_select_music_from_the_library")
                : BoomboxText.Get("text.cassette_replaced_the_previous_cassette_is_back");
            plugin.Save();
            return true;
        }

        public bool Extract(AudioDeck deck)
        {
            if (!plugin.CanControl(deck) || deck.State.insertedCassette == null)
                return false;
            var player = Player.m_localPlayer;
            if (BoomboxWorkstation.FreeBagCells(player).Count < 1)
                return InventoryFull(player);
            var cassette = deck.State.insertedCassette;
            var item = BoomboxItemPrefabs.NewItem(cassette.prefab);
            BoomboxItemData.Write(item, new BoomboxItemPayload { cassette = cassette });
            if (!player.GetInventory().AddItem(item))
                return InventoryFull(player);
            if (deck.State.playing)
                deck.TogglePlayback();
            deck.State.insertedCassette = null;
            deck.State.cassetteId = null;
            plugin.Notify("text.cassette_ejected_playlist_and_playback_position_are");
            plugin.Save();
            return true;
        }

        private bool InventoryFull(Player player)
        {
            plugin.Notice = Localization.instance.Localize("$msg_noroom");
            player.Message(MessageHud.MessageType.Center, "$msg_noroom");
            return false;
        }

        private static void ApplyCassetteVisual(ItemDrop drop)
        {
            var cassette = BoomboxItemData.Read(drop.m_itemData).cassette;
            var visual = drop.transform.Find("attach");
            if (visual != null)
                BoomboxCassetteAppearance.Apply(visual.gameObject, cassette);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Start")]
    internal static class BoomboxWorldItemStart
    {
        private static void Postfix(ItemDrop __instance) => BoomboxItems.RegisterDrop(__instance);
    }

    [HarmonyPatch(typeof(ItemDrop), "Interact")]
    internal static class BoomboxNativeInteraction
    {
        private static bool Prefix(ItemDrop __instance, Humanoid __0, bool __1, bool __2, ref bool __result)
        {
            if (!BoomboxItemData.IsDevice(__instance.m_itemData) || __0 != Player.m_localPlayer)
                return true;
            __result = !__1 && BoomboxPlugin.Instance.Items.InteractWorld(__instance, __2);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "GetHoverText")]
    internal static class BoomboxNativeHover
    {
        private static void Postfix(ItemDrop __instance, ref string __result)
        {
            if (BoomboxItemData.IsDevice(__instance.m_itemData))
                __result += Localization.instance.Localize("\n$boombox_world_hint");
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "Clone")]
    internal static class BoomboxBeforeClone
    {
        private static void Prefix(ItemDrop.ItemData __instance)
        {
            if (BoomboxItemData.IsOurs(__instance))
                BoomboxPlugin.Instance?.Items?.Flush();
        }
    }

    [HarmonyPatch(typeof(Inventory), "Save")]
    internal static class BoomboxBeforeInventorySave
    {
        private static void Prefix() => BoomboxPlugin.Instance?.Items?.Flush();
    }

    [HarmonyPatch(typeof(ItemDrop), "Save")]
    internal static class BoomboxBeforeWorldSave
    {
        private static void Prefix(ItemDrop __instance)
        {
            if (BoomboxItemData.IsDevice(__instance.m_itemData))
                BoomboxPlugin.Instance?.Items?.Flush();
        }
    }
}
