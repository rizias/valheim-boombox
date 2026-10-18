using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxWorldImpulse : MonoBehaviour
    {
        private sealed class Target
        {
            public Rigidbody Body;
            public Collider[] Colliders;
            public float PreviousDistance;
            public bool Hit;
        }

        public int HitCount
        {
            get;
            private set;
        }
        public int CandidateCount => targets.Count;
        private static readonly Dictionary<int, float> lastPushTime = new Dictionary<int, float>();
        private readonly List<Target> targets = new List<Target>();
        private float multiplier;
        private float pressure;
        private float maximumRadius;
        private Vector3 origin;

        public void Initialize(Vector3 centre, float radius, float strength)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            origin = centre;
            multiplier = BoomboxSettings.Nonnegative(settings.EnvironmentRadius.Value);
            pressure = BoomboxSettings.Nonnegative(settings.EnvironmentPressure.Value) * Mathf.Clamp01(strength);
            maximumRadius = radius * multiplier;
            PruneCooldowns();
            if (!settings.LocalGameplayEnabled || !settings.PushEnvironment.Value || pressure <= 0f || multiplier <= 0f)
                return;
            var bodies = new HashSet<Rigidbody>();
            foreach (var collider in Physics.OverlapSphere(origin, maximumRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = collider.attachedRigidbody;
                if (!IsPushable(body) || !IsOwner(body) || !bodies.Add(body))
                    continue;
                float maximumMass = BoomboxSettings.Nonnegative(settings.EnvironmentMaxMass.Value);
                if (maximumMass > 0f && body.mass > maximumMass)
                    continue;
                var colliders = body.GetComponentsInChildren<Collider>().Where(
                    c => c.enabled && !c.isTrigger && c.attachedRigidbody == body
                ).ToArray();
                if (colliders.Length == 0)
                    continue;
                var target = new Target { Body = body, Colliders = colliders };
                target.PreviousDistance = Distance(target);
                targets.Add(target);
            }
            targets.Sort((a, b) => a.PreviousDistance.CompareTo(b.PreviousDistance));
            int maximum = settings.EnvironmentMaxBodies.Value;
            if (maximum > 0 && targets.Count > maximum)
                targets.RemoveRange(maximum, targets.Count - maximum);
        }

        public void ApplyFront(float previousRadius, float radius)
        {
            if (!BoomboxPlugin.Instance.Settings.LocalGameplayEnabled
                || !BoomboxPlugin.Instance.Settings.PushEnvironment.Value)
                return;
            float previous = previousRadius * multiplier;
            float current = radius * multiplier;
            foreach (var target in targets)
            {
                if (target.Hit || !IsPushable(target.Body) || !IsOwner(target.Body))
                    continue;
                float distance = Distance(target);
                bool crossed = distance <= current && (target.PreviousDistance > previous || previous <= 0f);
                target.PreviousDistance = distance;
                if (!crossed)
                    continue;
                target.Hit = true;
                if (OnCooldown(target.Body))
                    continue;
                Vector3 direction = target.Body.worldCenterOfMass - origin;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.000001f)
                    continue;
                direction.Normalize();
                Bounds bounds = target.Colliders.First(c => c != null).bounds;
                foreach (var collider in target.Colliders)
                    if (collider != null)
                        bounds.Encapsulate(collider.bounds);
                float area = bounds.size.y
                    * (Mathf.Abs(direction.x) * bounds.size.z + Mathf.Abs(direction.z) * bounds.size.x);
                float falloff = maximumRadius > 0f ? Mathf.Clamp01(1f - distance / maximumRadius) : 0f;
                float impulse = pressure * area * falloff;
                if (impulse <= 0f)
                    continue;
                float maximumSpeedChange = BoomboxSettings.Nonnegative(
                    BoomboxPlugin.Instance.Settings.EnvironmentMaxSpeedChange.Value
                );
                float speedChange = Mathf.Min(impulse / target.Body.mass, maximumSpeedChange);
                if (speedChange <= 0f)
                    continue;
                WaveImpulseProtection.Mark(target.Body);
                target.Body.WakeUp();
                target.Body.AddForce(direction * speedChange * target.Body.mass, ForceMode.Impulse);
                MarkPushed(target.Body);
                HitCount++;
            }
        }

        private float Distance(Target target)
        {
            float distance = float.PositiveInfinity;
            foreach (var collider in target.Colliders)
                if (collider != null && collider.enabled)
                    distance = Mathf.Min(distance, Vector3.Distance(origin, collider.ClosestPoint(origin)));
            return distance;
        }

        internal static Rigidbody ResolveBody(Rigidbody body)
        {
            if (body == null)
                return null;
            var ship = body.GetComponentInParent<Ship>();
            if (ship != null)
                return ship.GetComponent<Rigidbody>();
            var cart = body.GetComponentInParent<Vagon>();
            return cart != null ? cart.GetComponent<Rigidbody>() : body;
        }

        internal static bool IsPushable(Rigidbody body)
        {
            if (body == null || body.isKinematic
            || (body.constraints & RigidbodyConstraints.FreezePosition) == RigidbodyConstraints.FreezePosition)
                return false;
            if (body.GetComponent<ItemDrop>() == null || body.GetComponent<TreeLog>() != null)
                return false;
            if (body.GetComponent<Ship>() != null || body.GetComponent<Vagon>() != null)
                return false;
            if (body.GetComponentInParent<Piece>() != null || body.GetComponentInParent<WearNTear>() != null)
                return false;
            return true;
        }

        private static bool IsOwner(Rigidbody body)
        {
            var view = body.GetComponentInParent<ZNetView>();
            return view == null || view.IsValid() && view.IsOwner();
        }

        private static bool OnCooldown(Rigidbody body)
        {
            float cooldown = BoomboxSettings.Nonnegative(BoomboxPlugin.Instance.Settings.EnvironmentPushCooldown.Value);
            return cooldown > 0f
                && lastPushTime.TryGetValue(body.GetInstanceID(), out float last)
                && Time.time - last < cooldown;
        }

        private static void MarkPushed(Rigidbody body) => lastPushTime[body.GetInstanceID()] = Time.time;

        private static void PruneCooldowns()
        {
            if (lastPushTime.Count == 0)
                return;
            float horizon = BoomboxSettings.PositiveOr(
                BoomboxPlugin.Instance.Settings.EnvironmentPushCooldown.Value,
                1f
            );
            foreach (int key in lastPushTime.Where(pair => Time.time - pair.Value > horizon).Select(pair => pair.Key).ToList())
                lastPushTime.Remove(key);
        }
    }

    public sealed class WaveImpulseProtection : MonoBehaviour
    {
        private Rigidbody body;
        public bool Active
        {
            get;
            private set;
        }
        public static void Mark(Rigidbody value)
        {
            if (!BoomboxWorldImpulse.IsPushable(value))
                return;
            var marker = value.GetComponent<WaveImpulseProtection>()
                ?? value.gameObject.AddComponent<WaveImpulseProtection>();
            marker.body = value;
            marker.Active = true;
        }
        public static bool IsMarked(
            Rigidbody value
        ) => value != null && value.GetComponent<WaveImpulseProtection>() != null
        && value.GetComponent<WaveImpulseProtection>().Active;
        private void FixedUpdate()
        {
            if (body == null || body.IsSleeping())
            {
                Active = false;
                Destroy(this);
            }
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (Active)
                Mark(BoomboxWorldImpulse.ResolveBody(collision.rigidbody));
        }
    }

    [HarmonyPatch(typeof(ImpactEffect), "OnCollisionEnter")]
    internal static class BoomboxProtectedImpact
    {
        private static bool Prefix(ImpactEffect __instance, Collision __0, out bool __state)
        {
            __state = __instance.m_damageToSelf;
            var source = BoomboxWorldImpulse.ResolveBody(__instance.GetComponent<Rigidbody>());
            var other = BoomboxWorldImpulse.ResolveBody(__0.rigidbody);
            bool marked = WaveImpulseProtection.IsMarked(source) || WaveImpulseProtection.IsMarked(other);
            if (!marked)
                return true;
            WaveImpulseProtection.Mark(source);
            WaveImpulseProtection.Mark(other);
            if (__instance.GetComponentInParent<WearNTear>() != null
                || __instance.GetComponentInParent<TreeBase>() != null)
                __instance.m_damageToSelf = false;
            var collider = __0.collider;
            bool protectedTarget = collider.GetComponentInParent<WearNTear>() != null
                || collider.GetComponentInParent<TreeBase>() != null;
            return !protectedTarget;
        }

        private static System.Exception Finalizer(ImpactEffect __instance, bool __state, System.Exception __exception)
        {
            if (__instance != null)
                __instance.m_damageToSelf = __state;
            return __exception;
        }
    }
}
