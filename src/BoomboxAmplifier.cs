using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxAmplifier : MonoBehaviour
    {
        public string UpgradeMessage
        {
            get;
            private set;
        } = "";
        public UpgradeTier Tier => BoomboxProgression.GetTier(deck.State.upgradeTier);
        private AudioDeck deck;
        private BoomboxAmplifierVisual appearance;

        public void Initialize(AudioDeck ownerDeck)
        {
            deck = ownerDeck;
            appearance = gameObject.AddComponent<BoomboxAmplifierVisual>();
            appearance.Initialize(deck);
        }

        public UpgradePlan GetUpgradePlan()
        {
            return PlanFor(Tier.Level, Player.m_localPlayer);
        }

        public static UpgradePlan PlanFor(int currentTier, Player player)
        {
            var inventory = CountInventory(player);
            if (currentTier >= BoomboxProgression.EnabledMaxTier)
                return BoomboxProgression.PlanNextUpgrade(currentTier, null, 0, inventory);
            var next = BoomboxProgression.GetTier(currentTier + 1);
            var station = FindStation(player, next.Recipe);
            return BoomboxProgression.PlanNextUpgrade(
                currentTier,
                station != null ? next.Recipe.StationPrefab : null,
                station != null ? station.GetLevel(true) : 0,
                inventory
            );
        }

        public bool TryUpgrade()
        {
            var player = Player.m_localPlayer;
            if (player == null || !BoomboxPlugin.Instance.CanControl(deck))
            {
                UpgradeMessage = BoomboxText.Get("text.move_closer_to_your_boombox");
                return false;
            }
            var plan = GetUpgradePlan();
            if (!plan.Allowed)
            {
                UpgradeMessage = Describe(plan);
                return false;
            }
            if (!Spend(player, plan.TargetTier.Recipe))
            {
                UpgradeMessage = BoomboxText.Get("text.not_enough_materials");
                return false;
            }
            deck.State.upgradeTier = plan.TargetTier.Level;
            appearance.Refresh();
            BoomboxPlugin.Instance.Save();
            if (Game.instance != null)
                Game.instance.SavePlayerProfile(false, false);
            UpgradeMessage = BoomboxText.Get("text.amplifier_upgraded_to_level") + Tier.Level + ".";
            player.Message(MessageHud.MessageType.Center, (UpgradeMessage));
            return true;
        }

        public static bool Spend(
            Player player,
            UpgradeRecipe recipe
        ) => SpendExcept(player, recipe, new ItemDrop.ItemData[0]);

        public static bool SpendExcept(Player player, UpgradeRecipe recipe, params ItemDrop.ItemData[] excluded)
        {
            var inventory = player.GetInventory();
            var remaining = recipe.Ingredients.ToDictionary(i => i.Prefab, i => i.Amount, StringComparer.Ordinal);
            var removal = new List<KeyValuePair<ItemDrop.ItemData, int>>();
            foreach (var item in inventory.GetAllItems().ToArray())
            {
                if (excluded.Contains(item))
                    continue;
                if (item.m_dropPrefab == null
                    || !remaining.TryGetValue(item.m_dropPrefab.name, out int amount)
                    || amount <= 0)
                    continue;
                int take = Math.Min(item.m_stack, amount);
                removal.Add(new KeyValuePair<ItemDrop.ItemData, int>(item, take));
                remaining[item.m_dropPrefab.name] -= take;
            }
            if (remaining.Values.Any(v => v > 0))
                return false;
            foreach (var item in removal)
                inventory.RemoveItem(item.Key, item.Value);
            return true;
        }

        internal static CraftingStation FindStation(Player player, UpgradeRecipe recipe)
        {
            if (player == null || ZNetScene.instance == null)
                return null;
            var prefab = ZNetScene.instance.GetPrefab(recipe.StationPrefab);
            var template = prefab != null ? prefab.GetComponent<CraftingStation>() : null;
            if (template == null)
                return null;
            var stations = new List<CraftingStation>();
            CraftingStation.FindStationsInRange(template.m_name, player.transform.position, float.MaxValue, stations);
            return stations.Where(s => s != null
                && s.InUseDistance(player)
                && s.CheckUsable(player, false)).OrderByDescending(
                s => s.GetLevel(true)
            ).FirstOrDefault();
        }

        public static Dictionary<string, int> CountInventory(Player player)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (player == null)
                return result;
            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (item.m_dropPrefab == null)
                    continue;
                string name = item.m_dropPrefab.name;
                result[name] = (result.TryGetValue(name, out var count) ? count : 0) + item.m_stack;
            }
            return result;
        }

        public static string ItemName(string prefab)
        {
            var item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            var drop = item != null ? item.GetComponent<ItemDrop>() : null;
            return drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }

        public static string StationName(string prefab)
        {
            var root = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            var station = root != null ? root.GetComponent<CraftingStation>() : null;
            return station != null ? Localization.instance.Localize(station.m_name) : prefab;
        }

        public static string Describe(UpgradePlan plan)
        {
            switch (plan.Status)
            {
                case UpgradeStatus.Ready:
                    return BoomboxText.Get("text.all_requirements_met");
                case UpgradeStatus.MaximumTier:
                    return BoomboxText.Get("text.maximum_amplifier_level");
                case UpgradeStatus.WrongStation:
                    return BoomboxText.Get("text.requires_an_accessible") + StationName(plan.TargetTier.Recipe.StationPrefab) + BoomboxText.Get("text.of_level")
                        + plan.TargetTier.Recipe.StationLevel
                        + ".";
                case UpgradeStatus.StationLevelTooLow:
                    return BoomboxText.Get("text.station_level_too_low_required") + plan.TargetTier.Recipe.StationLevel + ".";
                case UpgradeStatus.MissingMaterials:
                    return BoomboxText.Get("text.missing_materials")
                        + string.Join(
                        ", ",
                        plan.MissingIngredients.Select(i => ItemName(i.Prefab) + " × " + i.Amount)
                    )
                        + ".";
                default:
                    return BoomboxText.Get("text.invalid_amplifier_level");
            }
        }

        private void Update()
        {
            if (appearance != null)
                appearance.Refresh();
        }
    }
}
