using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxAuraSources
    {
        internal static readonly string[] Properties = Enumerable.Range(0, BoomboxAuraMath.BonusCount)
            .Select(index => BoomboxAuraMath.CassetteProperty((BoomboxDecorationBonus)index)).ToArray();

        internal static List<BoomboxAuraSource> Local()
        {
            var result = new List<BoomboxAuraSource>();
            var plugin = BoomboxPlugin.Instance;
            var player = Player.m_localPlayer;
            if (plugin == null || player == null || !plugin.AudioWorldActive)
                return result;
            foreach (var deck in plugin.Decks.Values)
            {
                if (deck == null || deck.State.owner != player.GetPlayerID() || deck.Item == null)
                    continue;
                var state = deck.State;
                var source = new BoomboxAuraSource
                {
                    Id = state.id,
                    Owner = state.owner,
                    OwnerName = player.GetPlayerName(),
                    Worn = state.onBack,
                    Active = !player.IsDead()
                        && !player.IsTeleporting()
                        && !deck.StoragePaused
                        && (deck.IsActivePlayback || deck.IsSyntheticActive),
                    Position = deck.transform.position,
                    WorldItem = deck.WorldDrop?.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None
                };
                source.Active &= plugin.Settings.LocalGameplayEnabled;
                for (int index = 0; index < 5; index++)
                    source.Ranks[index] = state.insertedCassette?.Rank(Properties[index]) ?? 0;
                var slots = new HashSet<int>();
                foreach (var placement in state.decorationPlacements)
                {
                    if (placement == null
                        || !slots.Add(placement.slot)
                        || !state.unlockedDecorationSlots.Contains(placement.slot)
                        || !state.unlockedDecorationPrefabs.Contains(placement.prefab)
                        || !BoomboxDecorationCatalog.DataEligible(placement.prefab, placement.slot))
                        continue;
                    source.Decorations.Add(new BoomboxAuraDecoration
                    {
                        Slot = placement.slot,
                        Prefab = placement.prefab,
                        Effect = (int)BoomboxDecorationCatalog.DataEffect(
                            placement.prefab,
                            placement.effect,
                            BoomboxAuraNetwork.Balance?.Fallback ?? plugin.Settings.DecorationModFallback.Value
                        )
                    });
                }
                result.Add(source);
            }
            return result;
        }

        internal static bool Identity(ZNetPeer peer, out ZDO character, out long player, out string name)
        {
            character = peer != null && peer.IsReady() ? ZDOMan.instance?.GetZDO(peer.m_characterID) : null;
            player = character?.GetLong(ZDOVars.s_playerID, 0L) ?? 0L;
            name = character?.GetString(ZDOVars.s_playerName, "") ?? "";
            return player != 0L && character != null && character.GetOwner() == peer.m_uid;
        }

        internal static bool ValidateAndNormalize(BoomboxAuraSource source, long peer, long player, string name, ZDO character)
        {
            if (source.Owner != player || source.Id == null || character == null
            || source.Decorations.Any(d => !BoomboxDecorationCatalog.DataEligible(d.Prefab, d.Slot)))
                return false;
            source.Peer = peer;
            source.OwnerName = name.Length > 128 ? name.Substring(0, 128) : name;
            source.Active &= !character.GetBool(ZDOVars.s_dead, false);
            if (source.Worn)
            {
                if (!source.WorldItem.IsNone())
                    return false;
                return Vector3.Distance(source.Position, character.GetPosition()) <= 5f;
            }
            if (source.WorldItem.IsNone())
                return !source.Active;
            var item = ZDOMan.instance.GetZDO(source.WorldItem);
            if (item == null || item.GetPrefab() != BoomboxItemData.DevicePrefab.GetStableHashCode())
                return false;
            var data = new ItemDrop.ItemData
            {
                m_dropPrefab = ZNetScene.instance?.GetPrefab(BoomboxItemData.DevicePrefab)
            };
            ItemDrop.LoadFromZDO(data, item, -1);
            var saved = BoomboxItemData.Read(data).deck;
            if (saved.id != source.Id || saved.owner != player && item.GetOwner() != peer)
                return false;
            source.Position = item.GetPosition();
            for (int index = 0; index < 5; index++)
                if (source.Ranks[index] != (saved.insertedCassette?.Rank(Properties[index]) ?? 0))
                    return false;
            var placements = saved.decorationPlacements.Where(
                p => p != null
                    && saved.unlockedDecorationSlots.Contains(p.slot)
                    && saved.unlockedDecorationPrefabs.Contains(p.prefab)
                    && BoomboxDecorationCatalog.DataEligible(p.prefab, p.slot)
            ).ToArray();
            return placements.Length == source.Decorations.Count
                && source.Decorations.All(
                d => placements.Any(
                    p => p.slot == d.Slot
                && p.prefab == d.Prefab
                && (int)BoomboxDecorationCatalog.DataEffect(p.prefab, p.effect, BoomboxAuraNetwork.Balance.Fallback)
                == d.Effect
                )
            );
        }
    }
}
