using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxBoatDrag
    {
        private static readonly AccessTools.FieldRef<Ship, float> LastDepth = AccessTools.FieldRefAccess<Ship, float>(
            "m_lastDepth"
        );
        private static readonly float[] losses = new float[BoomboxBoatHelp.MaximumSteps];
        private static Ship ship;
        private static Rigidbody body;
        private static int count;
        private static int next;

        internal static void Step(Ship current)
        {
            if (current != ship)
            {
                ship = current;
                body = current == null ? null : current.GetComponent<Rigidbody>();
                count = 0;
                next = 0;
            }
            if (ship == null || body == null)
                return;
            float along = Vector3.Dot(body.linearVelocity, ship.transform.forward);
            float immersion = Mathf.Clamp01(Mathf.Abs(LastDepth(ship)) / ship.m_forceDistance);
            losses[next] = ship.m_dampingForward * along * along * immersion;
            next = (next + 1) % losses.Length;
            count = Mathf.Min(count + 1, losses.Length);
        }

        internal static float Take(Ship current)
        {
            if (current != ship)
                return 0f;
            float sum = 0f;
            for (int i = 1; i <= count; i++)
                sum += losses[(next - i + losses.Length) % losses.Length];
            count = 0;
            return sum;
        }
    }
}
