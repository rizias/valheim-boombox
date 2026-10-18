using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static QaGameplay remainingObserver;
    private ItemDrop.ItemData remainingSword;
    private Ship remainingShip;
    private GameObject remainingLog;
    private JObject remainingAttack;
    private readonly JArray remainingHits = new JArray();
    private readonly JArray remainingPushes = new JArray();
    private bool RemainingOwnOperation(string operation, JObject request)
    {
        var player = Player.m_localPlayer;
        var settings = BoomboxPlugin.Instance?.Settings;
        if (operation == "remaining-own-prepare")
        {
            remainingObserver = this;
            remainingHits.Clear();
            remainingPushes.Clear();
            settings.MusicEnchantment.Value = settings.BoatHelp.Value = true;
            settings.Effect.CreatureDamage.Value = false;
            settings.PushEnvironment.Value = false;
            var weapons = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxEnchantWeapons");
            safetyHarmony.Patch(
                AccessTools.Method(weapons, "Landed"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(RemainingMeleeBefore)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(RemainingMeleeAfter))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxBoatHelp), "TryPush"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(RemainingBoatBefore)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(RemainingBoatAfter))
            );
            var prefab = ObjectDB.instance.GetItemPrefab("SwordBronze");
            if (prefab == null)
                throw new InvalidOperationException("Native SwordBronze absent");
            remainingSword = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            remainingSword.m_dropPrefab = prefab;
            remainingSword.m_customData["boombox.qa.remaining.weapon"] = "true";
            if (!player.GetInventory().AddItem(remainingSword) || !player.EquipItem(remainingSword, false))
                throw new InvalidOperationException("Native SwordBronze equip refused");
            return true;
        }
        if (operation == "remaining-attack")
        {
            busy = true;
            StartCoroutine(GuardAudio(RemainingAttack()));
            return true;
        }
        if (operation == "remaining-boat")
        {
            busy = true;
            StartCoroutine(GuardAudio(RemainingBoat((float?)request["lane"] ?? 0)));
            return true;
        }
        if (operation == "remaining-boat-stop")
        {
            player.StopDoodadControl();
            player.AttachStop();
            if (remainingShip != null && remainingShip.GetComponent<ZNetView>().IsOwner())
                ZNetScene.instance.Destroy(remainingShip.gameObject);
            remainingShip = null;
            return true;
        }
        return false;
    }
    private float RemainingCharge()
    {
        var type = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxEnchantWeapons");
        var charge = AccessTools.Field(type, "Melee").GetValue(null);
        return remainingSword == null
            ? 0f
            : (float)AccessTools.Method(charge.GetType(), "For").Invoke(charge, new object[] { remainingSword });
    }
    private JObject RemainingOwnSnapshot()
    {
        var player = Player.m_localPlayer;
        var ship = remainingShip;
        return new JObject
        {
            ["charge"] = RemainingCharge(),
            ["weapon"] = remainingSword?.m_dropPrefab?.name,
            ["hits"] = remainingHits.DeepClone(),
            ["attack"] = remainingAttack?.DeepClone(),
            ["pushes"] = remainingPushes.DeepClone(),
            ["ship"] = ship == null
                ? null
                : new JObject
                {
                    ["id"] = ship.GetComponent<ZNetView>().GetZDO().m_uid.ToString(),
                    ["owner"] = ship.GetComponent<ZNetView>().GetZDO().GetOwner(),
                    ["isOwner"] = ship.GetComponent<ZNetView>().IsOwner(),
                    ["controlled"] = player != null && player.GetControlledShip() == ship,
                    ["user"] = ship.GetComponent<ZNetView>().GetZDO().GetLong(ZDOVars.s_user, 0),
                    ["speed"] = ship.GetSpeedSetting().ToString(),
                    ["velocity"] = Point(ship.GetComponent<Rigidbody>().linearVelocity),
                    ["depth"] = (float)AccessTools.Field(typeof(Ship), "m_lastDepth").GetValue(ship)
                }
        };
    }
    private IEnumerator RemainingAttack()
    {
        var player = Player.m_localPlayer;
        remainingHits.Clear();
        var prefab = ZNetScene.instance.GetPrefab("Skeleton");
        if (prefab == null)
            throw new InvalidOperationException("Native sword target absent");
        player.SetLookDir(remainingSea, 0);
        remainingLog = UnityEngine.Object.Instantiate(
            prefab,
            player.transform.position + remainingSea * 1.7f,
            Quaternion.LookRotation(-remainingSea)
        );
        remainingRoots.Add(remainingLog);
        var view = remainingLog.GetComponent<ZNetView>();
        view.GetZDO().Set("boombox.qa.remaining", remainingId);
        var body = remainingLog.GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeAll;
        var log = remainingLog.GetComponent<Character>();
        log.GetBaseAI().enabled = false;
        AccessTools.Field(typeof(Character), "m_damageModifiers").SetValue(log, new HitData.DamageModifiers());
        Game.instance.SetForcePlayerDifficulty(1);
        log.SetMaxHealth(1000);
        log.SetHealth(1000);
        float before = log.GetHealth();
        float charge = RemainingCharge();
        yield return new WaitForFixedUpdate();
        bool started = player.StartAttack(null, false);
        if (!started)
            throw new InvalidOperationException("Native SwordBronze StartAttack refused");
        AccessTools.Method(typeof(Humanoid), "OnAttackTrigger").Invoke(player, null);
        float deadline = Time.realtimeSinceStartup + 3;
        while (remainingHits.Count == 0 && Time.realtimeSinceStartup < deadline)
            yield return null;
        yield return new WaitForSecondsRealtime(.15f);
        remainingAttack = new JObject
        {
            ["nativeStarted"] = started,
            ["chargeBefore"] = charge,
            ["healthBefore"] = before,
            ["healthAfter"] = log == null ? 0 : log.GetHealth(),
            ["target"] = view.GetZDO().m_uid.ToString(),
            ["hits"] = remainingHits.DeepClone(),
            ["utc"] = DateTime.UtcNow.ToString("O")
        };
        if (remainingLog != null && view.IsOwner())
            ZNetScene.instance.Destroy(remainingLog);
        remainingLog = null;
        busy = false;
    }
    private IEnumerator RemainingBoat(float lane)
    {
        var player = Player.m_localPlayer;
        player.StopDoodadControl();
        player.AttachStop();
        var point = remainingCoast + remainingSea * 14 + Vector3.Cross(Vector3.up, remainingSea) * lane;
        point.y = ZoneSystem.instance.m_waterLevel + .3f;
        var prefab = ZNetScene.instance.GetPrefab("Raft");
        if (prefab == null)
            throw new InvalidOperationException("Native Raft absent");
        var root = UnityEngine.Object.Instantiate(prefab, point, Quaternion.LookRotation(remainingSea));
        remainingRoots.Add(root);
        remainingShip = root.GetComponent<Ship>();
        var view = root.GetComponent<ZNetView>();
        if (!view.IsOwner())
            throw new InvalidOperationException("Native local boat owner absent");
        view.GetZDO().Set("boombox.qa.remaining", remainingId);
        view.GetZDO().Set(ZDOVars.s_health, 100000f);
        var controls = root.GetComponentInChildren<ShipControlls>();
        float deadline = Time.realtimeSinceStartup + 8;
        yield return new WaitForSecondsRealtime(.5f);
        Freeze(player, false);
        var playerBody = player.GetComponent<Rigidbody>();
        playerBody.position = player.transform.position = controls.m_attachPoint.position + Vector3.up * .8f;
        player.transform.rotation = controls.m_attachPoint.rotation;
        playerBody.rotation = controls.m_attachPoint.rotation;
        playerBody.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        while (player.GetStandingOnShip() != remainingShip && Time.realtimeSinceStartup < deadline)
            yield return new WaitForFixedUpdate();
        controls.Interact(player, false, false);
        while (player.GetControlledShip() != remainingShip && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (player.GetControlledShip() != remainingShip
            || view.GetZDO().GetLong(ZDOVars.s_user, 0) != player.GetPlayerID())
            throw new InvalidOperationException("Native helm user/control not accepted");
        remainingShip.Forward();
        remainingShip.Forward();
        root.GetComponent<Rigidbody>().linearVelocity = remainingSea * 2f;
        remainingPushes.Clear();
        yield return new WaitForSecondsRealtime(1.2f);
        sample = RemainingOwnSnapshot();
        busy = false;
    }
    private static void RemainingMeleeBefore(HitData __0, out JObject __state)
    {
        __state = null;
        if (remainingObserver == null || __0 == null)
            return;
        var weapons = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxEnchantWeapons");
        __state = new JObject
        {
            ["before"] = __0.m_damage.GetTotalDamage(),
            ["swing"] = (float)AccessTools.Property(weapons, "Swing").GetValue(null),
            ["utc"] = DateTime.UtcNow.ToString("O")
        };
    }
    private static void RemainingMeleeAfter(HitData __0, JObject __state)
    {
        if (remainingObserver == null || __state == null || __0 == null)
            return;
        __state["after"] = __0.m_damage.GetTotalDamage();
        remainingObserver.remainingHits.Add(__state);
    }
    private static void RemainingBoatBefore(Ship __0, out JObject __state)
    {
        __state = null;
        if (remainingObserver == null || __0 == null || __0 != remainingObserver.remainingShip)
            return;
        var body = __0.GetComponent<Rigidbody>();
        var drag = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxBoatDrag");
        var losses = (float[])AccessTools.Field(drag, "losses").GetValue(null);
        int count = (int)AccessTools.Field(drag, "count")
            .GetValue(null),
            next = (int)AccessTools.Field(drag, "next")
            .GetValue(null);
        float sum = 0;
        for (int i = 1; i <= count; i++)
            sum += losses[(next - i + losses.Length) % losses.Length];
        __state = new JObject
        {
            ["forceBefore"] = Point(body.GetAccumulatedForce(Time.fixedDeltaTime)),
            ["mass"] = body.mass,
            ["step"] = Time.fixedDeltaTime,
            ["lossSum"] = sum,
            ["count"] = count,
            ["velocity"] = Point(body.linearVelocity),
            ["depth"] = (float)AccessTools.Field(typeof(Ship), "m_lastDepth").GetValue(__0),
            ["utc"] = DateTime.UtcNow.ToString("O")
        };
    }
    private static void RemainingBoatAfter(Ship __0, bool __result, JObject __state)
    {
        if (remainingObserver == null || __state == null)
            return;
        __state["pushed"] = __result;
        __state["forceAfter"] = Point(__0.GetComponent<Rigidbody>().GetAccumulatedForce((float)__state["step"]));
        if (remainingObserver.remainingPushes.Count >= 16)
            remainingObserver.remainingPushes.RemoveAt(0);
        remainingObserver.remainingPushes.Add(__state);
    }
}
