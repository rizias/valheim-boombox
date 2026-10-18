using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxGhostEffects
    {
        private const float BurstSparks = 12f;
        private const float BurstSpeed = 1.2f;
        private const float DotSizeMin = .018f;
        private const float DotSizeMax = .028f;
        private readonly GameObject root;
        private readonly BoomboxEnchantGlow glow;
        private readonly BoomboxEnchantTrail trail;
        private Bounds glowBounds;
        private float glowCharge;

        internal BoomboxGhostEffects()
        {
            root = new GameObject("BoomboxGhostParticles");
            try
            {
                glow = new BoomboxEnchantGlow(root.transform, "Ghost");
                trail = new BoomboxEnchantTrail(root.transform, "EnchantGhostBurst");
            }
            catch
            {
                Object.Destroy(root);
                throw;
            }
        }

        internal void Glow(Bounds bounds, float charge)
        {
            if (charge <= glowCharge)
                return;
            glowBounds = bounds;
            glowCharge = charge;
        }

        internal void Burst(Vector3 point)
        {
            int sparks = BoomboxParticleLook.Count(BurstSparks);
            for (int k = 0; k < sparks; k++)
                trail.Dot(point, Random.onUnitSphere * BurstSpeed, Random.Range(DotSizeMin, DotSizeMax));
        }

        internal void Present()
        {
            glow.Present(glowBounds, glowCharge);
            trail.Sync();
            glowCharge = 0f;
        }

        internal void Clear()
        {
            glowCharge = 0f;
            glow.Clear();
            trail.Clear();
        }

        internal void Destroy() => Object.Destroy(root);
    }
}
