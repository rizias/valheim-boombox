using System;
using System.Net;
using System.Net.Sockets;
using HarmonyLib;
using UnityEngine;

public static class QaTransport
{
    public const int Port = 24870;
    public static void Install(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(ZNet), "OpenServer"),
            prefix: new HarmonyMethod(typeof(QaTransport), nameof(OpenServer))
        );
        harmony.Patch(
            AccessTools.Method(typeof(ZSocket2), "BindSocket"),
            prefix: new HarmonyMethod(typeof(QaTransport), nameof(BindSocket))
        );
        harmony.Patch(
            AccessTools.Method(typeof(FejdStartup), "GetOnlineBackend"),
            postfix: new HarmonyMethod(typeof(QaTransport), nameof(Backend))
        );
        harmony.Patch(
            AccessTools.Method(
                typeof(ZNet),
                "SetServerHost",
                new[] { typeof(string), typeof(int), typeof(OnlineBackendType) }
            ),
            prefix: new HarmonyMethod(typeof(QaTransport), nameof(JoinBackend))
        );
        harmony.Patch(
            AccessTools.Method(typeof(ZNet), "RPC_ClientHandshake"),
            postfix: new HarmonyMethod(typeof(QaTransport), nameof(Password))
        );
        harmony.Patch(
            AccessTools.Method(typeof(ZNet), "Update"),
            postfix: new HarmonyMethod(typeof(QaTransport), nameof(TickConnector))
        );
        harmony.Patch(
            AccessTools.Constructor(typeof(ZConnector2), new[] { typeof(string), typeof(int) }),
            prefix: new HarmonyMethod(typeof(QaTransport), nameof(ConnectLoopback))
        );
    }
    private static bool ConnectLoopback(ZConnector2 __instance, string __0, int __1)
    {
        if (__0 != "127.0.0.1")
            throw new InvalidOperationException("QA TCP endpoint must be loopback");
        AccessTools.Field(typeof(ZConnector2), "m_host").SetValue(__instance, __0);
        AccessTools.Field(typeof(ZConnector2), "m_port").SetValue(__instance, __1);
        AccessTools.Field(typeof(ZConnector2), "m_endPoint").SetValue(
            __instance,
            new IPEndPoint(IPAddress.Loopback, __1)
        );
        var socket = (TcpClient)AccessTools.Method(typeof(ZSocket2), "CreateSocket").Invoke(null, null);
        AccessTools.Field(typeof(ZConnector2), "m_socket").SetValue(__instance, socket);
        AccessTools.Field(typeof(ZConnector2), "m_result").SetValue(
            __instance,
            socket.BeginConnect(IPAddress.Loopback, __1, null, null)
        );
        return false;
    }
    private static void TickConnector(ZNet __instance)
    {
        if (!__instance.IsServer())
            AccessTools.Method(typeof(ZNet), "UpdateClientConnector").Invoke(
                __instance,
                new object[] { Time.deltaTime }
            );
    }
    private static void Backend(ref OnlineBackendType __result) => __result = OnlineBackendType.CustomSocket;
    private static void JoinBackend(string __0, ref OnlineBackendType __2)
    {
        if (__0 != "127.0.0.1")
            throw new InvalidOperationException("QA join host must be loopback");
        __2 = OnlineBackendType.CustomSocket;
    }
    private static bool OpenServer(ZNet __instance)
    {
        AccessTools.Field(typeof(ZNet), "m_onlineBackend").SetValue(__instance, OnlineBackendType.CustomSocket);
        ZSocket2.SetDataPort(Port);
        var socket = new ZSocket2();
        if (!socket.StartHost())
            throw new InvalidOperationException("QA native TCP listener did not bind");
        AccessTools.Field(typeof(ZNet), "m_hostSocket").SetValue(__instance, socket);
        return false;
    }
    private static bool BindSocket(ZSocket2 __instance, int __0, int __1, ref bool __result)
    {
        for (int port = __0; port <= __1; port++)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                AccessTools.Field(typeof(ZSocket2), "m_listner").SetValue(__instance, listener);
                AccessTools.Field(typeof(ZSocket2), "m_listenPort").SetValue(__instance, port);
                __result = true;
                return false;
            }
            catch (SocketException)
            {
                listener.Stop();
            }
        }
        __result = false;
        return false;
    }
    private static void Password(ZNet __instance, ZRpc __0, bool __1)
    {
        if (__1 && __instance.InPasswordDialog())
            AccessTools.Method(typeof(ZNet), "OnPasswordEntered").Invoke(
                __instance,
                new object[] { "qa-local-password" }
            );
    }
}
