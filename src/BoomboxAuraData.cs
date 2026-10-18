using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxAuraSource
    {
        public string Id;
        public long Owner;
        public string OwnerName;
        public long Peer;
        public ZDOID WorldItem;
        public bool Worn, Active;
        public Vector3 Position;
        public readonly int[] Ranks = new int[5];
        public readonly List<BoomboxAuraDecoration> Decorations = new List<BoomboxAuraDecoration>();
    }

    public sealed class BoomboxAuraDecoration
    {
        public int Slot;
        public string Prefab;
        public int Effect;
    }

    public sealed class BoomboxAuraBalance
    {
        public float Radius = 20f;
        public bool MusicOnly;
        public readonly float[] Strength = { 1f, 1f, 1f, 1f, 1f };
        public readonly float[] Cassette = { .05f, 1f, 1f, 1f, .05f };
        public readonly float[] Decoration = { .0025f, .1f, .1f, .1f, .0025f };
        public readonly float[] Initial = { 1f, 1f, 1f };
        public readonly float[] PerRank = { 1f, 1f, 1f };
        public readonly float[] Period = { 5f, 5f, 5f };
        public readonly float[] WaveRadius = new float[BoomboxProgression.MaxTier + 1];
        public string Fallback = "Health";
    }

    public sealed class BoomboxAuraContribution
    {
        public string Id, OwnerName;
        public long Owner;
        public readonly float[] Values = new float[5];
    }

    public sealed class BoomboxAuraAggregate
    {
        public readonly float[] Values = new float[5];
        public readonly List<BoomboxAuraContribution> Contributions = new List<BoomboxAuraContribution>();
    }

    public static class BoomboxAuraMath
    {
        public const int BonusCount = (int)BoomboxDecorationBonus.Speed + 1;
        public const int RecoveryCount = (int)BoomboxDecorationBonus.Eitr - (int)BoomboxDecorationBonus.Health + 1;

        public static string CassetteProperty(BoomboxDecorationBonus bonus)
        {
            switch (bonus)
            {
                case BoomboxDecorationBonus.Damage:
                    return "Red";
                case BoomboxDecorationBonus.Health:
                    return "Green";
                case BoomboxDecorationBonus.Stamina:
                    return "Yellow";
                case BoomboxDecorationBonus.Eitr:
                    return "Blue";
                default:
                    return "Purple";
            }
        }

        public static int RecoveryIndex(BoomboxDecorationBonus bonus) => (int)bonus - (int)BoomboxDecorationBonus.Health;

        public static BoomboxDecorationBonus RecoveryBonus(int index) => BoomboxDecorationBonus.Health + index;

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static float CassetteAmount(int index, int rank, BoomboxAuraBalance balance)
        {
            var bonus = (BoomboxDecorationBonus)index;
            if (bonus == BoomboxDecorationBonus.Damage || bonus == BoomboxDecorationBonus.Speed)
                return rank * balance.Cassette[index];
            if (rank <= 0)
                return 0f;
            int recovery = RecoveryIndex(bonus);
            float amount = balance.Initial[recovery] + (rank - 1) * balance.PerRank[recovery];
            return Math.Max(0f, amount) * balance.Cassette[index];
        }

        public static float DecorationAmount(
            int index,
            int count,
            BoomboxAuraBalance balance
        ) => count * balance.Decoration[index];

        public static BoomboxAuraContribution Contribution(BoomboxAuraSource source, BoomboxAuraBalance balance)
        {
            var result = new BoomboxAuraContribution
            {
                Id = source.Id,
                Owner = source.Owner,
                OwnerName = source.OwnerName
            };
            var counts = new int[5];
            foreach (var decoration in source.Decorations)
                counts[decoration.Effect]++;
            for (int index = 0; index < 5; index++)
            {
                float cassette = CassetteAmount(index, source.Ranks[index], balance);
                result.Values[index] = (cassette + DecorationAmount(index, counts[index], balance))
                    * balance.Strength[index];
            }
            return result;
        }

        public static BoomboxAuraAggregate Aggregate(
            IEnumerable<BoomboxAuraSource> sources,
            Vector3 recipient,
            BoomboxAuraBalance balance
        )
        {
            var result = new BoomboxAuraAggregate();
            if (balance == null || balance.MusicOnly)
                return result;
            var included = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                if (!source.Active
                    || Vector3.Distance(source.Position, recipient) > balance.Radius
                    || !included.Add(source.Id))
                    continue;
                var contribution = Contribution(source, balance);
                result.Contributions.Add(contribution);
                for (int index = 0; index < 5; index++)
                    result.Values[index] += contribution.Values[index];
            }
            return result;
        }
    }
}
