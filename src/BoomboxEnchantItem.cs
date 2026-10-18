using UnityEngine;

namespace VikingBoombox
{
    internal struct BoomboxEnchantFrame
    {
        internal bool LaunchHand;
        internal bool LaunchWeapon;
        internal float HandCharge;
        internal float WeaponCharge;
        internal Vector3? HitBurst;
    }

    internal sealed class BoomboxEnchantItem
    {
        private GameObject instance;
        private Renderer[] renderers = new Renderer[0];

        internal Bounds Bounds(GameObject item, Transform hand, Player player, float handSize)
        {
            if (item != instance)
            {
                instance = item;
                renderers = item == null ? new Renderer[0] : item.GetComponentsInChildren<Renderer>();
            }
            bool found = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                if (renderer == null
                    || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (found)
                    bounds.Encapsulate(renderer.bounds);
                else
                    bounds = renderer.bounds;
                found = true;
            }
            if (found)
                return bounds;
            return new Bounds(hand != null ? hand.position : player.GetCenterPoint(), Vector3.one * handSize);
        }
    }
}
