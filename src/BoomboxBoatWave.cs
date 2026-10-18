using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxBoatWave
    {
        private const float MembranesRise = 1f;
        private const float MiddleRise = 1.5f;
        private const float Arrival = .35f;
        private const float SailSparks = 24f;
        private const float SailSpeed = 1.5f;
        private const float RingStart = .6f;
        private const float RingEnd = 4f;
        private const float RingLifetime = .5f;
        private const float RingGap = .1f;
        private const float RingAlpha = .9f;
        private readonly ParticleSystem ringCore;
        private readonly ParticleSystem ringGlow;
        private float secondRingAt = -1f;
        private Vector3 ringPoint;
        private readonly GameObject root;
        private readonly BoomboxEnchantStream stream;
        private float burstAt = -1f;
        private bool shown;

        internal BoomboxBoatWave()
        {
            root = new GameObject("BoomboxBoatParticles");
            try
            {
                stream = new BoomboxEnchantStream(root.transform, "Boat");
                ringCore = Ring("BoatRingCore", BoomboxParticleAssets.Core("ring"), BoomboxEnchantSystems.Pale);
                ringGlow = Ring(
                    "BoatRingGlow",
                    BoomboxParticleAssets.Additive("ring", .5f),
                    BoomboxEnchantSystems.Blue
                );
                ringGlow.GetComponent<ParticleSystemRenderer>()
                    .sortingFudge = ringCore.GetComponent<ParticleSystemRenderer>().sortingFudge - 10f;
            }
            catch
            {
                Object.Destroy(root);
                throw;
            }
        }

        internal static Vector3 SailPoint(Ship ship)
        {
            if (Visible(ship.m_sailObject, out var sail))
                return sail.center;
            return Visible(ship.m_mastObject, out var mast) ? mast.center : ship.transform.position;
        }

        private static bool Visible(GameObject source, out Bounds bounds)
        {
            bool found = false;
            bounds = new Bounds();
            if (source == null)
                return false;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (found)
                    bounds.Encapsulate(renderer.bounds);
                else
                    bounds = renderer.bounds;
                found = true;
            }
            return found;
        }

        internal void Present(AudioDeck deck, Ship ship, bool pushed)
        {
            shown = true;
            BoomboxParticleAssets.Refresh();
            var model = deck.Visual.GetComponent<VikingBoomboxVisualState>();
            var left = model.Left.position;
            var right = model.Right.position;
            var middle = (left + right) * .5f;
            var sail = SailPoint(ship);
            float now = Time.time;
            if (pushed)
            {
                stream.Launch(now);
                burstAt = now + Arrival;
            }
            var route = new BoomboxEnchantRoute
            {
                Back = middle + Vector3.up * MembranesRise,
                Shoulder = (middle + sail) * .5f + Vector3.up * MiddleRise
            };
            stream.Present(left, right, route, sail, now);
            var facing = Quaternion.LookRotation(ship.transform.forward);
            ringCore.transform.rotation = facing;
            ringGlow.transform.rotation = facing;
            if (secondRingAt >= 0f && now >= secondRingAt)
            {
                secondRingAt = -1f;
                EmitRing(ringPoint);
            }
            if (burstAt < 0f || now < burstAt)
                return;
            burstAt = -1f;
            stream.Burst(sail, SailSparks, SailSpeed);
            ringPoint = sail;
            EmitRing(sail);
            secondRingAt = now + RingGap;
        }

        private void EmitRing(Vector3 point)
        {
            foreach (var system in new[] { ringCore, ringGlow })
                system.Emit(
                    new ParticleSystem.EmitParams
                    {
                        position = point,
                        startSize = RingEnd,
                        startLifetime = RingLifetime,
                        applyShapeToPosition = false
                    },
                    1
                );
        }

        private ParticleSystem Ring(string name, Material material, Color color)
        {
            var system = BoomboxEnchantSystems.Create(
                root.transform,
                name,
                ParticleSystemSimulationSpace.World,
                4,
                1f,
                material
            );
            system.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, RingStart / RingEnd, 1f, 1f));
            BoomboxEnchantSystems.Fade(
                system,
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(RingAlpha, 0f), new GradientAlphaKey(0f, 1f) }
            );
            return system;
        }

        internal void Clear()
        {
            if (!shown)
                return;
            shown = false;
            burstAt = -1f;
            secondRingAt = -1f;
            stream.Clear();
            ringCore.Clear();
            ringGlow.Clear();
        }

        internal void Destroy() => Object.Destroy(root);
    }
}
