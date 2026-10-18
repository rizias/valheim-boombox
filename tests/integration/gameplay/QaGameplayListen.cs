using System;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private bool ListenOperation(string operation, JObject request)
    {
        if (operation == "listen-panel")
        {
            var plugin = BoomboxPlugin.Instance;
            if (Player.m_localPlayer == null || !plugin.AudioWorldActive || Hud.instance == null)
                throw new InvalidOperationException("Real listen player/HUD not ready");
            Create("PANEL", new[] { 0, 0, 0, 0, 0 }, 0, false, Player.m_localPlayer.transform.position);
            plugin.Open(decks["PANEL"]);
            var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
            AccessTools.Field(menu.GetType(), "tab").SetValue(menu, 6);
            return true;
        }
        if (operation == "listen-panel-close")
        {
            BoomboxPlugin.Instance.CloseMenu();
            Clear();
            return true;
        }
        if (operation == "listen-snapshot")
            return true;
        return false;
    }
    private JObject ListenSnapshot()
    {
        if (Environment.GetEnvironmentVariable("BOOMBOX_QA_LISTEN_SESSION") != "1")
            return null;
        var plugin = BoomboxPlugin.Instance;
        if (plugin?.Settings == null || ZNet.instance == null)
            return null;
        var balance = BoomboxAuraNetwork.Balance;
        var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
        var hub = menu != null ? AccessTools.Field(menu.GetType(), "bonusPanel").GetValue(menu) : null;
        var panel = hub != null ? AccessTools.Field(hub.GetType(), "party").GetValue(hub) : null;
        var inputs = panel != null
            ? (TMP_InputField[])AccessTools.Field(panel.GetType(), "inputs").GetValue(panel)
            : Array.Empty<TMP_InputField>();
        var status = panel != null ? (TMP_Text)AccessTools.Field(panel.GetType(), "status").GetValue(panel) : null;
        return new JObject
        {
            ["isServer"] = ZNet.instance.IsServer(),
            ["hud"] = Hud.instance != null,
            ["canEdit"] = BoomboxAuraNetwork.CanEditBalance,
            ["balanceReceived"] = balance != null,
            ["radius"] = balance?.Radius,
            ["strength"] = balance != null ? new JArray(balance.Strength) : new JArray(),
            ["panelStatus"] = status?.text,
            ["inputs"] = new JArray(
                inputs.Select(
                    i => new JObject
                    {
                        ["interactable"] = i.interactable,
                        ["value"] = i.text,
                        ["focused"] = i.isFocused
                    }
                )
            ),
            ["scope"] =
                "Actual instantiated PartyPanel state; programmatic presentation only, no real user input/onEndEdit/config GUI acceptance"
        };
    }
}
