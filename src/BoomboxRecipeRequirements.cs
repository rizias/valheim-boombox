using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxRecipeRequirements : MonoBehaviour
    {
        private GameObject fifth;
        private GameObject[] expanded;
        internal GameObject[] Expand(GameObject[] original)
        {
            if (fifth == null)
            {
                fifth = Instantiate(original[original.Length - 1], original[0].transform.parent);
                fifth.name = "BoomboxRecipeRequirement5";
                var positions = original.Select(obj => ((RectTransform)obj.transform).anchoredPosition).ToArray();
                float rowSpacing = positions.Select(p => p.y).Distinct().OrderBy(y => y).Zip(
                    positions.Select(p => p.y).Distinct().OrderBy(y => y).Skip(1),
                    (a, b) => b - a
                ).DefaultIfEmpty(
                    ((RectTransform)original[0].transform).rect.height
                ).Min();
                ((RectTransform)fifth.transform).anchoredPosition = new Vector2(
                    positions[0].x,
                    positions.Max(p => p.y) + rowSpacing
                );
                expanded = original.Concat(new[] { fifth }).ToArray();
            }
            fifth.SetActive(true);
            return expanded;
        }
        internal void Hide()
        {
            if (fifth != null)
                fifth.SetActive(false);
        }
        internal static bool IsRecipe(InventoryGui gui)
        {
            var selected = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe").GetValue(gui);
            var recipe = selected == null
                ? null
                : AccessTools.Property(selected.GetType(), "Recipe")?.GetValue(selected, null) as Recipe
                    ?? AccessTools.Field(selected.GetType(), "Recipe")?.GetValue(selected) as Recipe;
            return recipe != null && recipe.name == "Recipe_ValheimBoombox_Item" && recipe.m_resources.Length == 5;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupRequirementList")]
    internal static class BoomboxFiveRecipeRequirements
    {
        private static void Prefix(InventoryGui __instance, out GameObject[] __state)
        {
            __state = __instance.m_recipeRequirementList;
            var view = __instance.GetComponent<BoomboxRecipeRequirements>();
            if (!BoomboxRecipeRequirements.IsRecipe(__instance) || __state.Length != 4)
            {
                view?.Hide();
                return;
            }
            if (view == null)
                view = __instance.gameObject.AddComponent<BoomboxRecipeRequirements>();
            __instance.m_recipeRequirementList = view.Expand(__state);
        }
        private static void Finalizer(InventoryGui __instance, GameObject[] __state)
        {
            __instance.m_recipeRequirementList = __state;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class BoomboxRecipeRequirementsHidden
    {
        private static void Postfix(InventoryGui __instance)
        {
            if (!BoomboxRecipeRequirements.IsRecipe(__instance))
                __instance.GetComponent<BoomboxRecipeRequirements>()?.Hide();
        }
    }
}
