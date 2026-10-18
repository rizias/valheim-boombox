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
    private static readonly HashSet<ZDOID> caseEnemyIds = new HashSet<ZDOID>();
    private static readonly List<JObject> caseEnemyHits = new List<JObject>();
    private static void CaseEnemyDamageBefore(Character __instance, HitData __1, out JObject __state)
    {
        __state = null;
        if (!caseEnemyIds.Contains(__instance.GetZDOID())
        || !(bool)AccessTools.Method(typeof(BoomboxCombatDamage), "IsWave").Invoke(null, new object[] { __1 }))
            return;
        __state = new JObject
        {
            ["id"] = __instance.GetZDOID().ToString(),
            ["healthBefore"] = __instance.GetHealth(),
            ["attackBefore"] = __instance.InAttack(),
            ["staggerBefore"] = __instance.IsStaggering(),
            ["push"] = __1.m_pushForce,
            ["staggerMultiplier"] = __1.m_staggerMultiplier,
            ["velocityBefore"] = Point(__instance.GetVelocity()),
            ["positionBefore"] = Point(__instance.transform.position)
        };
    }
    private static void CaseEnemyDamageAfter(Character __instance, JObject __state)
    {
        if (__state == null)
            return;
        __state["healthAfter"] = __instance.GetHealth();
        __state["attackAfter"] = __instance.InAttack();
        __state["staggerAfter"] = __instance.IsStaggering();
        __state["velocityAfter"] = Point(__instance.GetVelocity());
        __state["positionAfter"] = Point(__instance.transform.position);
        caseEnemyHits.Add(__state);
    }
    private IEnumerator CaseEnemyWave(string id)
    {
        var plugin = BoomboxPlugin.Instance;
        var settings = plugin.Settings;
        var player = Player.m_localPlayer;
        var proof =
            new JObject
            {
                ["case"] = "G-" + id.Substring(1),
                ["scope"] =
                    "Native enemy attack animation and native RPC_Damage. AI disabled only for deterministic fixture; Animator speed 0.2 retains the real attack. Observe, do not replace, wave damage. No rigidbody position freeze."
            };
        lastCapture = proof;
        var rows = new JArray();
        proof["targets"] = rows;
        float start = Time.realtimeSinceStartup;
        float damage = settings.Damage[1].Value, push = settings.Push[1].Value, radius = settings.Radius[1].Value;
        bool effects = settings.Effect.CreatureDamage.Value, musicOnly = settings.MusicOnly.Value;
        var variant = settings.Effect.Variant.Value;
        var observer = new Harmony("local.boombox.qa.enemy-wave-case");
        var roots = new List<GameObject>();
        caseEnemyIds.Clear();
        caseEnemyHits.Clear();
        try
        {
            yield return CaseReadyInventory();
            player.transform.position += Vector3.up * 20;
            Floor(player.transform.position);
            Physics.SyncTransforms();
            settings.Damage[1].Value = 10;
            settings.Push[1].Value = 5;
            settings.Radius[1].Value = 20;
            settings.Effect.CreatureDamage.Value = false;
            settings.MusicOnly.Value = false;
            observer.Patch(
                AccessTools.Method(typeof(Character), "RPC_Damage"),
                prefix: new HarmonyMethod(
                    typeof(QaGameplay),
                    nameof(CaseEnemyDamageBefore)
                )
                {
                    priority = Priority.Last
                },
                postfix: new HarmonyMethod(
                    typeof(QaGameplay),
                    nameof(CaseEnemyDamageAfter)
                )
                {
                    priority = Priority.Last
                }
            );
            Create(id, new[] { 1, 0, 0, 0, 0 }, 0, true, player.transform.position + Vector3.forward);
            var deck = decks[id];
            float audioReady = Time.realtimeSinceStartup + 3;
            while (!deck.IsSyntheticActive && Time.realtimeSinceStartup < audioReady)
                yield return null;
            Need(deck.IsSyntheticActive, "Enemy-wave synthetic rhythm fixture not ready");
            foreach (string prefab in id == "G06" ? new[] { "Skeleton" } : new[] { "Troll", "Eikthyr" })
            {
                var root = UnityEngine.Object.Instantiate(
                    ZNetScene.instance.GetPrefab(prefab),
                    player.transform.position + Vector3.right * 4,
                    Quaternion.identity
                );
                roots.Add(root);
                var npc = root.GetComponent<Humanoid>();
                Need(npc != null, "Enemy-wave fixture is not Humanoid: " + prefab);
                var view = root.GetComponent<ZNetView>();
                view.GetZDO().Set("boombox.qa.npc", true);
                npcId = view.GetZDO().m_uid;
                caseEnemyIds.Add(npcId);
                npc.GetBaseAI().enabled = false;
                npc.SetMaxHealth(5000);
                npc.SetHealth(5000);
                AccessTools.Field(typeof(Character), "m_damageModifiers").SetValue(npc, new HitData.DamageModifiers());
                var body = root.GetComponent<Rigidbody>();
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.linearVelocity = Vector3.zero;
                yield return new WaitForSecondsRealtime(.3f);
                npc.EquipBestWeapon(player, null, null, null);
                Need(npc.GetCurrentWeapon() != null, "Native AI weapon selection failed: " + prefab);
                foreach (bool attacking in id == "G06" ? new[] { true } : new[] { false, true })
                {
                    if (attacking)
                    {
                        bool started = false;
                        var preparation = new JArray();
                        proof["attackPreparation"] = preparation;
                        float prepareUntil = Time.realtimeSinceStartup + 4;
                        while (!started && Time.realtimeSinceStartup < prepareUntil)
                        {
                            npc.EquipBestWeapon(player, null, null, null);
                            var weapon = npc.GetCurrentWeapon();
                            preparation.Add(new JObject
                            {
                                ["prefab"] = prefab,
                                ["weapon"] = weapon?.m_shared.m_name,
                                ["primary"] = weapon?.HavePrimaryAttack(),
                                ["canMove"] = npc.CanMove(),
                                ["minorAction"] = npc.InMinorAction(),
                                ["knockedBack"] = npc.IsKnockedBack(),
                                ["stagger"] = npc.IsStaggering(),
                                ["attack"] = npc.InAttack()
                            });
                            started = npc.StartAttack(player, false);
                            if (!started)
                                yield return new WaitForSecondsRealtime(.25f);
                        }
                        Need(started, "Native attack fixture refused: " + prefab);
                        float attackReady = Time.realtimeSinceStartup + 2;
                        while (!npc.InAttack() && Time.realtimeSinceStartup < attackReady)
                            yield return null;
                        Need(npc.InAttack(), "Native attack animation not observed: " + prefab);
                        root.GetComponentInChildren<Animator>().speed = .2f;
                    }
                    else
                        Need(!npc.InAttack(), "Idle fixture already attacking");
                    int before = caseEnemyHits.Count;
                    var forceBefore = body.GetAccumulatedForce();
                    Combat("combat-strike", new JObject { ["key"] = id });
                    var forceAfter = body.GetAccumulatedForce();
                    Need(
                        (forceAfter - forceBefore).sqrMagnitude < .000001f,
                        "Wave queued a physical impulse outside RPC"
                    );
                    float hitReady = Time.realtimeSinceStartup + 2;
                    while (caseEnemyHits.Count == before && Time.realtimeSinceStartup < hitReady)
                        yield return null;
                    Need(caseEnemyHits.Count > before, "No native musical damage observed: " + prefab);
                    var hit = caseEnemyHits.Skip(before).FirstOrDefault(h => (string)h["id"] == npcId.ToString());
                    Need(hit != null, "Wave hit a different fixture");
                    Need((float)hit["healthAfter"] < (float)hit["healthBefore"], "Wave caused no health loss");
                    Need(
                        (float)hit["push"] == 0 && (float)hit["staggerMultiplier"] == 0 && !(bool)hit["staggerBefore"]
                        && !(bool)hit["staggerAfter"],
                        "Wave applied push or stagger"
                    );
                    if (attacking)
                        Need((bool)hit["attackBefore"] && (bool)hit["attackAfter"], "Wave interrupted native attack");
                    Need(
                        JToken.DeepEquals(hit["velocityBefore"], hit["velocityAfter"])
                        && JToken.DeepEquals(hit["positionBefore"], hit["positionAfter"]),
                        "Wave changed NPC velocity/position during RPC"
                    );
                    hit["prefab"] = prefab;
                    hit["duringAttack"] = attacking;
                    hit["forceBefore"] = Point(forceBefore);
                    hit["forceAfter"] = Point(forceAfter);
                    rows.Add(hit);
                    yield return new WaitForSecondsRealtime(.7f);
                    Need(!npc.IsStaggering(), "Delayed musical stagger observed");
                }
                caseEnemyIds.Remove(npcId);
                ZNetScene.instance.Destroy(root);
                roots.Remove(root);
                yield return null;
            }
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            observer.UnpatchSelf();
            caseEnemyIds.Clear();
            foreach (var root in roots)
                if (root != null)
                    ZNetScene.instance.Destroy(root);
            settings.Damage[1].Value = damage;
            settings.Push[1].Value = push;
            settings.Radius[1].Value = radius;
            settings.Effect.CreatureDamage.Value = effects;
            settings.MusicOnly.Value = musicOnly;
            settings.Effect.Variant.Value = variant;
            Freeze(player, false);
        }
    }
}
