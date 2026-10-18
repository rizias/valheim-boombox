using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator GameplaySettingsCapture()
    {
        yield return AwaitAudio();
        var plugin = BoomboxPlugin.Instance;
        var settings = plugin.Settings;
        var player = Player.m_localPlayer;
        bool[] saved = {
            settings.PersonalMusicOnly.Value,
            settings.MusicOnly.Value,
            settings.Effect.CreatureDamage.Value,
            settings.MusicEnchantment.Value,
            settings.BoatHelp.Value,
            settings.PushEnvironment.Value
        };
        var frames = new JArray();
        var cases = new JArray();
        var proof = new JObject { ["frames"] = frames, ["cases"] = cases, ["candidate"] = productSha };
        lastCapture = proof;
        uiClicks = new JArray();
        proof["inputRoutes"] = uiClicks;
        float savedPeriod = settings.Health.Interval.Value;
        var properties = new System.Collections.Generic.Dictionary<string, int>(
            audioDeck.State.insertedCassette.properties
        );
        bool day = EnvMan.instance.m_debugTimeOfDay;
        float hour = EnvMan.instance.m_debugTime;
        string language = Localization.instance.GetSelectedLanguage();
        float started = Time.realtimeSinceStartup;
        var errors = new JArray();
        proof["nativeErrors"] = errors;
        Application.LogCallback log = (condition, stack, type) =>
        {
            if ((type == LogType.Exception || type == LogType.Error) && errors.Count < 12)
                errors.Add((condition + "\n" + stack).Substring(0, Math.Min(1800, (condition + "\n" + stack).Length)));
        };
        Application.logMessageReceived += log;
        try
        {
            UiParkWindow(true);
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            Localization.instance.SetLanguage("Russian");
            settings.PersonalMusicOnly.Value = settings.MusicOnly.Value = false;
            settings.Effect.CreatureDamage.Value =
                settings.MusicEnchantment.Value =
                settings.BoatHelp.Value =
                settings.PushEnvironment.Value =
                true;
            foreach (string color in new[] { "Red", "Green", "Yellow", "Blue", "Purple" })
                audioDeck.State.insertedCassette.properties[color] = 1;
            AccessTools.Method(typeof(BoomboxAuraNetwork), "PublishImmediate").Invoke(null, null);
            player.AddKnownItem(audioDeck.Item);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.3f);
            UiDispatch(UiButton("OpenBonuses"), false);
            yield return new WaitForSecondsRealtime(.2f);
            UiDispatch(UiButton("BonusPage1"), false);
            yield return new WaitForSecondsRealtime(.25f);
            Need(
                settings.LocalGameplayEnabled
                    && BoomboxAuraNetwork.Sources.Any(s => s.Id == audioDeck.State.id
                    && s.Active),
                "Fixture gameplay must initially be active"
            );
            foreach (string button in new[] { "CreatureDamage", "MusicEnchantment", "BoatHelp", "PushEnvironment" })
            {
                UiDispatch(UiButton(button), false);
                yield return null;
                UiDispatch(UiButton(button), false);
                yield return null;
            }
            var weapons = typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxEnchantWeapons");
            AccessTools.Method(weapons, "Beat").Invoke(null, new object[] { player.GetCurrentWeapon() });
            var charge = AccessTools.Field(weapons, "Melee").GetValue(null);
            float before = (float)AccessTools.Property(charge.GetType(), "Value").GetValue(charge);
            Need(before > 0, "Charge precondition missing");
            int begun = BoomboxMusicNetwork.Current.BegunEvents;
            bool visualBefore = settings.ShowVisualEffects.Value;
            UiDispatch(UiButton("PersonalMusicOnly"), false);
            yield return new WaitForSecondsRealtime(.4f);
            Need(settings.PersonalMusicOnly.Value && !settings.LocalGameplayEnabled, "Personal mode not enabled");
            Need(
                !settings.MusicOnly.Value && !BoomboxAuraNetwork.Balance.MusicOnly,
                "Personal switch changed host/world policy"
            );
            Need(
                BoomboxAuraNetwork.Sources.Where(s => s.Id == audioDeck.State.id).All(s => !s.Active),
                "Personal source still grants gameplay"
            );
            Need(BoomboxAuraNetwork.For(player).Values.All(v => v == 0), "Own buffs remained in single-source fixture");
            Need(
                (float)AccessTools.Property(charge.GetType(), "Value").GetValue(charge) == 0,
                "Weapon charge was not cleared"
            );
            Need(
                audioDeck.State.playing
                    && audioDeck.Source.isPlaying
                    && visualBefore == settings.ShowVisualEffects.Value,
                "Music/visual preference was changed by personal mode"
            );
            Need(BoomboxMusicNetwork.Current.BegunEvents == begun, "New damage events began in music-only mode");
            cases.Add(new JObject
            {
                ["id"] = "personal-music-only",
                ["musicPlaying"] = true,
                ["hostPolicyUnchanged"] = true,
                ["ownBonusesZero"] = true,
                ["chargeBefore"] = before,
                ["chargeAfter"] = 0
            });
            yield return HudFrame("gameplay-personal-mode.png", frames);
            UiDispatch(UiButton("PersonalMusicOnly"), false);
            yield return new WaitForSecondsRealtime(.4f);
            Need(
                settings.LocalGameplayEnabled && settings.Effect.CreatureDamage.Value && settings.MusicEnchantment.Value
                && settings.BoatHelp.Value
                && settings.PushEnvironment.Value,
                "Saved individual choices were lost"
            );
            yield return HudFrame("gameplay-options.png", frames);
            proof["partyClick"] = UiDispatch(UiButton("BonusPage2"), false);
            yield return new WaitForSecondsRealtime(.2f);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Any(
                b => b.name == "WorldMusicOnly" && b.gameObject.activeInHierarchy
            )
            && Time.realtimeSinceStartup < deadline)
                yield return null;
            var menu = AccessTools.Field(typeof(BoomboxPlugin), "menu").GetValue(plugin);
            var hub = AccessTools.Field(menu.GetType(), "bonusPanel").GetValue(menu);
            proof["partyPage"] = (int)AccessTools.Field(hub.GetType(), "page").GetValue(hub);
            proof["activeButtons"] = new JArray(
                UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Where(
                    b => b.gameObject.activeInHierarchy
                ).Select(
                    b => b.name
                )
            );
            if (!UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Any(
                b => b.name == "WorldMusicOnly" && b.gameObject.activeInHierarchy
            ))
                yield return HudFrame("gameplay-party-failed.png", frames);
            UiDispatch(UiButton("WorldMusicOnly"), false);
            yield return new WaitForSecondsRealtime(.4f);
            Need(
                BoomboxAuraNetwork.Balance.MusicOnly
                    && !settings.LocalGameplayEnabled
                    && !settings.PersonalMusicOnly.Value,
                "Host world gate failed"
            );
            Need(audioDeck.Source.isPlaying, "Host mode stopped music");
            UiDispatch(UiButton("WorldMusicOnly"), false);
            yield return new WaitForSecondsRealtime(.4f);
            Need(!BoomboxAuraNetwork.Balance.MusicOnly && settings.LocalGameplayEnabled, "Host mode did not restore");
            cases.Add(new JObject { ["id"] = "host-world-mode", ["passed"] = true });
            float oldPeriod = settings.Health.Interval.Value;
            yield return UiType("RecoveryPeriod0", "7");
            UiDispatch((RectTransform)UiInput("RecoveryPeriod1").transform, false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(BoomboxAuraNetwork.Balance.Period[0] == 7, "Recovery interval not published");
            yield return UiType(
                "RecoveryPeriod0",
                oldPeriod.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            UiDispatch((RectTransform)UiInput("RecoveryPeriod1").transform, false);
            yield return null;
            yield return HudFrame("gameplay-party.png", frames);
            UiDispatch(UiButton("Tab5"), false);
            yield return new WaitForSecondsRealtime(.2f);
            Need(
                !UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Any(
                    b => b.name == "CreatureDamage" && b.gameObject.activeInHierarchy
                ),
                "Damage control still in Effects"
            );
            proof["seconds"] = Time.realtimeSinceStartup - started;
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            settings.Health.Interval.Value = savedPeriod;
            audioDeck.State.insertedCassette.properties = properties;
            settings.PersonalMusicOnly.Value = saved[0];
            settings.MusicOnly.Value = saved[1];
            settings.Effect.CreatureDamage.Value = saved[2];
            settings.MusicEnchantment.Value = saved[3];
            settings.BoatHelp.Value = saved[4];
            settings.PushEnvironment.Value = saved[5];
            AccessTools.Method(typeof(BoomboxAuraNetwork), "PublishImmediate").Invoke(null, null);
            EnvMan.instance.m_debugTimeOfDay = day;
            EnvMan.instance.m_debugTime = hour;
            Localization.instance.SetLanguage(language);
            UiParkWindow();
            Application.logMessageReceived -= log;
        }
    }
}
