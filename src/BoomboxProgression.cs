using System;
using System.Collections.Generic;

namespace VikingBoombox
{
    public static class BoomboxProgression
    {
        public const int BaseTier = 0;
        public const int MaxTier = 8;
        public static int EnabledMaxTier => Math.Max(
            1,
            Math.Min(
                MaxTier,
                BoomboxPlugin.Instance?.Settings?.MaximumDeviceTier.Value ?? MaxTier
            )
        );

        static readonly UpgradeTier[] tiers =
        {
            new UpgradeTier(0, 0f, 0f, 0f, null, VisualStage.MusicBase),
            new UpgradeTier(
                1,
                1f,
                3f,
                6f,
                new UpgradeRecipe(
                    "piece_workbench",
                    1,
                    new UpgradeIngredient("Wood", 10),
                    new UpgradeIngredient("Resin", 6),
                    new UpgradeIngredient("LeatherScraps", 2),
                    new UpgradeIngredient("BoneFragments", 2),
                    new UpgradeIngredient("Coins", 50)
                ),
                VisualStage.MusicBase
            ),
            new UpgradeTier(
                2,
                10f,
                3.25f,
                7f,
                new UpgradeRecipe(
                    "forge",
                    1,
                    new UpgradeIngredient("TrophyEikthyr", 1),
                    new UpgradeIngredient("Bronze", 4),
                    new UpgradeIngredient("FineWood", 4)
                ),
                VisualStage.EikthyrTrophy
            ),
            new UpgradeTier(
                3,
                16f,
                3.5f,
                8f,
                new UpgradeRecipe(
                    "forge",
                    2,
                    new UpgradeIngredient("TrophyTheElder", 1),
                    new UpgradeIngredient("Iron", 6),
                    new UpgradeIngredient("LeatherScraps", 4)
                ),
                VisualStage.ElderTrophy
            ),
            new UpgradeTier(
                4,
                24f,
                3.75f,
                9.5f,
                new UpgradeRecipe(
                    "forge",
                    3,
                    new UpgradeIngredient("TrophyBonemass", 1),
                    new UpgradeIngredient("Silver", 6),
                    new UpgradeIngredient("Crystal", 4)
                ),
                VisualStage.BonemassTrophy
            ),
            new UpgradeTier(
                5,
                34f,
                4f,
                11f,
                new UpgradeRecipe(
                    "forge",
                    4,
                    new UpgradeIngredient("TrophyDragonQueen", 1),
                    new UpgradeIngredient("BlackMetal", 6),
                    new UpgradeIngredient("LinenThread", 4)
                ),
                VisualStage.ModerTrophy
            ),
            new UpgradeTier(
                6,
                46f,
                4.25f,
                12.5f,
                new UpgradeRecipe(
                    "blackforge",
                    1,
                    new UpgradeIngredient("TrophyGoblinKing", 1),
                    new UpgradeIngredient("Eitr", 4),
                    new UpgradeIngredient("BlackMarble", 6)
                ),
                VisualStage.YagluthTrophy
            ),
            new UpgradeTier(
                7,
                62f,
                4.5f,
                14f,
                new UpgradeRecipe(
                    "blackforge",
                    3,
                    new UpgradeIngredient("TrophySeekerQueen", 1),
                    new UpgradeIngredient("FlametalNew", 6),
                    new UpgradeIngredient("AskHide", 2)
                ),
                VisualStage.QueenTrophy
            ),
            new UpgradeTier(
                8,
                80f,
                4.75f,
                14f,
                new UpgradeRecipe(
                    "blackforge",
                    4,
                    new UpgradeIngredient("TrophyFader", 1),
                    new UpgradeIngredient("Gold", 6),
                    new UpgradeIngredient("Frostwood", 4)
                ),
                VisualStage.FaderTrophy
            )
        };

        public static UpgradeTier GetTier(int combatTier)
        {
            if (combatTier < BaseTier || combatTier > MaxTier)
                throw new ArgumentOutOfRangeException(
                    nameof(combatTier),
                    combatTier,
                    "Combat tier must be between 0 and " + MaxTier + "."
                );
            var tier = tiers[combatTier];
            if (tier.Recipe == null)
                return tier;
            var recipe = BoomboxPlugin.Instance?.Settings?.Recipe("Device " + combatTier, tier.Recipe) ?? tier.Recipe;
            return ReferenceEquals(recipe, tier.Recipe)
                ? tier
                : new UpgradeTier(
                tier.Level,
                tier.BluntDamage,
                tier.Radius,
                tier.RawPushForce,
                recipe,
                tier.VisualStage
            );
        }

        public static UpgradePlan PlanNextUpgrade(
            int currentTier,
            string stationPrefab,
            int stationLevel,
            IReadOnlyDictionary<string, int> inventory
        )
        {
            if (currentTier < BaseTier || currentTier > MaxTier)
                return new UpgradePlan(UpgradeStatus.InvalidTier, null, new List<UpgradeIngredient>());
            if (currentTier >= EnabledMaxTier)
                return new UpgradePlan(UpgradeStatus.MaximumTier, null, new List<UpgradeIngredient>());
            var target = GetTier(currentTier + 1);
            var missing = target.Recipe.FindMissing(inventory);
            var status = target.Recipe.CheckStation(stationPrefab, stationLevel);
            if (status == UpgradeStatus.Ready && missing.Count > 0)
                status = UpgradeStatus.MissingMaterials;
            return new UpgradePlan(status, target, missing);
        }
    }

    public enum UpgradeStatus
    {
        Ready,
        InvalidTier,
        MaximumTier,
        WrongStation,
        StationLevelTooLow,
        MissingMaterials
    }

    public enum VisualStage
    {
        MusicBase,
        BronzeRings,
        IronPlates,
        CrystalRunes,
        BlackMetalGuards,
        EitrCoils,
        FlametalFrame,
        NorthernResonator,
        EikthyrTrophy,
        ElderTrophy,
        BonemassTrophy,
        ModerTrophy,
        YagluthTrophy,
        QueenTrophy,
        FaderTrophy
    }

    public sealed class UpgradeIngredient
    {
        public string Prefab
        {
            get;
        }
        public int Amount
        {
            get;
        }

        internal UpgradeIngredient(string prefab, int amount)
        {
            Prefab = prefab;
            Amount = amount;
        }
    }

    public sealed class UpgradeRecipe
    {
        public string StationPrefab
        {
            get;
        }
        public int StationLevel
        {
            get;
        }
        public IReadOnlyList<UpgradeIngredient> Ingredients
        {
            get;
        }

        internal UpgradeRecipe(string stationPrefab, int stationLevel, params UpgradeIngredient[] ingredients)
        {
            StationPrefab = stationPrefab;
            StationLevel = stationLevel;
            Ingredients = Array.AsReadOnly((UpgradeIngredient[])ingredients.Clone());
        }

        internal List<UpgradeIngredient> FindMissing(IReadOnlyDictionary<string, int> inventory)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            var missing = new List<UpgradeIngredient>();
            foreach (var required in Ingredients)
            {
                int available = 0;
                foreach (var entry in inventory)
                {
                    if (!string.Equals(entry.Key, required.Prefab, StringComparison.Ordinal))
                        continue;
                    available = Math.Max(0, entry.Value);
                    break;
                }
                if (available < required.Amount)
                    missing.Add(new UpgradeIngredient(required.Prefab, required.Amount - available));
            }
            return missing;
        }

        internal UpgradeStatus CheckStation(string prefab, int level)
        {
            if (!string.Equals(prefab, StationPrefab, StringComparison.Ordinal))
                return UpgradeStatus.WrongStation;
            return level < StationLevel ? UpgradeStatus.StationLevelTooLow : UpgradeStatus.Ready;
        }
    }

    public sealed class UpgradeTier
    {
        public int Level
        {
            get;
        }
        public float BluntDamage
        {
            get;
        }
        public float Radius
        {
            get;
        }
        public float RawPushForce
        {
            get;
        }
        public UpgradeRecipe Recipe
        {
            get;
        }
        public VisualStage VisualStage
        {
            get;
        }

        internal UpgradeTier(
            int level,
            float damage,
            float radius,
            float push,
            UpgradeRecipe recipe,
            VisualStage visual
        )
        {
            Level = level;
            BluntDamage = damage;
            Radius = radius;
            RawPushForce = push;
            Recipe = recipe;
            VisualStage = visual;
        }
    }

    public sealed class UpgradePlan
    {
        public bool Allowed
        {
            get
            {
                return Status == UpgradeStatus.Ready;
            }
        }
        public UpgradeStatus Status
        {
            get;
        }
        public UpgradeTier TargetTier
        {
            get;
        }
        public IReadOnlyList<UpgradeIngredient> MissingIngredients
        {
            get;
        }

        internal UpgradePlan(UpgradeStatus status, UpgradeTier targetTier, List<UpgradeIngredient> missing)
        {
            Status = status;
            TargetTier = targetTier;
            MissingIngredients = missing.AsReadOnly();
        }
    }
}
