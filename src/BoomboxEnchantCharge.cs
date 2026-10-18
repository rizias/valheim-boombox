using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxEnchantCharge
    {
        internal const float PerBeat = .25f;
        internal const float ArrowDamagePerCharge = .20f;
        internal const float BlockStaminaPerCharge = .30f;
        internal const float ShieldDecayPerSecond = .5f;
        private static Attack armed;
        private static float armedCharge;

        internal static float Bow
        {
            get;
            private set;
        }

        internal static float Shield
        {
            get;
            private set;
        }

        internal static float Shot
        {
            get;
            private set;
        }

        internal static bool LocalShieldBlock
        {
            get;
            set;
        }

        internal static float ArrowDamageFactor => 1f + ArrowDamagePerCharge * Shot;

        internal static float BlockStaminaFactor => 1f - BlockStaminaPerCharge * Shield;

        internal static bool HoldsShield(Humanoid humanoid) => Holds(humanoid, ItemDrop.ItemData.ItemType.Shield);

        internal static bool HoldsBow(Humanoid humanoid) => Holds(humanoid, ItemDrop.ItemData.ItemType.Bow);

        internal static void Beat(bool drawing, bool blocking)
        {
            if (drawing)
                Bow = Mathf.Min(1f, Bow + PerBeat);
            if (blocking)
                Shield = Mathf.Min(1f, Shield + PerBeat);
        }

        internal static void Step(bool drawing, bool blocking, float deltaTime)
        {
            if (!drawing)
                Bow = 0f;
            if (!blocking)
                Shield = Mathf.Max(0f, Shield - ShieldDecayPerSecond * deltaTime);
        }

        internal static void Reset()
        {
            Bow = 0f;
            Shield = 0f;
            armedCharge = 0f;
        }

        internal static void Arm(Attack attack)
        {
            armed = attack;
            armedCharge = Bow;
            Bow = 0f;
        }

        internal static void BeginBurst(Attack attack) => Shot = attack == armed ? armedCharge : 0f;

        internal static void EndBurst() => Shot = 0f;

        private static bool Holds(
            Humanoid humanoid,
            ItemDrop.ItemData.ItemType type
        ) => humanoid != null && humanoid.LeftItem != null && humanoid.LeftItem.m_shared.m_itemType == type;
    }
}
