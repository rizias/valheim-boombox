using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static Character ordinaryTarget;
    private static readonly List<JObject> ordinaryHits = new List<JObject>();
    private static readonly List<JObject> ordinaryBlocks = new List<JObject>();
    private static void OrdinaryHitBefore(Character __instance, HitData __1, out JObject __state)
    {
        __state = null;
        if (__instance != ordinaryTarget
        || (bool)AccessTools.Method(typeof(BoomboxCombatDamage), "IsWave").Invoke(null, new object[] { __1 }))
            return;
        __state = new JObject { ["before"] = __instance.GetHealth(), ["wave"] = false };
    }
    private static void OrdinaryHitAfter(Character __instance, JObject __state)
    {
        if (__state != null)
        {
            __state["after"] = __instance.GetHealth();
            ordinaryHits.Add(__state);
        }
    }
    private static void OrdinaryBlockBefore(Humanoid __instance, HitData __0, out JObject __state)
    {
        __state = null;
        if (__instance != Player.m_localPlayer)
            return;
        __state = new JObject
        {
            ["timer"] = (float)AccessTools.Field(typeof(Humanoid), "m_blockTimer").GetValue(__instance),
            ["beforeDamage"] = __0.GetTotalDamage()
        };
    }
    private static void OrdinaryBlockAfter(HitData __0, bool __result, JObject __state)
    {
        if (__state != null)
        {
            __state["accepted"] = __result;
            __state["remainingDamage"] = __0.GetTotalDamage();
            ordinaryBlocks.Add(__state);
        }
    }
    private IEnumerator CaseOrdinaryCombat()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var settings = plugin.Settings;
        var proof =
            new JObject
            {
                ["case"] = "G-08",
                ["scope"] =
                    "Mouse/keyboard InputSystem -> ordinary melee, shield and dodge. Incoming non-wave hits are controlled fixtures through Character.Damage, not direct BlockAttack calls. No NPC AI/pathfinding acceptance."
            };
        lastCapture = proof;
        var input = InputSystem.settings.backgroundBehavior;
        bool damage = settings.Effect.CreatureDamage.Value, musicOnly = settings.MusicOnly.Value;
        float push = settings.Push[1].Value;
        var observer = new Harmony("local.boombox.qa.ordinary-combat");
        var gear = new List<ItemDrop.ItemData>();
        GameObject root = null;
        ordinaryHits.Clear();
        ordinaryBlocks.Clear();
        float start = Time.realtimeSinceStartup;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            player.transform.position += Vector3.up * 20;
            Floor(player.transform.position);
            Physics.SyncTransforms();
            Freeze(player, false);
            float ground = Time.realtimeSinceStartup + 2;
            while (!player.IsOnGround() && Time.realtimeSinceStartup < ground)
                yield return null;
            Need(player.IsOnGround(), "G08 player did not settle on fixture floor");
            foreach (string name in new[] { "SwordBronze", "ShieldBanded" })
            {
                var prefab = ObjectDB.instance.GetItemPrefab(name);
                var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_customData["boombox.qa.fixture"] = "G08-gear";
                Need(
                    player.GetInventory().AddItem(item) && player.EquipItem(item, false),
                    "G08 combat gear fixture failed"
                );
                gear.Add(item);
            }
            settings.MusicOnly.Value = false;
            settings.Push[1].Value = 0;
            settings.Effect.CreatureDamage.Value = true;
            Create("G08", new int[5], 0, false, player.transform.position);
            var deck = decks["G08"];
            yield return new WaitForSecondsRealtime(.3f);
            Need(deck.IsSyntheticActive, "G08 music not active");
            root = UnityEngine.Object.Instantiate(
                ZNetScene.instance.GetPrefab("Greydwarf"),
                player.transform.position + Vector3.forward * 1.3f,
                Quaternion.identity
            );
            ordinaryTarget = root.GetComponent<Character>();
            root.GetComponent<ZNetView>().GetZDO().Set("boombox.qa.npc", true);
            ordinaryTarget.GetBaseAI().enabled = false;
            ordinaryTarget.SetMaxHealth(5000);
            ordinaryTarget.SetHealth(5000);
            observer.Patch(
                AccessTools.Method(typeof(Character), "RPC_Damage"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(OrdinaryHitBefore)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(OrdinaryHitAfter))
            );
            observer.Patch(
                AccessTools.Method(typeof(Humanoid), "BlockAttack"),
                prefix: new HarmonyMethod(typeof(QaGameplay), nameof(OrdinaryBlockBefore)),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(OrdinaryBlockAfter))
            );
            yield return new WaitForSecondsRealtime(.4f);
            player.transform.rotation = Quaternion.identity;
            player.SetLookDir(Vector3.forward, 0);
            InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left));
            yield return new WaitForSecondsRealtime(.15f);
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            float melee = Time.realtimeSinceStartup + 3;
            while (ordinaryHits.Count == 0 && Time.realtimeSinceStartup < melee)
                yield return null;
            Need(
                ordinaryHits.Any(h => (float)h["after"] < (float)h["before"]),
                "G08 native melee did not damage target"
            );
            proof["melee"] = JArray.FromObject(ordinaryHits);
            float settle = Time.realtimeSinceStartup + 3;
            while (player.InAttack() && Time.realtimeSinceStartup < settle)
                yield return null;
            Need(!player.InAttack(), "G08 melee animation did not finish");
            player.SetGodMode(false);
            root.transform.position = player.transform.position + Vector3.forward * 1.3f;
            root.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            foreach (bool parry in new[] { false, true })
            {
                InputSystem.QueueStateEvent(Mouse.current, new MouseState());
                yield return new WaitForSecondsRealtime(.3f);
                player.AddStamina(player.GetMaxStamina());
                InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Right));
                float blockReady = Time.realtimeSinceStartup + 2;
                while (!player.IsBlocking() && Time.realtimeSinceStartup < blockReady)
                    yield return null;
                Need(player.IsBlocking(), "G08 native block input failed");
                if (!parry)
                    yield return new WaitForSecondsRealtime(.4f);
                var hit = new HitData
                {
                    m_blockable = true,
                    m_dodgeable = true,
                    m_pushForce = 0,
                    m_point = player.GetCenterPoint(),
                    m_dir = (player.transform.position - root.transform.position).normalized,
                    m_hitType = HitData.HitType.Impact
                };
                hit.m_damage.m_blunt = 5;
                hit.SetAttacker(ordinaryTarget);
                int prior = ordinaryBlocks.Count;
                player.Damage(hit);
                float received = Time.realtimeSinceStartup + 1;
                while (ordinaryBlocks.Count == prior && Time.realtimeSinceStartup < received)
                    yield return null;
                Need(ordinaryBlocks.Count > prior, "G08 native BlockAttack was not reached");
                var block = ordinaryBlocks.Last();
                Need(
                    (bool)block["accepted"] && (float)block["remainingDamage"] < (float)block["beforeDamage"],
                    "G08 shield did not block damage"
                );
                Need(
                    parry
                        ? ((float)block["timer"] >= 0 && (float)block["timer"] < .25f)
                        : (float)block["timer"] >= .25f,
                    "G08 block/parry timing mismatch"
                );
                yield return new WaitForSecondsRealtime(.15f);
                if (parry)
                    Need(ordinaryTarget.IsStaggering(), "G08 parry did not stagger attacker");
                block["parry"] = parry;
            }
            proof["blocks"] = JArray.FromObject(ordinaryBlocks);
            player.AddStamina(player.GetMaxStamina());
            var beforePosition = player.transform.position;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Space));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            float dodgeReady = Time.realtimeSinceStartup + 1;
            while (!player.InDodge() && Time.realtimeSinceStartup < dodgeReady)
                yield return null;
            Need(player.InDodge(), "G08 native dodge animation not observed");
            proof["dodgeObserved"] = true;
            yield return new WaitForSecondsRealtime(.5f);
            proof["dodgeTravel"] = Vector3.Distance(beforePosition, player.transform.position);
            Need((float)proof["dodgeTravel"] > .1f, "G08 dodge did not move actor");
            Need(deck.IsSyntheticActive, "G08 ordinary combat stopped music");
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            observer.UnpatchSelf();
            ordinaryTarget = null;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            if (root != null)
                ZNetScene.instance.Destroy(root);
            foreach (var item in gear)
            {
                player.UnequipItem(item, false);
                player.GetInventory().RemoveItem(item);
            }
            player.SetGodMode(true);
            Freeze(player, false);
            InputSystem.settings.backgroundBehavior = input;
            settings.Effect.CreatureDamage.Value = damage;
            settings.MusicOnly.Value = musicOnly;
            settings.Push[1].Value = push;
        }
    }
}
