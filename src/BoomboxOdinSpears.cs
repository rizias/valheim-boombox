using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxOdinSpears
    {
        internal const float Delay = .2f;
        private const float FadeOut = .3f;
        private static readonly List<Flight> flights = new List<Flight>();
        private static readonly List<Ghost> ghosts = new List<Ghost>();
        private static Projectile hitting;

        private struct Sample
        {
            internal float Time;
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        private sealed class Flight
        {
            internal Projectile Projectile;
            internal ItemDrop.ItemData Item;
            internal readonly List<Sample> Path = new List<Sample>();
        }

        private sealed class Ghost
        {
            internal Flight Flight;
            internal Character Target;
            internal HitData Hit;
            internal Vector3 LocalPoint;
            internal float HitTime;
            internal float Resolved = -1f;
            internal bool Struck;
            internal BoomboxGhostModel Model;
            internal Quaternion LocalRotation;
        }

        internal static void Thrown(Projectile projectile, Character owner, ItemDrop.ItemData item)
        {
            if (projectile == null || owner == null || owner != Player.m_localPlayer || item == null
            || item.m_shared.m_skillType != Skills.SkillType.Spears)
                return;
            if (BoomboxEnchantWeapons.Melee.Item != item || BoomboxEnchantWeapons.Melee.Value < 1f)
                return;
            var flight = new Flight { Projectile = projectile, Item = item };
            Record(flight);
            flights.Add(flight);
        }

        internal static void Hitting(Projectile projectile) => hitting = projectile;

        internal static void Damaging(Character target, HitData hit)
        {
            if (hitting == null || target == null || hit == null)
                return;
            var flight = flights.Find(f => f.Projectile == hitting);
            if (flight == null || target == Player.m_localPlayer)
                return;
            flights.Remove(flight);
            Record(flight);
            var ghost = new Ghost
            {
                Flight = flight,
                Target = target,
                Hit = hit.Clone(),
                LocalPoint = target.transform.InverseTransformPoint(hit.m_point),
                HitTime = Time.time
            };
            ghosts.Add(ghost);
            try
            {
                ghost.Model = new BoomboxGhostModel(
                    flight.Projectile.gameObject,
                    flight.Projectile.transform.position,
                    "BoomboxGhostSpear",
                    true
                );
            }
            catch (Exception error)
            {
                ghost.Model = null;
                BoomboxPlugin.Instance?.LogError(BoomboxText.Get("text.ghost_spear_is_not_shown_the_ghost_strike_still_works")
                    + error.Message);
            }
            if (ghost.Model != null)
                ghost.LocalRotation = Quaternion.Inverse(flight.Projectile.transform.rotation);
        }

        internal static void Update(BoomboxGhostEffects effects)
        {
            float now = Time.time;
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                if (flights[i].Projectile == null)
                    flights.RemoveAt(i);
                else
                    Record(flights[i]);
            }
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                var ghost = ghosts[i];
                if (ghost.Resolved < 0f && now - ghost.HitTime >= Delay)
                    Resolve(ghost, now, effects);
                float opacity = ghost.Resolved < 0f
                    ? BoomboxGhostModel.Opacity
                    : BoomboxGhostModel.Opacity * Mathf.Clamp01(1f - (now - ghost.Resolved) / FadeOut);
                if (ghost.Model != null)
                {
                    Place(ghost, now);
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
            hitting = null;
            flights.Clear();
            foreach (var ghost in ghosts)
                ghost.Model?.Destroy();
            ghosts.Clear();
        }

        private static void Record(Flight flight)
        {
            var transform = flight.Projectile.transform;
            flight.Path.Add(
                new Sample { Time = Time.time, Position = transform.position, Rotation = transform.rotation }
            );
        }

        private static void Resolve(Ghost ghost, float now, BoomboxGhostEffects effects)
        {
            ghost.Resolved = now;
            if (ghost.Target == null || ghost.Target.IsDead())
                return;
            var copy = ghost.Hit.Clone();
            copy.m_point = ghost.Target.transform.TransformPoint(ghost.LocalPoint);
            ghost.Target.Damage(copy);
            effects?.Burst(copy.m_point);
            ghost.Struck = true;
            if (BoomboxEnchantWeapons.Melee.Item == ghost.Flight.Item)
                BoomboxEnchantWeapons.Melee.Spend();
        }

        private static void Place(Ghost ghost, float now)
        {
            var path = ghost.Flight.Path;
            float replay = Mathf.Min(now - Delay, ghost.HitTime);
            int index = path.FindLastIndex(s => s.Time <= replay);
            Vector3 position;
            Quaternion rotation;
            if (index < 0)
            {
                position = path[0].Position;
                rotation = path[0].Rotation;
            }
            else if (index >= path.Count - 1)
            {
                position = path[path.Count - 1].Position;
                rotation = path[path.Count - 1].Rotation;
            }
            else
            {
                var a = path[index];
                var b = path[index + 1];
                float t = b.Time > a.Time ? (replay - a.Time) / (b.Time - a.Time) : 1f;
                position = Vector3.Lerp(a.Position, b.Position, t);
                rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            }
            if (now - ghost.HitTime >= Delay && ghost.Target != null)
                position += ghost.Target.transform.TransformPoint(ghost.LocalPoint) - path[path.Count - 1].Position;
            ghost.Model.Root.SetPositionAndRotation(position, rotation * ghost.LocalRotation);
        }
    }
}
