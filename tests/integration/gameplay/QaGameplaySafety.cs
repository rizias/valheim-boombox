using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private Harmony safetyHarmony;
    private static int playerWaveCalls;
    private static readonly Dictionary<ZDOID, int> npcWaveCalls = new Dictionary<ZDOID, int>();
    private JObject safetyBefore;

    private void InstallSafety()
    {
        playerWaveCalls = 0;
        safetyHarmony = new Harmony("local.boombox.qa.safety.observation");
        safetyHarmony.Patch(
            AccessTools.Method(typeof(Character), "RPC_Damage"),
            prefix: new HarmonyMethod(typeof(QaGameplay), nameof(ObserveDamage)) { priority = Priority.Last }
        );
    }
    private static void ObserveDamage(Character __instance, HitData __1)
    {
        if (__instance.IsPlayer()
        && (bool)AccessTools.Method(typeof(BoomboxCombatDamage), "IsWave").Invoke(null, new object[] { __1 }))
            playerWaveCalls++;
        if (!__instance.IsPlayer()
        && (bool)AccessTools.Method(typeof(BoomboxCombatDamage), "IsWave").Invoke(null, new object[] { __1 }))
        {
            var id = __instance.GetZDOID();
            npcWaveCalls[id] = npcWaveCalls.TryGetValue(id, out var count) ? count + 1 : 1;
        }
    }
    private JObject SafetySnapshot()
    {
        var player = Player.m_localPlayer;
        return new JObject
        {
            ["before"] = safetyBefore,
            ["playerWaveCalls"] = playerWaveCalls,
            ["pvp"] = player != null && player.IsPVPEnabled(),
            ["velocity"] = player != null ? Point(player.GetVelocity()) : null,
            ["healthTicks"] = BoomboxAuraRecipient.HealthTicks
        };
    }
    private bool SafetyOperation(string operation, JObject request)
    {
        if (operation == "safety-player")
        {
            var player = Player.m_localPlayer;
            player.SetPVP((bool)request["pvp"]);
            player.SetGodMode(false);
            player.SetHealth(25f);
            Freeze(player, false);
            player.SetMoveDir(Vector3.zero);
            player.SetRun(false);
            safetyBefore = new JObject
            {
                ["health"] = player.GetHealth(),
                ["velocity"] = Point(player.GetVelocity()),
                ["healthTicks"] = BoomboxAuraRecipient.HealthTicks,
                ["playerWaveCalls"] = playerWaveCalls
            };
            return true;
        }
        if (operation == "safety-tame")
        {
            var character = ZNetScene.instance.FindInstance(npcId)?.GetComponent<Character>();
            if (character == null || !character.IsOwner())
                throw new InvalidOperationException("Native taming fixture requires current NPC owner");
            character.SetTamed((bool)request["tamed"]);
            return true;
        }
        if (operation == "safety-push")
        {
            BoomboxPlugin.Instance.Settings.Push[1].Value = 5f;
            return true;
        }
        if (operation != "combat-pulse")
            return false;
        var deck = decks[(string)request["key"]];
        var settings = BoomboxPlugin.Instance.Settings;
        settings.Effect.Variant.Value = BoomboxMusicEffect.Pulse;
        settings.Effect.CreatureDamage.Value = true;
        var moment = new MusicMoment
        {
            Time = deck.Moment.Time,
            Level = 1f,
            Silent = false,
            Smooth = new MusicRanges { Whole = 1, Low = 1, Mid = 1, High = 1 }
        };
        try
        {
            deck.Effect.Step(moment, true);
            moment.Time += .01;
            deck.Effect.Step(moment, true);
            moment.Time += .01;
            deck.Effect.Step(moment, true);
        }
        finally
        {
            settings.Effect.CreatureDamage.Value = false;
        }
        return true;
    }
}
