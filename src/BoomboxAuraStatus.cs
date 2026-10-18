using System.Globalization;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxAuraStatus : SE_Stats
    {
        public int BonusIndex;
        public float BonusValue;
        public float BonusPeriod;
        public int BonusSources;
        public override string GetTooltipString() => m_tooltip;
    }

    internal static class BoomboxAuraHud
    {
        private static readonly BoomboxAuraStatus[] templates = new BoomboxAuraStatus[5];
        private static readonly string[] colors = { "Red", "Green", "Yellow", "Blue", "Purple" };

        internal static string Name(int index)
        {
            string[] keys = { "text.wave_damage", "text.health", "text.stamina", "text.eitr", "text.speed" };
            return BoomboxText.Get(keys[index]);
        }

        internal static string Number(float value, int index)
        => (value * (index == 0 || index == 4 ? 100f : 1f)).ToString(
            "0.##",
            BoomboxText.Culture
        ) + (index == 0 || index == 4
                ? " %"
                : "");

        internal static void Present(Player player, BoomboxAuraAggregate aggregate, BoomboxAuraBalance balance)
        {
            var manager = player.GetSEMan();
            if (manager == null)
                return;
            for (int index = 0; index < 5; index++)
            {
                int hash = Key(index).GetStableHashCode();
                if (aggregate.Values[index] <= 0f)
                {
                    manager.RemoveStatusEffect(hash, true);
                    continue;
                }
                var effect = manager.GetStatusEffect(hash) as BoomboxAuraStatus;
                if (effect == null)
                {
                    if (templates[index] == null)
                    {
                        templates[index] = ScriptableObject.CreateInstance<BoomboxAuraStatus>();
                        templates[index].name = Key(index);
                    }
                    var item = ObjectDB.instance?.GetItemPrefab(BoomboxItemData.CassettePrefix + colors[index]);
                    templates[index].m_icon = VikingBoomboxVisual.CanRender
                        ? BoomboxBonusLook.Icon(index)
                        : item?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
                    effect = manager.AddStatusEffect(templates[index], false, 0, 0, 0) as BoomboxAuraStatus;
                }
                if (effect == null)
                    continue;
                effect.BonusIndex = index;
                effect.BonusValue = aggregate.Values[index];
                effect.BonusSources = aggregate.Contributions.Count(c => c.Values[index] > 0);
                effect.BonusPeriod = index > 0 && index < 4 ? balance.Period[index - 1] : 0f;
                effect.m_name = Name(index);
                string unit = index > 0 && index < 4
                    ? BoomboxText.Get("text.every_2") + balance.Period[index - 1].ToString("0.##", BoomboxText.Culture)
                        + BoomboxText.Get("text.s")
                    : "";
                effect.m_tooltip = Name(index) + ": +" + Number(aggregate.Values[index], index) + unit;
                effect.m_tooltip += BoomboxText.Get("text.source_boomboxes") + effect.BonusSources;
                foreach (var owner in aggregate.Contributions.Where(c => c.Values[index] > 0).GroupBy(c => c.Owner))
                    effect.m_tooltip += "\n" + Clean(owner.First().OwnerName) + ": +"
                        + Number(owner.Sum(c => c.Values[index]), index);
                effect.m_speedModifier = index == 4 ? aggregate.Values[index] : 0f;
            }
        }

        internal static void Remove(Player player)
        {
            if (player != null)
                for (int index = 0; index < 5; index++)
                    player.GetSEMan()?.RemoveStatusEffect(Key(index).GetStableHashCode(), true);
        }

        internal static void Clear(Player player)
        {
            Remove(player);
            for (int index = 0; index < 5; index++)
            {
                if (templates[index] != null)
                    Object.Destroy(templates[index]);
                templates[index] = null;
            }
        }

        private static string Key(int index) => "ValheimBoombox_Shared_" + colors[index];
        private static string Clean(string text) => string.IsNullOrWhiteSpace(text)
            ? BoomboxText.Get("text.player")
            : text.Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " ");
    }
}
