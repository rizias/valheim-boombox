using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxEnchantVisuals
    {
        private const float TrailPerMetre = 10f;
        private const float TrailSparkSize = .04f;
        private const float TrailSparkLifetime = .3f;
        private const float HandSize = .1f;
        private const float HitSparks = 12f;
        private const float HitSpeed = 1.2f;
        private static readonly FieldInfo LeftItemInstance = AccessTools.Field(
            typeof(VisEquipment),
            "m_leftItemInstance"
        );
        private static readonly FieldInfo RightItemInstance = AccessTools.Field(
            typeof(VisEquipment),
            "m_rightItemInstance"
        );
        private static bool trailFailed;
        private readonly GameObject root;
        private readonly BoomboxEnchantStream handStream;
        private readonly BoomboxEnchantStream weaponStream;
        private readonly BoomboxEnchantGlow handGlow;
        private readonly BoomboxEnchantGlow weaponGlow;
        private readonly BoomboxEnchantItem handItem = new BoomboxEnchantItem();
        private readonly BoomboxEnchantItem weaponItem = new BoomboxEnchantItem();
        private readonly BoomboxEnchantSkeleton skeleton = new BoomboxEnchantSkeleton();
        private bool shown;

        internal BoomboxEnchantVisuals()
        {
            root = new GameObject("BoomboxEnchantParticles");
            try
            {
                handStream = new BoomboxEnchantStream(root.transform, "Hand");
                weaponStream = new BoomboxEnchantStream(root.transform, "Weapon");
                handGlow = new BoomboxEnchantGlow(root.transform, "Hand");
                weaponGlow = new BoomboxEnchantGlow(root.transform, "Weapon");
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        internal void Present(AudioDeck deck, Player player, BoomboxEnchantFrame frame)
        {
            shown = true;
            var equipment = player.GetVisEquipment();
            var weapon = player.GetCurrentWeapon();
            var hand = handItem.Bounds(
                LeftInstance(equipment),
                equipment == null
                    ? null
                    : equipment.m_leftHand,
                player,
                HandSize
            );
            bool weaponLeft = weapon != null && weapon == player.LeftItem;
            var weaponBounds = weaponItem.Bounds(
                weaponLeft
                    ? LeftInstance(equipment)
                    : weapon != null && weapon == player.RightItem
                        ? RightInstance(equipment)
                        : null,
                equipment == null
                    ? null
                    : weaponLeft
                        ? equipment.m_leftHand
                        : equipment.m_rightHand,
                player,
                HandSize
            );
            var model = deck.Visual.GetComponent<VikingBoomboxVisualState>();
            float now = Time.time;
            if (frame.LaunchHand)
                handStream.Launch(now);
            if (frame.LaunchWeapon)
                weaponStream.Launch(now);
            handStream.Present(
                model.Left.position,
                model.Right.position,
                BoomboxEnchantRoute.For(skeleton.Resolve(player), player, false),
                hand.center,
                now
            );
            weaponStream.Present(
                model.Left.position,
                model.Right.position,
                BoomboxEnchantRoute.For(skeleton.Resolve(player), player, !weaponLeft),
                weaponBounds.center,
                now
            );
            if (frame.HitBurst.HasValue)
                weaponStream.Burst(frame.HitBurst.Value, HitSparks, HitSpeed);
            handGlow.Present(hand, frame.HandCharge);
            weaponGlow.Present(weaponBounds, frame.WeaponCharge);
        }

        internal void Clear()
        {
            if (!shown)
                return;
            shown = false;
            handStream.Clear();
            weaponStream.Clear();
            handGlow.Clear();
            weaponGlow.Clear();
        }

        internal void Destroy()
        {
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }

        internal static void Trail(GameObject projectile, float charge, float speed)
        {
            if (BoomboxPlugin.Instance?.Settings?.DrawEnchantment != true)
                return;
            if (trailFailed)
                return;
            try
            {
                float perMetre = TrailPerMetre * BoomboxParticleLook.Amount;
                var trail = BoomboxEnchantSystems.Create(
                    projectile.transform,
                    "BoomboxEnchantTrail",
                    ParticleSystemSimulationSpace.World,
                    Mathf.Max(1, Mathf.CeilToInt(perMetre * speed * TrailSparkLifetime)),
                    1f,
                    BoomboxParticleAssets.Additive("dot", 1f)
                );
                var main = trail.main;
                trail.gameObject.AddComponent<BoomboxEnchantVisibility>();
                main.startLifetime = TrailSparkLifetime;
                main.startSize = TrailSparkSize * charge;
                var emission = trail.emission;
                emission.rateOverDistance = perMetre;
                BoomboxEnchantSystems.Fade(
                    trail,
                    new[] {
                    new GradientColorKey(BoomboxEnchantSystems.Blue, 0f),
                    new GradientColorKey(BoomboxEnchantSystems.Blue, 1f)
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
                );
            }
            catch (Exception error)
            {
                trailFailed = true;
                BoomboxPlugin.Instance?.LogError(BoomboxText.Get("text.charged_arrow_trail_is_unavailable") + error.Message);
            }
        }

        private static GameObject LeftInstance(VisEquipment equipment) => equipment == null
            ? null
            : LeftItemInstance.GetValue(equipment) as GameObject;

        private static GameObject RightInstance(VisEquipment equipment) => equipment == null
            ? null
            : RightItemInstance.GetValue(equipment) as GameObject;
    }
}
