using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxPendantParticleAnchor : MonoBehaviour
    {
        internal int Slot;
        internal float BodySize;

        internal static void Mark(Transform pivot, int slot, Vector3 center, float bodySize)
        {
            var anchor = new GameObject("PendantParticles").AddComponent<BoomboxPendantParticleAnchor>();
            anchor.Slot = slot;
            anchor.BodySize = bodySize;
            anchor.gameObject.layer = pivot.gameObject.layer;
            anchor.transform.SetParent(pivot, false);
            anchor.transform.localPosition = center;
            anchor.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
    }
}
