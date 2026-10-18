using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private float? auraConfigRadius;
    private float[] auraConfigStrength;
    private string auraLanguage;
    private readonly HashSet<int> auraHudKnownIcons = new HashSet<int>();
    private bool AuraConfigOperation(string operation, JObject request)
    {
        if (!operation.StartsWith("aura-config", StringComparison.Ordinal) && operation != "aura-hud-language")
            return false;
        var plugin = BoomboxPlugin.Instance;
        if (operation == "aura-config-local")
        {
            busy = true;
            StartCoroutine(AuraConfigLocal());
            return true;
        }
        if (operation == "aura-hud-language")
        {
            if (auraLanguage == null)
                auraLanguage = BoomboxText.Language;
            BoomboxText.LoadTranslations((string)request["language"]);
            return true;
        }
        if (operation == "aura-config-read")
        {
            sample = AuraConfigSnapshot();
            return true;
        }
        if (operation == "aura-config-restore")
        {
            if (auraConfigRadius.HasValue)
            {
                plugin.Settings.Party.Radius.Value = auraConfigRadius.Value;
                for (int i = 0; i < 5; i++)
                    plugin.Settings.Party.Strength[i].Value = auraConfigStrength[i];
                plugin.Config.Save();
            }
            BoomboxText.LoadTranslations(auraLanguage);
            return true;
        }
        if (operation != "aura-config-set")
            throw new InvalidOperationException("Unknown aura config fixture operation");
        if (!auraConfigRadius.HasValue)
        {
            auraConfigRadius = plugin.Settings.Party.Radius.Value;
            auraConfigStrength = plugin.Settings.Party.Strength.Select(s => s.Value).ToArray();
        }
        plugin.Settings.Party.Radius.Value = (float)request["radius"];
        var strength = (JArray)request["strength"];
        for (int i = 0; i < 5; i++)
            plugin.Settings.Party.Strength[i].Value = (float)strength[i];
        plugin.Config.Save();
        return true;
    }
    private IEnumerator AuraConfigLocal()
    {
        Game.instance.Logout(true, true);
        FejdStartup menu = null;
        float deadline = Time.realtimeSinceStartup + 120;
        while ((menu = UnityEngine.Object.FindFirstObjectByType<FejdStartup>()) == null
            && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (menu == null)
        {
            error = "Native local-world menu not ready";
            busy = false;
            yield break;
        }
        sample = AuraConfigSnapshot();
        sample["temporaryOverride"] = BoomboxAuraNetwork.Current != null
        && AccessTools.Field(typeof(BoomboxAuraNetwork), "balance").GetValue(BoomboxAuraNetwork.Current) != null
        && !BoomboxAuraNetwork.CanEditBalance;
        Game.SetProfile(role.Replace('-', '_'), FileHelpers.FileSource.Local);
        SaveWithBackups save;
        bool loaded = SaveSystem.TryGetSaveByName("BoomboxQA", SaveDataType.World, out save) && !save.IsDeleted;
        var world = loaded ? World.LoadWorld(save) : new World("BoomboxQA", "boombox-qa-20261004");
        world.m_fileSource = FileHelpers.FileSource.Local;
        if (!loaded)
        {
            SaveSystem.SetSaveNumber(0);
            world.SaveWorldFWLData(DateTime.Now);
        }
        ZNet.ResetServerHost();
        ZNet.SetServer(true, false, false, "Boombox QA local", "", world);
        AccessTools.Field(typeof(FejdStartup), "m_startingWorld").SetValue(menu, true);
        AccessTools.Method(typeof(FejdStartup), "TransitionToMainScene").Invoke(menu, null);
        while ((Player.m_localPlayer == null || !BoomboxPlugin.Instance.AudioWorldActive)
            && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (Player.m_localPlayer == null || !BoomboxPlugin.Instance.AudioWorldActive || ZNet.instance == null
        || !ZNet.instance.IsServer())
            error = "Native local-world player/server not ready";
        busy = false;
    }
    private JObject AuraConfigSnapshot()
    {
        var settings = BoomboxPlugin.Instance.Settings;
        var balance = BoomboxAuraNetwork.Balance;
        var player = Player.m_localPlayer;
        var aura = player?.GetSEMan().GetStatusEffects().OfType<BoomboxAuraStatus>().ToArray()
            ?? Array.Empty<BoomboxAuraStatus>();
        return new JObject
        {
            ["localRadius"] = settings.Party.Radius.Value,
            ["localStrength"] = new JArray(settings.Party.Strength.Select(s => s.Value)),
            ["hostRadius"] = balance?.Radius,
            ["hostStrength"] = balance == null ? null : new JArray(balance.Strength),
            ["receivedRevision"] = BoomboxAuraNetwork.Current == null
                ? null
                : JToken.FromObject(
                AccessTools.Field(typeof(BoomboxAuraNetwork), "receivedRevision").GetValue(
                    BoomboxAuraNetwork.Current
                )
            ),
            ["language"] = BoomboxText.Language,
            ["effects"] = new JArray(
                aura.Select(
                    s => new JObject
                    {
                        ["name"] = s.name,
                        ["tooltip"] = s.GetTooltipString(),
                        ["speed"] = s.m_speedModifier,
                        ["icon"] = s.m_icon?.name
                    }
                )
            ),
            ["hudRows"] = AuraHudRows()
        };
    }
}
