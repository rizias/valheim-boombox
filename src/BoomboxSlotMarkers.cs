using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxSlotMarkers
    {
        private const float Reach = .25f;

        internal static BoomboxPendantMount[] Mounts(GameObject model, Bounds body)
        {
            var slots = BoomboxDecorationCatalog.Slots;
            var result = new BoomboxPendantMount[slots.Length];
            var heads = BoomboxHeadPlacement.Mounts(model, body);
            foreach (var slot in slots.Where(slot => slot.Kind == BoomboxDecorationKind.Figurine))
                result[slot.Index] = new BoomboxPendantMount
                {
                    Loop = heads[slot.MountIndex],
                    Normal = Vector3.up,
                    Along = Vector3.forward
                };
            var frame = model.transform;
            var filters = frame.Find(VikingBoomboxVisual.ModelPrefabKey)
                .GetComponentsInChildren<MeshFilter>(true)
                .Where(
                f => f.sharedMesh
                    && f.gameObject.activeInHierarchy
                    && f.GetComponent<MeshRenderer>() is MeshRenderer renderer
                    && renderer.enabled
            );
            var colliders = BoomboxHeadPlacement.Colliders(filters);
            var common = new List<int>();
            try
            {
                foreach (var group in BoomboxContactShapes.Instance.ActiveMounts)
                    for (int index = 0; index < group.slots.Length; index++)
                    {
                        result[group.slots[index]] = Place(colliders, frame, group, index);
                        if (group.common)
                            common.Add(group.slots[index]);
                    }
            }
            finally
            {
                BoomboxHeadPlacement.Release(colliders);
            }
            var missing = slots.Where(slot => result[slot.Index].Normal == Vector3.zero).Select(slot => slot.Index).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException(
                    "Pendant mount layout has no place for slots " + string.Join(", ", missing)
                );
            if (common.Count > 0)
            {
                float height = common.Min(slot => result[slot].Loop.y);
                foreach (int slot in common)
                    result[slot].Loop.y = height;
            }
            return result;
        }

        private static BoomboxPendantMount Place(
            List<MeshCollider> colliders,
            Transform frame,
            BoomboxMountGroup group,
            int index
        )
        {
            var normal = group.Normal;
            var along = group.Legs;
            var aim = group.Aim(index);
            var span = BoomboxStaple.Span(Quaternion.LookRotation(normal, along), Vector3.up);
            if (group.top.HasValue)
                aim.y = group.top.Value - span.y;
            if (group.bottom.HasValue)
                aim.y = group.bottom.Value - span.x;
            if (!BoomboxHeadPlacement.Cast(
                colliders,
                frame.TransformPoint(aim + normal * Reach),
                frame.TransformDirection(-normal),
                frame.TransformVector(normal * Reach * 2f).magnitude,
                out var hit
            ))
                throw new InvalidOperationException("No boombox surface for pendant mount at slot " + group.slots[index]);
            return new BoomboxPendantMount
            {
                Loop = frame.InverseTransformPoint(hit) + normal * BoomboxStaple.LoopGap,
                Normal = normal,
                Along = along
            };
        }
    }
}
