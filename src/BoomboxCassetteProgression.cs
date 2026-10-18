using System;

namespace VikingBoombox
{
    public static class BoomboxCassetteProgression
    {
        public const int MaxRank = 7;
        private static readonly UpgradeRecipe[] recipes =
        {
            new UpgradeRecipe(
                "piece_workbench",
                1,
                new UpgradeIngredient("TrophyEikthyr",1),
                new UpgradeIngredient("Bronze",1),
                new UpgradeIngredient("FineWood",1)
            ),
            new UpgradeRecipe(
                "piece_workbench",
                1,
                new UpgradeIngredient("TrophyTheElder",1),
                new UpgradeIngredient("Iron",2),
                new UpgradeIngredient("LeatherScraps",1)
            ),
            new UpgradeRecipe(
                "piece_workbench",
                1,
                new UpgradeIngredient("TrophyBonemass",1),
                new UpgradeIngredient("Silver",2),
                new UpgradeIngredient("Crystal",1)
            ),
            new UpgradeRecipe(
                "piece_workbench",
                1,
                new UpgradeIngredient("TrophyDragonQueen",1),
                new UpgradeIngredient("BlackMetal",2),
                new UpgradeIngredient("LinenThread",1)
            ),
            new UpgradeRecipe(
                "piece_workbench",
                1,
                new UpgradeIngredient("TrophyGoblinKing",1),
                new UpgradeIngredient("Eitr",1),
                new UpgradeIngredient("BlackMarble",2)
            ),
            new UpgradeRecipe(
                "piece_workbench",
                1,
                new UpgradeIngredient("TrophySeekerQueen",1),
                new UpgradeIngredient("FlametalNew",2),
                new UpgradeIngredient("AskHide",1)
            ),
            new UpgradeRecipe(
                "piece_workbench",
                1,
                new UpgradeIngredient("TrophyFader",1),
                new UpgradeIngredient("Gold",2),
                new UpgradeIngredient("Frostwood",1)
            )
        };
        public static int Limit(int configured) => Math.Max(1, Math.Min(MaxRank, configured));
        public static UpgradeRecipe DefaultRecipe(int targetRank)
        {
            if (targetRank < 1 || targetRank > MaxRank)
                throw new ArgumentOutOfRangeException(nameof(targetRank));
            return recipes[targetRank - 1];
        }
        public static UpgradeRecipe Recipe(BoomboxSettings settings, int targetRank)
        {
            var original = DefaultRecipe(targetRank);
            return settings?.Recipe("Cassette rank " + targetRank, original) ?? original;
        }
        public static bool CanAdvance(RecordedCassette cassette, string property, int configuredMaximum)
        {
            if (cassette?.properties == null || Array.IndexOf(BoomboxItemData.Properties, property) < 0)
                return false;
            int rank = cassette.Rank(property);
            return rank >= 0 && rank < Limit(configuredMaximum);
        }
        public static bool TryAdvance(RecordedCassette cassette, string property, int configuredMaximum)
        {
            if (!CanAdvance(cassette, property, configuredMaximum))
                return false;
            cassette.properties[property] = cassette.Rank(property) + 1;
            cassette.prefab = BoomboxItemData.UpgradedPrefab;
            return true;
        }
    }
}
