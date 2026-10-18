using System.Collections.Generic;
using System.Linq;

namespace VikingBoombox
{
    internal static class BoomboxWorkstation
    {
        internal static UpgradeRecipe CostFor(
            AudioDeck deck,
            string property
        ) => BoomboxCassetteProgression.Recipe(
            BoomboxPlugin.Instance.Settings,
            deck.State.insertedCassette.Rank(property) + 1
        );

        internal static string UpgradeUnavailable(AudioDeck deck, string property)
        {
            var plugin = BoomboxPlugin.Instance;
            if (!plugin.CanControl(deck))
                return BoomboxText.Get("text.move_closer_to_your_boombox");
            if (deck.State.insertedCassette == null)
                return BoomboxText.Get("text.install_a_cassette_first");
            if (!BoomboxItemData.Properties.Contains(property))
                return BoomboxText.Get("text.unknown_property");
            if (deck.State.insertedCassette.Rank(property) >= BoomboxCassetteWorkshop.MaximumRank)
                return BoomboxText.Get("text.maximum_rank_reached");
            if (!BoomboxCassetteProgression.CanAdvance(
                deck.State.insertedCassette,
                property,
                BoomboxCassetteWorkshop.MaximumRank
            ))
                return BoomboxText.Get("text.invalid_cassette_rank");
            var cost = CostFor(deck, property);
            var player = Player.m_localPlayer;
            var station = BoomboxAmplifier.FindStation(player, cost);
            if (station == null || station.GetLevel(true) < cost.StationLevel)
                return BoomboxText.Get("text.requires_an_accessible") + BoomboxAmplifier.StationName(cost.StationPrefab)
                    + " "
                    + cost.StationLevel
                    + ".";
            return BoomboxCraftingRequirements.CanAfford(player, cost, "BoomboxCassetteRequirements")
                ? ""
                : BoomboxText.Get("text.not_enough_materials");
        }

        internal static bool Upgrade(AudioDeck deck, string property)
        {
            var plugin = BoomboxPlugin.Instance;
            string reason = UpgradeUnavailable(deck, property);
            if (reason.Length != 0)
            {
                plugin.Notice = reason;
                return false;
            }
            var cost = CostFor(deck, property);
            if (!BoomboxCraftingRequirements.Spend(Player.m_localPlayer, cost, "BoomboxCassetteRequirements"))
                return false;
            var cassette = deck.State.insertedCassette;
            if (!BoomboxCassetteProgression.TryAdvance(cassette, property, BoomboxCassetteWorkshop.MaximumRank))
                return false;
            BoomboxItems.Persist(deck);
            plugin.Notice = Localization.instance.Localize("$boombox_property_" + property.ToLowerInvariant()) + ": "
                + cassette.Rank(property);
            plugin.Save();
            Game.instance?.SavePlayerProfile(false, false);
            return true;
        }

        internal static List<Vector2i> FreeBagCells(Player player)
        {
            var inventory = player.GetInventory();
            var cells = new List<Vector2i>();
            for (int y = 0; y < inventory.GetHeight(); y++)
                for (int x = 0; x < inventory.GetWidth(); x++)
                {
                    var cell = new Vector2i(x, y);
                    if (!BoomboxEquipment.IsExtraPosition(cell) && inventory.GetItemAt(x, y) == null)
                        cells.Add(cell);
                }
            return cells;
        }
    }
}
