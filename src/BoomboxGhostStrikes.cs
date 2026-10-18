using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxGhostStrikes
    {
        internal const float Delay = .5f;
        private const float SwingAngle = 100f;
        private const float FadeIn = .1f;
        private const float FadeOut = .6f;
        private const float Scale = 1.2f;
        private const float GlowOpacity = .75f;
        private const float CoreOpacity = 1f;
        private const float Fizzle = .1f;
        private static readonly FieldInfo RightItemInstance = AccessTools.Field(
            typeof(VisEquipment),
            "m_rightItemInstance"
        );
        private static readonly FieldInfo LeftItemInstance = AccessTools.Field(
            typeof(VisEquipment),
            "m_leftItemInstance"
        );
        private static readonly FieldInfo SecondaryAttack = AccessTools.Field(
            typeof(Humanoid),
            "m_currentAttackIsSecondary"
        );
        private static readonly List<Ghost> ghosts = new List<Ghost>();
        private static Ghost armed;

        private sealed class Ghost
        {
            internal ItemDrop.ItemData Item;
            internal readonly List<(
                Component Target,
                Collider Collider,
                HitData Hit
            )> Hits = new List<(
                Component,
                Collider,
                HitData
            )>();
            internal float Start;
            internal float Resolved = -1f;
            internal bool Struck;
            internal BoomboxGhostModel Model;
            internal Vector3 Axis;
            internal float Sign;
        }

        internal static bool IsTool(ItemDrop.ItemData weapon) => weapon != null
            && (weapon.m_shared.m_skillType == Skills.SkillType.Axes || weapon.m_shared.m_skillType == Skills.SkillType.Pickaxes);

        internal static void Begin(Attack attack, Humanoid character, ItemDrop.ItemData weapon)
        {
            armed = null;
            if (character == null || character != Player.m_localPlayer)
                return;
            float charge = BoomboxEnchantWeapons.Melee.For(weapon);
            if (!IsTool(weapon) || charge < 1f)
                return;
            bool overhead = attack != null
                && attack.m_attackType == Attack.AttackType.Vertical
                || (bool)SecondaryAttack.GetValue(character);
            armed = new Ghost { Item = weapon, Axis = overhead ? character.transform.right : Vector3.up };
        }

        internal static void Capture(Component target, HitData hit)
        {
            if (armed == null || hit == null || !BoomboxEnchantResources.IsResource(target))
                return;
            armed.Hits.Add((target, hit.m_hitCollider, hit.Clone()));
        }

        internal static void End(Player player)
        {
            var ghost = armed;
            armed = null;
            if (ghost == null)
                return;
            if (ghost.Hits.Count == 0 || !ghost.Hits.Exists(h => BoomboxEnchantResources.Intact(h.Target, h.Collider)))
            {
                Log(BoomboxText.Get("text.the_target_is_neither_ore_nor_a_tree"));
                return;
            }
            ghost.Start = Time.time;
            ghosts.Add(ghost);
            try
            {
                Shape(ghost, player);
            }
            catch (Exception error)
            {
                ghost.Model?.Destroy();
                ghost.Model = null;
                BoomboxPlugin.Instance?.LogError(BoomboxText.Get("text.ghost_tool_is_not_shown_the_ghost_strike_still_works")
                    + error.Message);
            }
        }

        internal static void Update(BoomboxGhostEffects effects)
        {
            float now = Time.time;
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var ghost = ghosts[i];
                float age = now - ghost.Start;
                if (ghost.Resolved < 0f && age >= Delay)
                    Resolve(ghost, now, effects);
                float opacity = Opacity(ghost, age, now);
                if (ghost.Model != null)
                {
                    float progress = Mathf.Clamp01(age / Delay);
                    ghost.Model.Root.rotation = Quaternion.AngleAxis(
                        ghost.Sign * SwingAngle * (1f - progress * progress),
                        ghost.Axis
                    );
                    ghost.Model.Show(opacity);
                    effects?.Glow(ghost.Model.Bounds, opacity / BoomboxGhostModel.Opacity);
                }
                if (ghost.Resolved >= 0f && opacity <= 0f)
                {
                    ghost.Model?.Destroy();
                    ghosts.RemoveAt(i);
                }
            }
        }

        internal static void Reset()
        {
            armed = null;
            foreach (var ghost in ghosts)
                ghost.Model?.Destroy();
            ghosts.Clear();
        }

        private static void Resolve(Ghost ghost, float now, BoomboxGhostEffects effects)
        {
            ghost.Resolved = now;
            foreach (var (target, collider, hit) in ghost.Hits)
            {
                if (!BoomboxEnchantResources.Intact(target, collider))
                    continue;
                var copy = hit.Clone();
                ((IDestructible)target).Damage(copy);
                effects?.Burst(hit.m_point);
                ghost.Struck = true;
            }
            if (ghost.Struck && BoomboxEnchantWeapons.Melee.Item == ghost.Item)
                BoomboxEnchantWeapons.Melee.Spend();
        }

        private static void Log(
            string reason
        ) => BoomboxPlugin.Instance?.LogInfo(BoomboxText.Get("text.ghost_tool_did_not_appear") + reason);

        private static float Opacity(Ghost ghost, float age, float now)
        {
            if (ghost.Resolved < 0f)
                return BoomboxGhostModel.Opacity * Mathf.Clamp01(age / FadeIn);
            float fade = ghost.Struck ? FadeOut : Fizzle;
            return BoomboxGhostModel.Opacity * Mathf.Clamp01(1f - (now - ghost.Resolved) / fade);
        }

        private static void Shape(Ghost ghost, Player player)
        {
            var equipment = player.GetVisEquipment();
            bool left = ghost.Item == player.LeftItem;
            var instance = (left ? LeftItemInstance : RightItemInstance).GetValue(equipment) as GameObject;
            var hand = left ? equipment.m_leftHand : equipment.m_rightHand;
            if (instance == null || hand == null)
            {
                BoomboxPlugin.Instance?.LogInfo(BoomboxText.Get("text.ghost_tool_has_no_visible_model")
                    + ghost.Item.m_shared.m_name);
                return;
            }
            ghost.Model = new BoomboxGhostModel(
                instance,
                hand.position,
                "BoomboxGhostTool",
                false,
                GlowOpacity,
                CoreOpacity
            );
            if (ghost.Model.Empty)
            {
                BoomboxPlugin.Instance?.LogInfo(BoomboxText.Get("text.ghost_tool_has_no_visible_model")
                    + ghost.Item.m_shared.m_name);
                return;
            }
            var head = ghost.Model.Center - hand.position;
            var raised = Quaternion.AngleAxis(SwingAngle, ghost.Axis) * head;
            var lowered = Quaternion.AngleAxis(-SwingAngle, ghost.Axis) * head;
            bool vertical = ghost.Axis != Vector3.up;
            ghost.Sign = vertical
                ? (raised.y >= lowered.y
                    ? 1f
                    : -1f)
                : (Vector3.Dot(raised, player.transform.right) >= Vector3.Dot(lowered, player.transform.right)
                    ? 1f
                    : -1f);
            var point = ghost.Hits[0].Hit.m_point;
            ghost.Model.Root.position = point - (point - hand.position) * Scale;
            ghost.Model.Root.localScale = Vector3.one * Scale;
            BoomboxPlugin.Instance?.LogInfo(BoomboxText.Get("text.ghost_tool_appeared") + ghost.Item.m_shared.m_name);
        }
    }
}
