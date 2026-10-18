using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal struct MusicTarget
    {
        public Character Character;
        public int Id;
        public long EventId;
        public float Edge;
        public Vector3 Center;
    }

    internal sealed class MusicLiveStrike
    {
        public double Time;
        public long EventId;
        public float Strength;
        public MusicRange Loudest;
        public Vector3 Origin;
        public List<MusicTarget> Targets;
        public readonly HashSet<int> Hit = new HashSet<int>();
    }

    internal sealed class MusicReach
    {
        private readonly Func<float, float, double> arrival;
        public readonly double Lifetime;
        public readonly bool Anchored;

        private MusicReach(Func<float, float, double> arrival, double lifetime, bool anchored = false)
        {
            this.arrival = arrival;
            Lifetime = lifetime;
            Anchored = anchored;
        }

        public double Arrival(float edge, float radius) => arrival(Math.Max(0f, edge), radius);

        public static MusicReach Flight(
            double seconds,
            double lifetime
        ) => new MusicReach((edge, radius) => seconds, lifetime);

        public static MusicReach AnchoredFlight(
            double seconds,
            double lifetime
        ) => new MusicReach((edge, radius) => seconds, lifetime, true);

        public static MusicReach Front(double travel, double lifetime) => new MusicReach(
            (edge, radius) => radius <= 0f
                ? 0
                : travel * (1 - Math.Pow(Math.Max(0, 1 - edge / radius), 1.0 / 3)),
            lifetime
        );
    }

    internal sealed class MusicStrikeTracker
    {
        private long sequence;
        internal Func<long> NextEvent;
        public readonly List<MusicLiveStrike> Strikes = new List<MusicLiveStrike>();
        public readonly List<MusicLiveStrike> Fresh = new List<MusicLiveStrike>();

        public void Reset()
        {
            Strikes.Clear();
            Fresh.Clear();
        }

        public void Update(
            double time,
            List<MusicStrike> fresh,
            List<MusicTarget> targets,
            float radius,
            MusicReach reach,
            Vector3 center,
            Action<MusicTarget, float> hit
        )
        {
            Fresh.Clear();
            foreach (var strike in fresh)
                Fresh.Add(new MusicLiveStrike
                {
                    EventId = NextEvent != null ? NextEvent() : ++sequence,
                    Time = strike.Time,
                    Strength = strike.Strength,
                    Loudest = strike.Loudest,
                    Origin = center,
                    Targets = reach.Anchored ? new List<MusicTarget>(targets) : null
                });
            Strikes.AddRange(Fresh);
            foreach (var strike in Strikes)
                foreach (var target in strike.Targets ?? targets)
                {
                    if (strike.Hit.Contains(target.Id)
                        || target.Edge > radius
                        || time - strike.Time < reach.Arrival(target.Edge, radius))
                        continue;
                    strike.Hit.Add(target.Id);
                    var eventTarget = target;
                    eventTarget.EventId = strike.EventId;
                    hit(eventTarget, strike.Strength);
                }
            Strikes.RemoveAll(strike => time - strike.Time > reach.Lifetime);
        }
    }

    internal static class MusicEffectTargets
    {
        private static readonly List<Character> characters = new List<Character>();

        public static void Collect(Player owner, Vector3 center, List<MusicTarget> targets)
        {
            targets.Clear();
            characters.Clear();
            characters.AddRange(Character.GetAllCharacters());
            foreach (var character in characters)
            {
                if (!IsHostile(character, owner))
                    continue;
                var offset = character.transform.position - center;
                offset.y = 0f;
                targets.Add(new MusicTarget
                {
                    Character = character,
                    Id = character.GetInstanceID(),
                    Edge = offset.magnitude - character.GetRadius(),
                    Center = character.GetCenterPoint()
                });
            }
        }

        public static bool IsHostile(Character target, Player owner)
        {
            return target != null && owner != null && !target.IsPlayer() && !target.IsTamed() && !target.IsDead()
            && target.GetBaseAI() is MonsterAI
            && (target.GetFaction() == Character.Faction.Boss || target.IsMonsterFaction(Time.time))
            && BaseAI.IsEnemy(target, owner);
        }
    }

    internal static class MusicEffectDamage
    {
        public static void Apply(Character target, Player owner, Vector3 center, float damage, float push)
        {
            if (!MusicEffectTargets.IsHostile(target, owner) || !target.IsOwner())
                return;
            Vector3 direction = target.transform.position - center;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.forward;
            direction.Normalize();
            var hit = new HitData();
            hit.m_damage.m_blunt = damage;
            hit.m_point = target.GetCenterPoint();
            hit.m_dir = direction;
            hit.m_pushForce = 0f;
            hit.m_staggerMultiplier = 0f;
            hit.m_skill = Skills.SkillType.None;
            hit.m_skillRaiseAmount = 0f;
            hit.m_hitType = HitData.HitType.Impact;
            hit.SetAttacker(owner);
            BoomboxCombatDamage.Apply(target, hit);
            if (push > 0f && CanPush(target, owner) && !target.InAttack() && !target.InDodge())
                target.GetComponent<Rigidbody>().AddForce(direction * push, ForceMode.Impulse);
        }

        public static bool CanPush(Character target, Player owner)
        {
            if (!MusicEffectTargets.IsHostile(target, owner) || target.GetFaction() == Character.Faction.Boss)
                return false;
            var body = target.GetComponent<Rigidbody>();
            var ownerBody = owner.GetComponent<Rigidbody>();
            var collider = target.GetComponent<CapsuleCollider>();
            var ownerCollider = owner.GetComponent<CapsuleCollider>();
            if (body == null || body.isKinematic || ownerBody == null || body.mass > ownerBody.mass || collider == null
            || ownerCollider == null)
                return false;
            var size = collider.bounds.size;
            var ownerSize = ownerCollider.bounds.size;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float ownerLargest = Mathf.Max(ownerSize.x, Mathf.Max(ownerSize.y, ownerSize.z));
            return largest <= ownerLargest * 1.25f
                && size.x * size.y * size.z <= ownerSize.x * ownerSize.y * ownerSize.z * 1.5f;
        }
    }
}
