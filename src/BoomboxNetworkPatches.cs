using HarmonyLib;

namespace VikingBoombox
{
    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class BoomboxNetworkStartup
    {
        private static void Postfix(ZNet __instance)
        {
            BoomboxNetworkHandshake.Attach(__instance);
            BoomboxAuraNetwork.Attach(__instance);
            BoomboxRemoteNetwork.Attach(__instance);
            BoomboxMusicNetwork.Attach(__instance);
        }
    }
    [HarmonyPatch(typeof(ZNet), "ClientConnect")]
    internal static class BoomboxNetworkAttempt
    {
        private static void Prefix()
        {
            BoomboxNetworkProtocol.LastReason = null;
        }
    }
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class BoomboxNetworkConnect
    {
        private static void Postfix(ZNet __instance, ZNetPeer __0)
        {
            BoomboxNetworkHandshake.Attach(__instance).Connected(__0);
            BoomboxAuraNetwork.Attach(__instance).Connected(__0);
            BoomboxRemoteNetwork.Attach(__instance).Connected(__0);
            BoomboxMusicNetwork.Attach(__instance).Connected(__0);
        }
    }
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class BoomboxNetworkPeerInfo
    {
        private static bool Prefix(
            ZNet __instance,
            ZRpc __0,
            ZPackage __1
        ) => BoomboxNetworkHandshake.Attach(__instance).PeerInfo(__0, __1);
    }
    [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
    internal static class BoomboxNetworkError
    {
        private static void Postfix(FejdStartup __instance, ZNet.ConnectionStatus __0)
        {
            string reason = BoomboxNetworkProtocol.LastReason;
            if (string.IsNullOrEmpty(reason)
                || (__0 != ZNet.ConnectionStatus.ErrorKicked && ZNet.GetConnectionStatus()
                    != ZNet.ConnectionStatus.ErrorKicked))
                return;
            var label = (TMPro.TMP_Text)AccessTools.Field(typeof(FejdStartup), "m_connectionFailedError")
                .GetValue(__instance);
            if (label != null)
                label.text = reason;
        }
    }
}
