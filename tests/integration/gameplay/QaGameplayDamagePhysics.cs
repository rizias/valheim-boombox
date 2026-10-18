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
    private readonly List<GameObject> damagePhysicsRoots = new List<GameObject>();
    private Vector3 damagePhysicsOrigin;
    private JObject physicsCleanup;
    private bool DamagePhysicsOperation(string operation, JObject request)
    {
        if (operation == "combat-clean-physics")
        {
            if (!ZNet.instance.IsServer())
                throw new InvalidOperationException("Marked physics cleanup belongs to D");
            var removed = new JArray();
            foreach (var view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                var zdo = view.GetZDO();
                if (zdo == null || !zdo.GetBool("boombox.qa.physics", false))
                    continue;
                zdo.SetOwner(ZNet.GetUID());
                if (!view.IsOwner())
                    throw new InvalidOperationException("Marked native physics cleanup owner missing");
                removed.Add(new JObject { ["id"] = zdo.m_uid.ToString(), ["name"] = view.name });
                ZNetScene.instance.Destroy(view.gameObject);
            }
            physicsCleanup = new JObject
            {
                ["removed"] = removed,
                ["marker"] = "boombox.qa.physics=true",
                ["utc"] = DateTime.UtcNow.ToString("O")
            };
            sample = physicsCleanup;
            return true;
        }
        if (operation != "combat-physics")
            return false;
        if (role != "client-a")
            throw new InvalidOperationException("Positive native drop belongs to A");
        var origin = Player.m_localPlayer.transform.position + Vector3.up * 20f;
        damagePhysicsOrigin = origin;
        var nativePrefabs = (List<GameObject>)AccessTools.Field(typeof(ZNetScene), "m_prefabs")
            .GetValue(ZNetScene.instance);
        var wall = nativePrefabs.Where(
            p => p != null
                && p.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
                && p.GetComponent<Piece>() != null
                && p.GetComponent<WearNTear>() != null
                && p.GetComponentInChildren<Collider>() != null
        ).OrderBy(
            p => p.name,
            StringComparer.Ordinal
        ).FirstOrDefault();
        if (wall == null)
            throw new InvalidOperationException("Current native building wall catalog is empty");
        var names = new[] { "Stone", "beech_log", "Raft", "Cart", wall.name };
        var offsets = new[] {
            new Vector3(8, 6, 0),
            new Vector3(0, 6, -12),
            new Vector3(-10, 6, 0),
            new Vector3(0, 6, 12),
            new Vector3(12, 6, 12)
        };
        for (int i = 0; i < names.Length; i++)
        {
            var prefab = ZNetScene.instance.GetPrefab(names[i]);
            if (prefab == null)
                throw new InvalidOperationException("Native physics prefab missing: " + names[i]);
            var root = UnityEngine.Object.Instantiate(prefab, origin + offsets[i], Quaternion.identity);
            var view = root.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
                throw new InvalidOperationException("Native A physics ownership not ready: " + names[i]);
            view.GetZDO().Set("boombox.qa.physics", true);
            damagePhysicsRoots.Add(root);
        }
        busy = true;
        StartCoroutine(DamagePhysics(origin));
        return true;
    }
    private JArray DamageBodies()
    {
        var observed = new JArray(damagePhysicsRoots.Select(root =>
        {
            var body = root.GetComponentInChildren<Rigidbody>();
            var view = root.GetComponent<ZNetView>();
            var sync = root.GetComponentInChildren<ZSyncTransform>();
            return new JObject
            {
                ["name"] = root.name,
                ["prefabId"] = view.GetZDO().GetPrefab(),
                ["id"] = view.GetZDO().m_uid.ToString(),
                ["owner"] = view.GetZDO().GetOwner(),
                ["isOwner"] = view.IsOwner(),
                ["position"] = Point(root.transform.position),
                ["floorClearance"] = root.transform.position.y - Player.m_localPlayer.transform.position.y,
                ["body"] = body != null,
                ["kinematic"] = body != null && body.isKinematic,
                ["useGravity"] = body != null && body.useGravity,
                ["gravityCache"] = sync == null
                    ? null
                    : JToken.FromObject((bool)AccessTools.Field(typeof(ZSyncTransform), "m_useGravity").GetValue(sync)),
                ["constraints"] = body == null ? 0 : (int)body.constraints,
                ["mass"] = body?.mass,
                ["velocity"] = body == null ? null : Point(body.linearVelocity),
                ["itemDrop"] = root.GetComponent<ItemDrop>() != null,
                ["treeLog"] = root.GetComponentInChildren<TreeLog>() != null,
                ["ship"] = root.GetComponentInChildren<Ship>() != null,
                ["cart"] = root.GetComponentInChildren<Vagon>() != null,
                ["piece"] = root.GetComponent<Piece>() != null,
                ["wearNTear"] = root.GetComponent<WearNTear>() != null
            };
        }));
        for (int i = 0; i < damagePhysicsRoots.Count; i++)
        {
            var colliders = damagePhysicsRoots[i].GetComponentsInChildren<Collider>().Where(c => c.enabled
                && !c.isTrigger).ToArray();
            observed[i]["closestDistance"] = colliders.Length == 0
                ? float.PositiveInfinity
                : colliders.Min(c => Vector3.Distance(damagePhysicsOrigin, c.ClosestPoint(damagePhysicsOrigin)));
        }
        return observed;
    }
    private IEnumerator DamagePhysics(Vector3 origin)
    {
        var settings = BoomboxPlugin.Instance.Settings;
        var entries = new BepInEx.Configuration.ConfigEntryBase[] {
            settings.PushEnvironment,
            settings.EnvironmentRadius,
            settings.EnvironmentPressure,
            settings.EnvironmentMaxMass,
            settings.EnvironmentMaxBodies,
            settings.EnvironmentMaxSpeedChange,
            settings.EnvironmentPushCooldown,
            settings.WaveSpeed
        };
        var prior = entries.Select(entry => entry.BoxedValue).ToArray();
        bool save = BoomboxPlugin.Instance.Config.SaveOnConfigSet;
        BoomboxPlugin.Instance.Config.SaveOnConfigSet = false;
        try
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var nativeBeforeIsolation = DamageBodies();
            foreach (var root in damagePhysicsRoots)
            {
                var body = root.GetComponentInChildren<Rigidbody>();
                if (body == null)
                    continue;
                foreach (var sync in root.GetComponentsInChildren<ZSyncTransform>())
                    AccessTools.Field(typeof(ZSyncTransform), "m_useGravity").SetValue(sync, false);
                body.useGravity = false;
                body.isKinematic = false;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.linearDamping = 0;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            settings.PushEnvironment.Value = true;
            settings.EnvironmentRadius.Value = 1;
            settings.EnvironmentPressure.Value = 10000;
            settings.EnvironmentMaxMass.Value = 0;
            settings.EnvironmentMaxBodies.Value = 0;
            settings.EnvironmentMaxSpeedChange.Value = 2;
            settings.EnvironmentPushCooldown.Value = 1;
            settings.WaveSpeed.Value = 400;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var baseline = DamageBodies();
            var front1 = BassWave.Emit(origin, 1, 1);
            var wave1 = front1.EnvironmentImpulse;
            float t1 = Time.time;
            float maximumRadius = front1.MaximumRadius;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            var first = DamageBodies();
            yield return new WaitForSecondsRealtime(.15f);
            var beforeSecond = DamageBodies();
            var wave2 = BassWave.Emit(origin, 1, 1).EnvironmentImpulse;
            float t2 = Time.time;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            var second = DamageBodies();
            yield return new WaitForSecondsRealtime(1.05f);
            var beforeThird = DamageBodies();
            var wave3 = BassWave.Emit(origin, 1, 1).EnvironmentImpulse;
            float t3 = Time.time;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            sample =
                new JObject
                {
                    ["nativeUID"] = ZNet.GetUID(),
                    ["origin"] = Point(origin),
                    ["nativeBeforeIsolation"] = nativeBeforeIsolation,
                    ["baseline"] = baseline,
                    ["first"] = first,
                    ["beforeSecond"] = beforeSecond,
                    ["second"] = second,
                    ["beforeThird"] = beforeThird,
                    ["third"] = DamageBodies(),
                    ["times"] = new JArray(t1, t2, t3),
                    ["hitCounts"] = new JArray(wave1.HitCount, wave2.HitCount, wave3.HitCount),
                    ["candidates"] = new JArray(wave1.CandidateCount, wave2.CandidateCount, wave3.CandidateCount),
                    ["isolation"] =
                        "Native prefabs/owners; wave origin20m above player/floor, objects26m above floor. PREPARATION ONLY: native ZSyncTransform.m_useGravity=false with Rigidbody.useGravity=false; before/after runtime gravity flags recorded. Linear damping0, FreezeRotation only; no FreezePosition, kinematic or velocity changes during measurement; real BassWave/Physics"
                };
            sample["maximumRadius"] = maximumRadius;
        }
        finally
        {
            try
            {
                for (int index = 0; index < entries.Length; index++)
                    entries[index].BoxedValue = prior[index];
            }
            finally
            {
                BoomboxPlugin.Instance.Config.SaveOnConfigSet = save;
            }
            foreach (var root in damagePhysicsRoots)
                if (root != null)
                {
                    var view = root.GetComponent<ZNetView>();
                    if (view != null && view.IsValid() && view.IsOwner())
                        ZNetScene.instance.Destroy(root);
                    else
                        error = "Native A physics ownership changed before cleanup";
                }
            damagePhysicsRoots.Clear();
            busy = false;
        }
    }
}
