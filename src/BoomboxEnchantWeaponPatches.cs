using HarmonyLib;

namespace VikingBoombox
{
    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class BoomboxEnchantMeleeSwing
    {
        private static void Prefix(Attack __instance, Humanoid ___m_character, ItemDrop.ItemData ___m_weapon)
        {
            BoomboxEnchantWeapons.BeginSwing(___m_character, ___m_weapon);
            BoomboxGhostStrikes.Begin(__instance, ___m_character, ___m_weapon);
        }

        private static void Finalizer(Humanoid ___m_character)
        {
            BoomboxEnchantWeapons.EndSwing();
            BoomboxGhostStrikes.End(___m_character as Player);
        }
    }

    [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
    internal static class BoomboxEnchantAreaSwing
    {
        private static void Prefix(Attack __instance, Humanoid ___m_character, ItemDrop.ItemData ___m_weapon)
        {
            BoomboxEnchantWeapons.BeginSwing(___m_character, ___m_weapon);
            BoomboxGhostStrikes.Begin(__instance, ___m_character, ___m_weapon);
        }

        private static void Finalizer(Humanoid ___m_character)
        {
            BoomboxEnchantWeapons.EndSwing();
            BoomboxGhostStrikes.End(___m_character as Player);
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
    internal static class BoomboxEnchantMeleeDamage
    {
        private static void Postfix(ref HitData hitData)
        {
            if (hitData != null)
                BoomboxEnchantWeapons.Landed(hitData);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class BoomboxEnchantStaffCast
    {
        private static void Prefix(
            Attack __instance,
            Humanoid character,
            ItemDrop.ItemData weapon
        ) => BoomboxEnchantWeapons.ArmStaff(__instance, character, weapon);

        private static void Postfix(
            Attack __instance,
            bool __result
        ) => BoomboxEnchantWeapons.StaffStarted(__instance, __result);
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackEitr), new[] { typeof(Character), typeof(ItemDrop.ItemData) })]
    internal static class BoomboxEnchantStaffEitr
    {
        private static void Postfix(Attack __instance, ref float __result)
        {
            if (__result > 0f)
                __result *= BoomboxEnchantWeapons.EitrFactor(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "QueueReloadAction")]
    internal static class BoomboxEnchantCrossbowReload
    {
        private static void Prefix(Player __instance) => BoomboxEnchantWeapons.BeginReload(__instance);

        private static void Finalizer() => BoomboxEnchantWeapons.EndReload();
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeaponLoadingTime))]
    internal static class BoomboxEnchantCrossbowLoading
    {
        private static void Postfix(ref float __result) => __result *= BoomboxEnchantWeapons.ReloadFactor();
    }
}
