using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace VikingBoombox
{
    public static class BoomboxCombatDamage
    {
        private sealed class WaveHit
        {
        }
        private static readonly ConditionalWeakTable<HitData, WaveHit> hits =
            new ConditionalWeakTable<HitData, WaveHit>();
        [ThreadStatic] private static Character pendingTarget;

        public static void Apply(Character target, HitData hit)
        {
            var previous = pendingTarget;
            pendingTarget = target;
            try
            {
                target.Damage(hit);
            }
            finally
            {
                pendingTarget = previous;
            }
        }

        internal static void Register(Character target, HitData hit)
        {
            if (target != pendingTarget || hit == null)
                return;
            pendingTarget = null;
            hits.GetValue(hit, _ => new WaveHit());
        }
        internal static bool IsWave(HitData hit) => hit != null && hits.TryGetValue(hit, out _);
    }

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class BoomboxWaveHitReceived
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Character __instance, HitData __1) => BoomboxCombatDamage.Register(__instance, __1);
    }

    [HarmonyPatch(typeof(Character), "AddStaggerDamage")]
    internal static class BoomboxWaveNoStagger
    {
        private static bool Prefix(HitData __2, ref bool __result)
        {
            if (!BoomboxCombatDamage.IsWave(__2))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyPushback", new[] { typeof(HitData) })]
    internal static class BoomboxWaveNoNativePush
    {
        private static bool Prefix(HitData __0) => !BoomboxCombatDamage.IsWave(__0);
    }
}
