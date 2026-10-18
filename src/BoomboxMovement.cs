using HarmonyLib;

namespace VikingBoombox
{
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class BoomboxMovement
    {
        public static void Clear() => BoomboxAuraRecipient.Clear();
        public static float Bonus(Player player) => player == Player.m_localPlayer
            ? BoomboxAuraNetwork.For(player).Values[4]
            : 0f;
        private static void Postfix(Player __instance) => BoomboxAuraRecipient.Apply(__instance);
    }
}
