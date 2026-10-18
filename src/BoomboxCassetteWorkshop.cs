using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingBoombox
{
    public static class BoomboxCassetteWorkshop
    {
        private static readonly UpgradeRecipe baseRecipe = new UpgradeRecipe(
            "piece_workbench",
            1,
            new UpgradeIngredient("Wood", 2),
            new UpgradeIngredient("Resin", 1),
            new UpgradeIngredient("BoneFragments", 1)
        );
        public static UpgradeRecipe BaseRecipe
        {
            get
            {
                var recipe = BoomboxPlugin.Instance?.Settings?.Recipe("Cassette basic", baseRecipe) ?? baseRecipe;
                return recipe.Ingredients.Any(i => i.Prefab == "BoneFragments")
                    ? recipe
                    : new UpgradeRecipe(
                    recipe.StationPrefab,
                    recipe.StationLevel,
                    recipe.Ingredients.Concat(new[] { new UpgradeIngredient("BoneFragments", 1) }).ToArray()
                );
            }
        }
        public static int MaximumRank => BoomboxCassetteProgression.Limit(
            BoomboxPlugin.Instance?.Settings?.MaximumCassetteRank.Value ?? 7
        );
        public static string Name(
            RecordedCassette cassette
        ) => Localization.instance.Localize(
            cassette != null && cassette.properties.Count > 0
                ? "$boombox_cassette_upgraded"
                : "$boombox_cassette_archive"
        );
        internal static string Effect(string property, int rank)
        => EffectFromBalance(property, rank, BoomboxAuraNetwork.Balance);

        internal static string EffectFromBalance(string property, int rank, BoomboxAuraBalance balance)
        {
            if (rank <= 0)
                return "—";
            if (balance == null)
                return BoomboxText.Get("text.waiting_for_server_settings");
            int index = Array.IndexOf(BoomboxAuraSources.Properties, property);
            float value = BoomboxAuraMath.CassetteAmount(index, rank, balance) * balance.Strength[index];
            if (index == 0 || index == 4)
                return "+" + (value * 100).ToString("0.##", BoomboxText.Culture) + "%";
            return "+" + value.ToString("0.##", BoomboxText.Culture) + BoomboxText.Get("text.every")
                + balance.Period[index - 1].ToString("0.##", BoomboxText.Culture)
                + BoomboxText.Get("text.s");
        }
        public static string Statistics(RecordedCassette cassette)
        {
            if (cassette == null || cassette.properties.Count == 0)
                return BoomboxText.Get("text.no_additional_effects");
            var balance = BoomboxAuraNetwork.Balance;
            var lines = new List<string>();
            foreach (string property in BoomboxItemData.Properties)
            {
                int rank = cassette.Rank(property);
                if (rank == 0)
                    continue;
                string effect = EffectFromBalance(property, rank, balance);
                lines.Add(
                    "• " + Localization.instance.Localize("$boombox_property_" + property.ToLowerInvariant()) + " "
                        + rank
                        + ": "
                        + effect
                );
            }
            lines.Add(
                BoomboxText.Get("text.effects_apply_while_music_is_playing")
            );
            if (balance?.MusicOnly == true || !BoomboxPlugin.Instance.Settings.LocalGameplayEnabled)
                lines.Add(
                    BoomboxText.Get("text.gameplay_effects_are_disabled_by_music_only_mode")
                );
            return string.Join("\n", lines);
        }
        public static string Describe(RecordedCassette cassette) => cassette.properties.Count == 0
            ? Localization.instance.Localize("$boombox_property_none")
            : string.Join(
            " · ",
            BoomboxItemData.Properties.Where(p => cassette.Rank(p) > 0).Select(
                p => Localization.instance.Localize("$boombox_property_" + p.ToLowerInvariant()) + " "
                        + cassette.Rank(p)
            )
        );

        internal static void PreserveLegacyMusic(RecordedCassette cassette)
        {
            if (cassette != null && cassette.tracks.Count > 0)
                BoomboxPlugin.Instance.MusicLibrary.Import("cassette:" + cassette.id, cassette.name, cassette.tracks);
        }
    }
}
