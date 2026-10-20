using System.Collections.Generic;
using System.Linq;

namespace VikingBoombox
{
    internal sealed class BoomboxVisualSnapshot
    {
        private readonly string id;
        private readonly int tier;
        private readonly string cassettePrefab;
        private readonly Dictionary<string, int> properties;
        private readonly BoomboxDecorationPlacement[] placements;
        private readonly int[] unlockedSlots;
        private readonly string[] unlockedPrefabs;

        internal BoomboxVisualSnapshot(DeckState state)
        {
            id = state.id;
            tier = state.upgradeTier;
            cassettePrefab = state.insertedCassette?.prefab;
            properties = state.insertedCassette == null
                ? null
                : new Dictionary<string, int>(state.insertedCassette.properties);
            placements = state.decorationPlacements.Select(p => new BoomboxDecorationPlacement
            {
                slot = p.slot,
                prefab = p.prefab,
                effect = p.effect,
                effectParticles = p.effectParticles,
                extraParticles = p.extraParticles
            }).ToArray();
            unlockedSlots = state.unlockedDecorationSlots.ToArray();
            unlockedPrefabs = state.unlockedDecorationPrefabs.ToArray();
        }

        internal bool Matches(DeckState state, bool includeTier = true)
        {
            if (id != state.id || includeTier && tier != state.upgradeTier
                || cassettePrefab != state.insertedCassette?.prefab
                || (properties == null) != (state.insertedCassette == null)
                || placements.Length != state.decorationPlacements.Count
                || !unlockedSlots.SequenceEqual(state.unlockedDecorationSlots)
                || !unlockedPrefabs.SequenceEqual(state.unlockedDecorationPrefabs))
                return false;
            if (properties != null)
            {
                var current = state.insertedCassette.properties;
                if (properties.Count != current.Count)
                    return false;
                foreach (var property in properties)
                    if (!current.TryGetValue(property.Key, out int rank) || property.Value != rank)
                        return false;
            }
            for (int i = 0; i < placements.Length; i++)
            {
                var old = placements[i];
                var current = state.decorationPlacements[i];
                if (old.slot != current.slot || old.prefab != current.prefab || old.effect != current.effect
                    || old.effectParticles != current.effectParticles || old.extraParticles != current.extraParticles)
                    return false;
            }
            return true;
        }
    }
}
