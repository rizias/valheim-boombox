using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static QaGameplay recoveryReloadOwner;
    private Harmony recoveryReloadHarmony;
    private Coroutine recoveryWatch;
    private SE_Stats recoveryReloadControl;
    private Player recoveryReloadPlayer;
    private JObject recoveryCheckpoint, recoveryLoaded;
    private readonly JArray recoveryReloadEvents = new JArray();
    private float recoveryActiveAt = -1;
    private static string RecoveryCheckpointPath => Path.Combine(BepInEx.Paths.ConfigPath, "qa-H08-checkpoint.json");
    private void InstallRecoveryReloadObserver()
    {
        if (Environment.GetEnvironmentVariable("BOOMBOX_QA_CASE") != "local-case-H08load")
            return;
        recoveryCheckpoint = JObject.Parse(File.ReadAllText(RecoveryCheckpointPath));
        recoveryReloadOwner = this;
        var settings = BoomboxPlugin.Instance.Settings;
        settings.Health.Interval.Value = settings.Stamina.Interval.Value = settings.Eitr.Interval.Value = 5;
        recoveryReloadHarmony = new Harmony("local.boombox.qa.recovery-reload");
        recoveryReloadHarmony.Patch(
            AccessTools.Method(typeof(Player), "Load"),
            postfix: new HarmonyMethod(typeof(QaGameplay), nameof(RecoveryLoaded))
        );
    }
    private void StopRecoveryReloadObserver()
    {
        recoveryReloadHarmony?.UnpatchSelf();
        if (recoveryWatch != null)
        {
            StopCoroutine(recoveryWatch);
            recoveryWatch = null;
        }
        if (recoveryReloadControl != null)
        {
            if (recoveryReloadPlayer != null)
                recoveryReloadPlayer.GetSEMan().RemoveStatusEffect(recoveryReloadControl);
            controls.Remove(recoveryReloadControl);
            Destroy(recoveryReloadControl);
            recoveryReloadControl = null;
        }
        if (recoveryReloadOwner == this)
            recoveryReloadOwner = null;
    }
    private static void RecoverySaved(Player __instance)
    {
        var owner = recoveryReloadOwner;
        if (owner == null || owner.recoveryCheckpoint == null)
            return;
        var item = __instance.GetInventory().GetAllItems().FirstOrDefault(
            i => BoomboxItemData.IsDevice(i)
                && BoomboxItemData.Read(i).deck.id == (string)owner.recoveryCheckpoint["source"]
        );
        if (item == null)
            return;
        owner.recoveryCheckpoint["savedUtc"] = DateTime.UtcNow.ToString("O");
        owner.recoveryCheckpoint["savedHealth"] = __instance.GetHealth();
        owner.recoveryCheckpoint["savedState"] = CaseStable(item);
        File.WriteAllText(RecoveryCheckpointPath, owner.recoveryCheckpoint.ToString());
        owner.recoveryReloadHarmony.UnpatchSelf();
    }
    private static void RecoveryLoaded(Player __instance)
    {
        var owner = recoveryReloadOwner;
        if (owner == null || Game.instance == null || ZNet.instance == null || ZNet.instance.GetWorld() == null)
            return;
        var view = __instance.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
            return;
        var item = __instance.GetInventory().GetAllItems().FirstOrDefault(
            i => BoomboxItemData.IsDevice(i)
                && BoomboxItemData.Read(i).deck.id == (string)owner.recoveryCheckpoint["source"]
        );
        if (item == null)
            return;
        if (owner.recoveryWatch != null)
            owner.StopCoroutine(owner.recoveryWatch);
        owner.recoveryReloadEvents.Clear();
        owner.recoveryActiveAt = -1;
        owner.recoveryLoaded = new JObject
        {
            ["healthAtNativeLoad"] = __instance.GetHealth(),
            ["state"] = CaseStable(item),
            ["time"] = Time.realtimeSinceStartup
        };
        var control = ScriptableObject.CreateInstance<SE_Stats>();
        control.name = "BoomboxQa_ReloadRegenControl";
        control.m_hidden = true;
        control.m_healthRegenMultiplier = control.m_staminaRegenMultiplier = control.m_eitrRegenMultiplier = 0;
        __instance.GetSEMan().AddStatusEffect(control, false, 0, 0, 0);
        owner.controls.Add(control);
        __instance.SetGodMode(true);
        owner.recoveryReloadControl = control;
        owner.recoveryReloadPlayer = __instance;
        __instance.UseStamina(20);
        __instance.UseEitr(20);
        owner.recoveryLoaded["probeResources"] = new JArray(CaseResources(__instance));
        owner.recoveryWatch = owner.StartCoroutine(owner.ObserveRecoveryReload(__instance));
        owner.recoveryReloadHarmony.UnpatchSelf();
    }
    private IEnumerator ObserveRecoveryReload(Player player)
    {
        var prior = CaseRecoveryTicks();
        float until = Time.realtimeSinceStartup + 25;
        var resources = CaseResources(player);
        while (player != null && Time.realtimeSinceStartup < until)
        {
            var current = CaseRecoveryTicks();
            var now = CaseResources(player);
            if (recoveryActiveAt < 0 && BoomboxAuraNetwork.For(player).Values.Skip(1).Take(3).All(v => v > 0))
                recoveryActiveAt = Time.realtimeSinceStartup;
            if (!current.SequenceEqual(prior))
                recoveryReloadEvents.Add(new JObject
                {
                    ["time"] = Time.realtimeSinceStartup,
                    ["ticks"] = new JArray(current),
                    ["delta"] = new JArray(current.Select((v, i) => v - prior[i])),
                    ["before"] = new JArray(resources),
                    ["after"] = new JArray(now)
                });
            prior = current;
            resources = now;
            yield return null;
        }
    }
    private IEnumerator CaseRecoveryReload(bool save)
    {
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        var settings = plugin.Settings;
        if (save)
        {
            yield return CaseReadyInventory();
            if (Physics.Raycast(
                player.transform.position + Vector3.up * 2,
                Vector3.down,
                out var ground,
                1000,
                LayerMask.GetMask("terrain"),
                QueryTriggerInteraction.Ignore
            ))
                player.transform.position = ground.point + Vector3.up * .1f;
            else
                throw new InvalidOperationException("H08 needs actual saved-world terrain for logout");
            recoveryCheckpoint = new JObject
            {
                ["sha"] = productSha,
                ["periods"] = new JArray(
                    settings.Health.Interval.Value,
                    settings.Stamina.Interval.Value,
                    settings.Eitr.Interval.Value
                )
            };
            try
            {
                settings.Health.Interval.Value = settings.Stamina.Interval.Value = settings.Eitr.Interval.Value = 5;
                Create("H08", new[] { 0, 1, 1, 1, 0 }, 0, false, player.transform.position);
                var deck = decks["H08"];
                recoveryCheckpoint["source"] = deck.State.id;
                player.SetHealth(Mathf.Max(1, player.GetMaxHealth() - 20));
                player.UseStamina(20);
                player.UseEitr(20);
                yield return CaseFreshRecovery();
                recoveryReloadOwner = this;
                recoveryReloadHarmony = new Harmony("local.boombox.qa.recovery-save");
                recoveryReloadHarmony.Patch(
                    AccessTools.Method(typeof(Player), "Save"),
                    postfix: new HarmonyMethod(typeof(QaGameplay), nameof(RecoverySaved))
                );
                plugin.Items.Flush();
                Game.instance.SavePlayerProfile(true);
                Need(File.Exists(RecoveryCheckpointPath), "H08 native save was not observed");
                lastCapture = new JObject
                {
                    ["case"] = "H-08",
                    ["phase"] = "save",
                    ["checkpoint"] = recoveryCheckpoint.DeepClone(),
                    ["passed"] = true
                };
                yield break;
            }
            finally
            {
                var periods = (JArray)recoveryCheckpoint["periods"];
                settings.Health.Interval.Value = (float)periods[0];
                settings.Stamina.Interval.Value = (float)periods[1];
                settings.Eitr.Interval.Value = (float)periods[2];
            }
        }
        var proof =
            new JObject
            {
                ["case"] = "H-08",
                ["phase"] = "load",
                ["scope"] =
                    "Fresh process/native saved character. Health persists natively; stamina/eitr deficits are reintroduced after Load because vanilla does not persist them.",
                ["events"] = recoveryReloadEvents
            };
        lastCapture = proof;
        try
        {
            Need(
                recoveryCheckpoint != null && (string)recoveryCheckpoint["sha"] == productSha,
                "H08 checkpoint candidate mismatch"
            );
            Need(recoveryLoaded != null, "H08 saved source not observed in native Player.Load");
            Freeze(player, true);
            proof["nativeLoad"] = recoveryLoaded;
            var reloadedState = JToken.Parse(recoveryLoaded["state"].ToString(Newtonsoft.Json.Formatting.None));
            Need(
                JToken.DeepEquals(recoveryCheckpoint["savedState"], reloadedState),
                "H08 saved item/cassette payload changed on reload"
            );
            Need(
                (float)recoveryLoaded["healthAtNativeLoad"] <= (float)recoveryCheckpoint["savedHealth"] + .1f,
                "H08 health paid out during native load"
            );
            double absence = (DateTime.UtcNow - recoveryCheckpoint["savedUtc"].ToObject<DateTime>().ToUniversalTime()).TotalSeconds;
            Need(absence > 5, "H08 absence shorter than one recovery period");
            float until = Time.realtimeSinceStartup + 17;
            while (recoveryReloadEvents.Count < 2 && Time.realtimeSinceStartup < until)
                yield return null;
            Need(recoveryReloadEvents.Count >= 2 && recoveryActiveAt >= 0, "H08 two post-load ticks not observed");
            foreach (JObject tick in recoveryReloadEvents)
                Need(((JArray)tick["delta"]).All(v => (double)v == 1), "H08 accumulated catch-up payout detected");
            float first = (float)recoveryReloadEvents[0]["time"] - (float)recoveryLoaded["time"], period = (float)recoveryReloadEvents[1]["time"]
                - (float)recoveryReloadEvents[0]["time"];
            proof["firstTickDelay"] = first;
            proof["nextPeriod"] = period;
            proof["firstActiveObservation"] = recoveryActiveAt;
            Need(
                first >= 4.7f && first <= 6.5f && period >= 4.7f && period <= 6.5f,
                "H08 initial/subsequent period incorrect"
            );
            proof["nativeLoad"] = recoveryLoaded;
            proof["absenceSeconds"] = absence;
            proof["firstTickDelay"] = first;
            proof["nextPeriod"] = period;
            proof["passed"] = true;
        }
        finally
        {
            StopRecoveryReloadObserver();
            Freeze(player, false);
            if (recoveryCheckpoint?["periods"] is JArray values)
            {
                settings.Health.Interval.Value = (float)values[0];
                settings.Stamina.Interval.Value = (float)values[1];
                settings.Eitr.Interval.Value = (float)values[2];
            }
        }
    }
}
