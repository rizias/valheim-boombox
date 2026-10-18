using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class BoomboxEnchantBowRelease
    {
        private static void Postfix(Attack __instance, Humanoid character, bool __result)
        {
            if (__result && __instance.m_bowDraw && character != null && character == Player.m_localPlayer)
                BoomboxEnchantCharge.Arm(__instance);
        }
    }

    [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
    internal static class BoomboxEnchantArrowBurst
    {
        private static void Prefix(Attack __instance) => BoomboxEnchantCharge.BeginBurst(__instance);

        private static void Finalizer() => BoomboxEnchantCharge.EndBurst();
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class BoomboxEnchantArrowDamage
    {
        private static void Prefix(Character owner, HitData hitData)
        {
            if (BoomboxEnchantCharge.Shot > 0f && hitData != null && owner != null && owner == Player.m_localPlayer)
                hitData.m_damage.Modify(BoomboxEnchantCharge.ArrowDamageFactor);
        }

        private static void Postfix(Projectile __instance, Character owner, Vector3 velocity)
        {
            if (BoomboxEnchantCharge.Shot > 0f && owner != null && owner == Player.m_localPlayer)
                BoomboxEnchantVisuals.Trail(__instance.gameObject, BoomboxEnchantCharge.Shot, velocity.magnitude);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class BoomboxEnchantBlock
    {
        private static void Prefix(
            Humanoid __instance
        ) => BoomboxEnchantCharge.LocalShieldBlock = __instance == Player.m_localPlayer
            && BoomboxEnchantCharge.HoldsShield(__instance);

        private static void Finalizer() => BoomboxEnchantCharge.LocalShieldBlock = false;
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyBlockStaminaUsage))]
    internal static class BoomboxEnchantBlockStamina
    {
        private static void Postfix(ref float staminaUse)
        {
            if (BoomboxEnchantCharge.LocalShieldBlock && staminaUse > 0f)
                staminaUse *= BoomboxEnchantCharge.BlockStaminaFactor;
        }
    }
}
