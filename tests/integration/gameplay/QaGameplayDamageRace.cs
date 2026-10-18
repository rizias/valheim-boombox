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
    private static QaGameplay damageObserver;
    private string delayedSource;
    private ZRpc delayedRpc;
    private byte[] delayedAura;
    private int delayedPackets;
    private bool delayApplied;
    private string delayedApplyFailure;
    private byte[] delayedApply;
    private ZRpc delayedApplyRpc;
    private long registrationPeer;
    private byte[] registrationAura, registrationVisual;
    private ZRpc registrationRpc;
    private JObject registrationProof, sourceProof, applyProof;
    private bool delayNextUnknown;
    private JObject lastDamageComplete, lastDamageResult;
    private JArray lastFreshPendingState;
    private readonly JArray damageResults = new JArray();
    private JObject serverOwnerProof;
    private bool DamageRaceOperation(string operation, JObject request)
    {
        if (operation == "combat-waves")
        {
            sample = new JObject
            {
                ["count"] = UnityEngine.Object.FindObjectsByType<BassWave>(FindObjectsSortMode.None)
                .Length
            };
            return true;
        }
        if (operation == "combat-watch")
        {
            damageObserver = this;
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxAuraNetwork), "ReceiveState"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(DelayDamageAura))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxMusicNetwork), "ReceiveApplied"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(DelayDamageApply))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxAuraNetwork), "ReceiveSources"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(DelayRegistrationAura))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxRemoteNetwork), "Receive"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(DelayRegistrationVisual))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxMusicNetwork), "Complete"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(ObserveDamageComplete))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxMusicNetwork), "Result"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(ObserveDamageResult)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(ObserveDamageResultAfter))
            );
            safetyHarmony.Patch(
                AccessTools.Method(typeof(BoomboxMusicNetwork), "AuraStateReceived"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(ObserveFreshPendingState))
            );
            return true;
        }
        if (operation == "combat-delay-source")
        {
            delayedSource = (string)request["source"];
            delayNextUnknown = (bool?)request["unknown"] ?? false;
            delayedAura = null;
            delayedPackets = 0;
            sourceProof = null;
            if ((bool?)request["short"] == true)
                StartCoroutine(ShortDamageSource());
            return true;
        }
        if (operation == "combat-delay-registration")
        {
            registrationPeer = (long)request["peer"];
            registrationAura = registrationVisual = null;
            registrationProof = null;
            if ((bool?)request["short"] == true)
                StartCoroutine(ShortRegistration());
            return true;
        }
        if (operation == "combat-arm-owner-change")
        {
            serverOwnerProof = null;
            StartCoroutine(ShortServerOwner((long)request["owner"], (long)request["peer"]));
            return true;
        }
        if (operation == "combat-release-registration")
        {
            ReleaseRegistration();
            return true;
        }
        if (operation == "combat-delay-apply")
        {
            delayApplied = true;
            delayedApply = null;
            applyProof = null;
            delayedApplyFailure = null;
            if ((bool?)request["short"] == true)
                StartCoroutine(ShortDamageApply((long)request["peer"]));
            return true;
        }
        if (operation == "combat-release-apply")
        {
            delayApplied = false;
            if (delayedApply == null)
                throw new InvalidOperationException("No real native Apply RPC captured");
            AccessTools.Method(typeof(BoomboxMusicNetwork), "ReceiveApplied").Invoke(
                BoomboxMusicNetwork.Current,
                new object[] { delayedApplyRpc, new ZPackage(delayedApply) }
            );
            delayedApply = null;
            return true;
        }
        if (operation == "combat-release-source")
        {
            ReleaseDamageSource();
            return true;
        }
        if (operation != "combat-observe")
            return false;
        sample = DamageObservation();
        return true;
    }
    private JObject DamageObservation()
    {
        var service = BoomboxMusicNetwork.Current;
        var waiting = service == null
            ? null
            : (IDictionary)AccessTools.Field(typeof(BoomboxMusicNetwork), "waiting").GetValue(service);
        var result = new JObject
        {
            ["waiting"] = waiting?.Count ?? 0,
            ["delayedPackets"] = delayedPackets,
            ["heldAura"] = delayedAura != null,
            ["heldApply"] = delayedApply != null,
            ["rerouted"] = service?.ReroutedHits ?? 0,
            ["cancelledBegins"] = service?.CancelledBegins ?? 0,
            ["pendingBegins"] = service == null
                ? 0
                : ((IDictionary)AccessTools.Field(typeof(BoomboxMusicNetwork), "pendingBegins")
                    .GetValue(service)).Count,
            ["visualSources"] = new JArray(
                BoomboxRemoteNetwork.Current?.States.Select(
                    s => new JObject { ["id"] = s.Id, ["active"] = s.Active, ["worldItem"] = s.WorldItem.ToString() }
                ) ?? Enumerable.Empty<JObject>()
            ),
            ["sources"] = new JArray(
                BoomboxAuraNetwork.Sources.Select(
                    s => new JObject { ["id"] = s.Id, ["active"] = s.Active, ["peer"] = s.Peer }
                )
            )
        };
        result["pendingRequests"] = service == null
            ? 0
            : ((IDictionary)AccessTools.Field(typeof(BoomboxMusicNetwork), "pendingRequests").GetValue(service)).Count;
        int deliveries = 0;
        if (service != null)
            foreach (var value in ((IDictionary)AccessTools.Field(typeof(BoomboxMusicNetwork), "deliveries")
                .GetValue(service)).Values)
                if (!(bool)AccessTools.Field(value.GetType(), "Complete").GetValue(value))
                    deliveries++;
        result["pendingDeliveries"] = deliveries;
        result["heldRegistrationAura"] = registrationAura != null;
        result["heldRegistrationVisual"] = registrationVisual != null;
        result["auraSha"] = PacketHash(delayedAura);
        result["applySha"] = PacketHash(delayedApply);
        result["registrationAuraSha"] = PacketHash(registrationAura);
        result["registrationVisualSha"] = PacketHash(registrationVisual);
        result["utc"] = DateTime.UtcNow.ToString("O");
        result["realtime"] = Time.realtimeSinceStartup;
        var npc = ZNetScene.instance.FindInstance(npcId)?.GetComponent<Character>();
        var body = npc?.GetComponent<Rigidbody>();
        result["npcBody"] = body == null
            ? null
            : new JObject
            {
                ["kinematic"] = body.isKinematic,
                ["constraints"] = (int)body.constraints,
                ["velocity"] = Point(body.linearVelocity),
                ["waveCalls"] = npcWaveCalls.TryGetValue(npcId, out var calls)
                    ? calls
                    : 0
            };
        result["npc"] = CombatSnapshot()["npc"]?.DeepClone();
        result["registrationProof"] = registrationProof?.DeepClone();
        result["sourceProof"] = sourceProof?.DeepClone();
        result["applyProof"] = applyProof?.DeepClone();
        result["lastComplete"] = lastDamageComplete?.DeepClone();
        result["lastResult"] = lastDamageResult?.DeepClone();
        result["lastFreshPendingState"] = lastFreshPendingState?.DeepClone();
        result["serverOwnerProof"] = serverOwnerProof?.DeepClone();
        return result;
    }
    private static JObject DamageHitDetails(object hit)
    {
        var type = hit.GetType();
        var attackerId = (ZDOID)AccessTools.Field(type, "Attacker").GetValue(hit);
        var targetId = (ZDOID)AccessTools.Field(type, "Target").GetValue(hit);
        var attacker = ZNetScene.instance.FindInstance(attackerId)?.GetComponent<Player>();
        var target = ZNetScene.instance.FindInstance(targetId)?.GetComponent<Character>();
        string source = (string)AccessTools.Field(type, "Source").GetValue(hit);
        long peer = (long)AccessTools.Field(type, "Peer").GetValue(hit);
        var sourceState = BoomboxAuraNetwork.Sources.FirstOrDefault(s => s.Id == source && s.Peer == peer);
        var targets = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.MusicEffectTargets");
        var result = new JObject
        {
            ["source"] = source,
            ["peer"] = peer,
            ["epoch"] = (string)AccessTools.Field(type, "Epoch").GetValue(hit),
            ["sequence"] = (long)AccessTools.Field(type, "Sequence").GetValue(hit),
            ["cancelled"] = (bool)AccessTools.Field(type, "Cancelled").GetValue(hit),
            ["attackerId"] = attackerId.ToString(),
            ["targetId"] = targetId.ToString(),
            ["targetNativeOwner"] = ZDOMan.instance.GetZDO(targetId)?.GetOwner(),
            ["sourceKnown"] = sourceState != null,
            ["sourceActive"] = sourceState != null && sourceState.Active,
            ["utc"] = DateTime.UtcNow.ToString("O")
        };
        result["attacker"] = attacker == null
            ? null
            : new JObject
            {
                ["id"] = attacker.GetPlayerID(),
                ["local"] = attacker == Player.m_localPlayer,
                ["dead"] = attacker.IsDead(),
                ["teleporting"] = attacker.IsTeleporting(),
                ["health"] = attacker.GetHealth()
            };
        result["target"] = target == null
            ? null
            : new JObject
            {
                ["isOwner"] = target.IsOwner(),
                ["dead"] = target.IsDead(),
                ["health"] = target.GetHealth(),
                ["faction"] = target.GetFaction().ToString(),
                ["aiEnabled"] = target.GetBaseAI() != null && target.GetBaseAI().enabled,
                ["hostile"] = (bool)AccessTools.Method(
                targets,
                "IsHostile"
            ).Invoke(
                null,
                new object[] { target, attacker }
            )
            };
        return result;
    }
    private static void ObserveDamageComplete(object __0, int __1)
    {
        if (damageObserver == null)
            return;
        var result = DamageHitDetails(__0);
        result["disposition"] = __1;
        damageObserver.lastDamageComplete = result;
    }
    private static void ObserveDamageResult(object __0, long __1, int __2)
    {
        if (damageObserver == null)
            return;
        var result = DamageHitDetails(__0);
        result["recipient"] = __1;
        result["disposition"] = __2;
        result["deliveryBefore"] = DamageDeliveryDetails(__0);
        damageObserver.lastDamageResult = result;
    }
    private static JObject DamageDeliveryDetails(object hit)
    {
        string key = (string)AccessTools.Method(typeof(BoomboxMusicNetwork), "DeliveryKey").Invoke(null, new[] { hit });
        var deliveries = (IDictionary)AccessTools.Field(typeof(BoomboxMusicNetwork), "deliveries")
            .GetValue(BoomboxMusicNetwork.Current);
        if (!deliveries.Contains(key))
            return null;
        var delivery = deliveries[key];
        var type = delivery.GetType();
        var original = AccessTools.Field(type, "Hit").GetValue(delivery);
        return new JObject
        {
            ["owner"] = (long)AccessTools.Field(type, "Owner").GetValue(delivery),
            ["complete"] = (bool)AccessTools.Field(type, "Complete").GetValue(delivery),
            ["redirect"] = (bool)AccessTools.Field(type, "Redirect").GetValue(delivery),
            ["cancelled"] = (bool)AccessTools.Field(original.GetType(), "Cancelled").GetValue(original),
            ["ageSeconds"] = Time.realtimeSinceStartup
                - (float)AccessTools.Field(original.GetType(), "Created").GetValue(original)
        };
    }
    private static void ObserveDamageResultAfter(object __0)
    {
        if (damageObserver == null || damageObserver.lastDamageResult == null)
            return;
        damageObserver.lastDamageResult["deliveryAfter"] = DamageDeliveryDetails(__0);
        damageObserver.damageResults.Add(damageObserver.lastDamageResult.DeepClone());
        while (damageObserver.damageResults.Count > 4)
            damageObserver.damageResults.RemoveAt(0);
    }
    private static void ObserveFreshPendingState(object __0)
    {
        if (damageObserver == null || BoomboxMusicNetwork.Current == null || ZNet.instance.IsServer())
            return;
        var waiting = (IDictionary)AccessTools.Field(typeof(BoomboxMusicNetwork), "waiting")
            .GetValue(BoomboxMusicNetwork.Current);
        if (waiting.Count == 0)
            return;
        var incoming = ((IEnumerable)__0).Cast<object>().ToArray();
        var result = new JArray();
        foreach (var hit in waiting.Values)
        {
            var details = DamageHitDetails(hit);
            string source = (string)details["source"];
            long peer = (long)details["peer"];
            details["incomingActive"] = incoming.Any(
                s => (string)AccessTools.Field(s.GetType(), "Id").GetValue(s) == source
                    && (long)AccessTools.Field(s.GetType(), "Peer").GetValue(s) == peer
                    && (bool)AccessTools.Field(s.GetType(), "Active").GetValue(s)
            );
            result.Add(details);
        }
        damageObserver.lastFreshPendingState = result;
    }
    private void ReleaseRegistration()
    {
        registrationPeer = 0;
        if (registrationAura == null || registrationVisual == null)
            throw new InvalidOperationException("Real source/visual registration packets not captured");
        AccessTools.Method(typeof(BoomboxAuraNetwork), "ReceiveSources").Invoke(
            BoomboxAuraNetwork.Current,
            new object[] { registrationRpc, new ZPackage(registrationAura) }
        );
        AccessTools.Method(typeof(BoomboxRemoteNetwork), "Receive").Invoke(
            BoomboxRemoteNetwork.Current,
            new object[] { registrationRpc, new ZPackage(registrationVisual) }
        );
        registrationAura = registrationVisual = null;
    }
    private void ReleaseDamageSource()
    {
        delayedSource = null;
        delayNextUnknown = false;
        if (delayedAura == null)
            throw new InvalidOperationException("No real active-source AuraState RPC captured");
        AccessTools.Method(typeof(BoomboxAuraNetwork), "ReceiveState").Invoke(
            BoomboxAuraNetwork.Current,
            new object[] { delayedRpc, new ZPackage(delayedAura) }
        );
        delayedAura = null;
    }
    private IEnumerator ShortRegistration()
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var before = DamageObservation();
            if ((int)before["pendingBegins"] > 0 && (int)before["pendingRequests"] > 0 && registrationAura != null
            && registrationVisual != null)
            {
                float observed = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(.15f);
                var pending = DamageObservation();
                ReleaseRegistration();
                registrationProof = new JObject
                {
                    ["firstPending"] = before,
                    ["beforeRelease"] = pending,
                    ["afterRelease"] = DamageObservation(),
                    ["pendingHoldSeconds"] = Time.realtimeSinceStartup - observed
                };
                yield break;
            }
            yield return null;
        }
        registrationProof = new JObject
        {
            ["failure"] = "Real registration pending was not observed",
            ["last"] = DamageObservation()
        };
    }
    private IEnumerator ShortDamageSource()
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var before = DamageObservation();
            if ((int)before["waiting"] > 0 && delayedAura != null)
            {
                string source = delayedSource;
                float observed = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(.15f);
                var pending = DamageObservation();
                ReleaseDamageSource();
                sourceProof = new JObject
                {
                    ["source"] = source,
                    ["firstPending"] = before,
                    ["beforeRelease"] = pending,
                    ["afterRelease"] = DamageObservation(),
                    ["pendingHoldSeconds"] = Time.realtimeSinceStartup - observed
                };
                yield break;
            }
            yield return null;
        }
        sourceProof = new JObject
        {
            ["failure"] = "Real unknown-source waiting was not observed",
            ["last"] = DamageObservation()
        };
    }
    private IEnumerator ShortDamageApply(long newOwner)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (delayedApply != null)
            {
                float observed = Time.realtimeSinceStartup;
                var before = DamageObservation();
                yield return new WaitForSecondsRealtime(.15f);
                var current = ZDOMan.instance.GetZDO(npcId);
                if (current.GetOwner() == ZNet.GetUID())
                    Combat("combat-owner-local", new JObject { ["peer"] = newOwner });
                else if (current.GetOwner() != newOwner)
                    throw new InvalidOperationException("Native owner changed to another peer during real Apply hold");
                var changed = DamageObservation();
                DamageRaceOperation("combat-release-apply", new JObject());
                applyProof = new JObject
                {
                    ["before"] = before,
                    ["ownerChanged"] = changed,
                    ["afterRelease"] = DamageObservation(),
                    ["pendingHoldSeconds"] = Time.realtimeSinceStartup - observed,
                    ["passed"] = delayedApplyFailure == null,
                    ["error"] = delayedApplyFailure
                };
                yield break;
            }
            yield return null;
        }
        applyProof = new JObject { ["failure"] = "Real native Apply was not captured", ["last"] = DamageObservation() };
    }
    private IEnumerator ShortServerOwner(long currentOwner, long newOwner)
    {
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var deliveries = (IDictionary)AccessTools.Field(typeof(BoomboxMusicNetwork), "deliveries")
                .GetValue(BoomboxMusicNetwork.Current);
            foreach (var delivery in deliveries.Values)
            {
                var type = delivery.GetType();
                var hit = AccessTools.Field(type, "Hit").GetValue(delivery);
                if ((bool)AccessTools.Field(type, "Complete").GetValue(delivery)
                || (long)AccessTools.Field(type, "Owner").GetValue(delivery) != currentOwner
                || !((ZDOID)AccessTools.Field(hit.GetType(), "Target").GetValue(hit)).Equals(npcId))
                    continue;
                var before = DamageObservation();
                yield return new WaitForSecondsRealtime(.05f);
                Combat("combat-owner", new JObject { ["peer"] = newOwner });
                serverOwnerProof = new JObject { ["before"] = before, ["after"] = DamageObservation() };
                yield break;
            }
            yield return null;
        }
        serverOwnerProof = new JObject
        {
            ["failure"] = "No real native B delivery before server ownership change",
            ["last"] = DamageObservation()
        };
    }
    private static bool DelayDamageAura(ZRpc __0, ZPackage __1)
    {
        var observer = damageObserver;
        if (observer == null || observer.delayedSource == null && !observer.delayNextUnknown)
            return true;
        var copy = new ZPackage(__1.GetArray());
        var codec = typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxAuraCodec");
        AccessTools.Method(codec, "ReadHeader").Invoke(null, new object[] { copy, 0L });
        AccessTools.Method(codec, "ReadBalance").Invoke(null, new object[] { copy });
        var sources = (IEnumerable)AccessTools.Method(codec, "ReadSources").Invoke(null, new object[] { copy });
        bool active = false;
        foreach (var source in sources)
        {
            string id = (string)AccessTools.Field(source.GetType(), "Id").GetValue(source);
            if (!(bool)AccessTools.Field(source.GetType(), "Active").GetValue(source))
                continue;
            if (observer.delayNextUnknown && !BoomboxAuraNetwork.Sources.Any(s => s.Id == id))
            {
                observer.delayedSource = id;
                observer.delayNextUnknown = false;
            }
            if (id == observer.delayedSource)
                active = true;
        }
        if (!active)
            return true;
        observer.delayedRpc = __0;
        observer.delayedAura = __1.GetArray();
        observer.delayedPackets++;
        return false;
    }
    private static bool DelayDamageApply(ZRpc __0, ZPackage __1)
    {
        var observer = damageObserver;
        if (observer == null || !observer.delayApplied)
            return true;
        var bytes = __1.GetArray();
        if (observer.delayedApply != null && !observer.delayedApply.SequenceEqual(bytes))
        {
            observer.delayedApplyFailure = "Damage-race fixture received a second distinct Apply while one was held";
            observer.error = observer.delayedApplyFailure;
            return true;
        }
        observer.delayedApplyRpc = __0;
        observer.delayedApply = bytes;
        return false;
    }
    private static bool RegistrationMatches(ZRpc rpc)
    {
        var observer = damageObserver;
        return observer != null && observer.registrationPeer != 0
        && ZNet.instance.GetPeers().Any(p => p.m_rpc == rpc && p.m_uid == observer.registrationPeer);
    }
    private static bool DelayRegistrationAura(ZRpc __0, ZPackage __1)
    {
        if (!RegistrationMatches(__0))
            return true;
        damageObserver.registrationRpc = __0;
        damageObserver.registrationAura = __1.GetArray();
        return false;
    }
    private static bool DelayRegistrationVisual(ZRpc __0, ZPackage __1)
    {
        if (!RegistrationMatches(__0))
            return true;
        damageObserver.registrationRpc = __0;
        damageObserver.registrationVisual = __1.GetArray();
        return false;
    }
    private static string PacketHash(byte[] bytes)
    {
        if (bytes == null)
            return null;
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
    }
}
