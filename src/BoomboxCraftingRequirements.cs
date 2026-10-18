using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxCraftingRequirements
    {
        private static readonly FieldInfo selectionField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly FieldInfo requirementListField = AccessTools.Field(typeof(InventoryGui), "m_reqList");
        private static readonly FieldInfo stationField = AccessTools.Field(typeof(Player), "m_currentStation");
        private static readonly MethodInfo setupList = AccessTools.Method(typeof(InventoryGui), "SetupRequirementList");
        private static readonly FieldInfo batchAmount = FindBatchAmount();
        private static ObjectDB database;
        private static Recipe recipe;
        private static string signature;
        private static UpgradeRecipe customCost;
        private static string customName;

        internal static Recipe Recipe
        {
            get
            {
                var cost = customCost
                    ?? throw new InvalidOperationException("A specific crafting recipe must be selected.");
                string key = cost.StationPrefab + ":" + cost.StationLevel + ":"
                    + string.Join(
                    ",",
                    cost.Ingredients.Select(i => i.Prefab + ":" + i.Amount)
                );
                if (recipe != null && database == ObjectDB.instance && signature == key)
                    return recipe;
                if (recipe != null)
                    UnityEngine.Object.Destroy(recipe);
                database = ObjectDB.instance;
                signature = key;
                recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = customName ?? "BoomboxCassetteRequirements";
                recipe.m_item = database.GetItemPrefab(BoomboxItemData.UpgradedPrefab).GetComponent<ItemDrop>();
                recipe.m_amount = 1;
                recipe.m_enabled = false;
                recipe.m_craftingStation = ZNetScene.instance.GetPrefab(
                    cost.StationPrefab
                )?.GetComponent<CraftingStation>();
                recipe.m_minStationLevel = cost.StationLevel;
                recipe.m_resources = cost.Ingredients.Select(i => new Piece.Requirement
                {
                    m_resItem = database.GetItemPrefab(i.Prefab)?.GetComponent<ItemDrop>(),
                    m_amount = i.Amount,
                    m_amountPerLevel = 0,
                    m_recover = true
                }).ToArray();
                return recipe;
            }
        }

        private static FieldInfo FindBatchAmount()
        {
            var type = AccessTools.TypeByName("AzuAntiArthriticCrafting.Patches.InventoryGuiShowCraftingPanelPatch");
            return type == null ? null : AccessTools.Field(type, "CurrentCraftAmount");
        }

        private static bool FreeCraft(
            Player player
        ) => player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);

        internal static bool CanAfford(Player player)
        {
            var current = Recipe;
            if (current.m_resources.Any(r => r.m_resItem == null))
                return false;
            using (new CraftingScope(player))
                return FreeCraft(player) || player.HaveRequirements(current, false, 1, 1);
        }

        internal static bool Spend(Player player)
        {
            var current = Recipe;
            if (current.m_resources.Any(r => r.m_resItem == null))
                return false;
            using (new CraftingScope(player))
            {
                if (FreeCraft(player))
                    return true;
                if (!player.HaveRequirements(current, false, 1, 1))
                    return false;
                player.ConsumeResources(current.m_resources, 1, -1, 1);
                return true;
            }
        }

        internal static bool CanAfford(
            Player player,
            UpgradeRecipe cost,
            string name
        ) => With(cost, name, () => CanAfford(player));
        internal static bool Spend(
            Player player,
            UpgradeRecipe cost,
            string name
        ) => With(cost, name, () => Spend(player));
        internal static void Draw(GameObject[] elements, UpgradeRecipe cost, string name) => With(
        cost,
        name,
        () =>
            {
                Draw(elements);
                return true;
            }
        );

        private static T With<T>(UpgradeRecipe cost, string name, Func<T> action)
        {
            var previousCost = customCost;
            var previousName = customName;
            customCost = cost;
            customName = name;
            try
            {
                return action();
            }
            finally
            {
                customCost = previousCost;
                customName = previousName;
            }
        }

        internal static void Draw(GameObject[] elements)
        {
            var gui = InventoryGui.instance;
            var player = Player.m_localPlayer;
            var current = Recipe;
            if (gui == null || player == null || elements.Length == 0)
                return;
            var previousElements = gui.m_recipeRequirementList;
            object previousSelection = selectionField.GetValue(gui);
            object previousList = requirementListField.GetValue(gui);
            using (new CraftingScope(player))
            {
                try
                {
                    var selection = Activator.CreateInstance(
                        selectionField.FieldType,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new object[] { current, null, null, true },
                        null
                    );
                    selectionField.SetValue(gui, selection);
                    requirementListField.SetValue(gui, new List<Piece.Requirement>());
                    gui.m_recipeRequirementList = elements;
                    setupList.Invoke(gui, new object[] { 1, player, true, 1 });
                }
                finally
                {
                    gui.m_recipeRequirementList = previousElements;
                    selectionField.SetValue(gui, previousSelection);
                    requirementListField.SetValue(gui, previousList);
                }
            }
        }

        private sealed class CraftingScope : IDisposable
        {
            private readonly Player player;
            private readonly object station;
            private readonly object amount;
            internal CraftingScope(Player owner)
            {
                player = owner;
                station = stationField.GetValue(player);
                amount = batchAmount?.GetValue(null);
                stationField.SetValue(
                    player,
                    BoomboxAmplifier.FindStation(
                        player,
                        customCost
                            ?? throw new InvalidOperationException("A specific crafting recipe must be selected.")
                    )
                );
                batchAmount?.SetValue(null, 1);
            }
            public void Dispose()
            {
                stationField.SetValue(player, station);
                if (batchAmount != null)
                    batchAmount.SetValue(null, amount);
            }
        }
    }
}
