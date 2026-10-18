using HarmonyLib;

namespace VikingBoombox
{
    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
    internal static class BoomboxGhostMineRock5
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(MineRock5 __instance, HitData hit) => BoomboxGhostStrikes.Capture(__instance, hit);
    }

    [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
    internal static class BoomboxGhostMineRock
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(MineRock __instance, HitData hit) => BoomboxGhostStrikes.Capture(__instance, hit);
    }

    [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
    internal static class BoomboxGhostTreeBase
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TreeBase __instance, HitData hit) => BoomboxGhostStrikes.Capture(__instance, hit);
    }

    [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]
    internal static class BoomboxGhostTreeLog
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TreeLog __instance, HitData hit) => BoomboxGhostStrikes.Capture(__instance, hit);
    }

    [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
    internal static class BoomboxGhostDestructible
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            Destructible __instance,
            HitData hit
        ) => BoomboxGhostStrikes.Capture(__instance, hit);
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class BoomboxOdinSpearThrow
    {
        private static void Postfix(
            Projectile __instance,
            Character owner,
            ItemDrop.ItemData item
        ) => BoomboxOdinSpears.Thrown(__instance, owner, item);
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class BoomboxOdinSpearHit
    {
        private static void Prefix(Projectile __instance) => BoomboxOdinSpears.Hitting(__instance);

        private static void Finalizer() => BoomboxOdinSpears.Hitting(null);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class BoomboxOdinSpearDamage
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Character __instance, HitData hit) => BoomboxOdinSpears.Damaging(__instance, hit);
    }
}
