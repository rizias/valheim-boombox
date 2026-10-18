using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxPendantPlacement
    {
        private static Vector3? standFacing;

        internal static void Orient(Transform item, GameObject prefab, Vector3 outward)
        {
            if (ItemStand.GetAttachPrefab(prefab) != null)
                BoomboxHeadPlacement.Orient(item, prefab);
            var facing = StandFacing();
            var direction = new Vector3(outward.x, 0f, outward.z).normalized;
            item.localRotation = Quaternion.LookRotation(direction)
                * Quaternion.Inverse(Quaternion.LookRotation(facing))
                * item.localRotation;
        }

        internal static float HangHeight(Transform item, Transform pivot, float radius, float top, float depth)
        {
            var filters = item.GetComponentsInChildren<MeshFilter>(true).Where(
                f => f.sharedMesh
                    && f.gameObject.activeInHierarchy
                    && f.GetComponent<Renderer>() is Renderer renderer
                    && renderer.enabled
            ).ToArray();
            var colliders = BoomboxHeadPlacement.Colliders(filters);
            try
            {
                float highest = float.NegativeInfinity;
                float length = pivot.TransformVector(Vector3.down * depth).magnitude;
                var down = -pivot.up;
                foreach (var offset in new[] {
                    Vector3.zero,
                    Vector3.right * radius,
                    Vector3.left * radius,
                    Vector3.forward * radius,
                    Vector3.back * radius
                })
                {
                    var origin = pivot.TransformPoint(offset + Vector3.up * top);
                    for (int i = 0; i < colliders.Count; i++)
                    {
                        float travelled = 0f;
                        for (int step = 0; step < 64 && travelled < length; step++)
                        {
                            if (!colliders[i].Raycast(
                                new Ray(origin + down * travelled, down),
                                out var hit,
                                length - travelled
                            ))
                                break;
                            if (BoomboxHeadPlacement.VisibleContact(filters[i], hit))
                            {
                                highest = Mathf.Max(highest, pivot.InverseTransformPoint(hit.point).y);
                                break;
                            }
                            travelled += hit.distance + .0001f;
                        }
                    }
                }
                return highest;
            }
            finally
            {
                BoomboxHeadPlacement.Release(colliders);
            }
        }

        internal static float RiseHeight(Transform item, Transform pivot, float from, float to)
        {
            var filters = item.GetComponentsInChildren<MeshFilter>(true).Where(
                f => f.sharedMesh
                    && f.gameObject.activeInHierarchy
                    && f.GetComponent<Renderer>() is Renderer renderer
                    && renderer.enabled
            ).ToArray();
            var colliders = BoomboxHeadPlacement.Colliders(filters);
            try
            {
                float lowest = float.NaN;
                var origin = pivot.TransformPoint(Vector3.up * from);
                float length = pivot.TransformVector(Vector3.up * (to - from)).magnitude;
                for (int i = 0; i < colliders.Count; i++)
                {
                    float travelled = 0f;
                    for (int step = 0; step < 64 && travelled < length; step++)
                    {
                        if (!colliders[i].Raycast(
                            new Ray(origin + pivot.up * travelled, pivot.up),
                            out var hit,
                            length - travelled
                        ))
                            break;
                        if (BoomboxHeadPlacement.VisibleContact(filters[i], hit))
                        {
                            float y = pivot.InverseTransformPoint(hit.point).y;
                            if (float.IsNaN(lowest) || y < lowest)
                                lowest = y;
                            break;
                        }
                        travelled += hit.distance + .0001f;
                    }
                }
                return lowest;
            }
            finally
            {
                BoomboxHeadPlacement.Release(colliders);
            }
        }

        internal static float Extent(Transform item, Transform pivot)
        {
            var bounds = new Bounds();
            bool found = false;
            foreach (var filter in item.GetComponentsInChildren<MeshFilter>(true).Where(
                f => f.sharedMesh
                    && f.gameObject.activeInHierarchy
                    && f.GetComponent<Renderer>() is Renderer renderer
                    && renderer.enabled
            ))
            {
                var matrix = pivot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var vertex in BoomboxHeadPlacement.CollisionMesh(filter).vertices)
                {
                    var point = matrix.MultiplyPoint3x4(vertex);
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                        bounds.Encapsulate(point);
                }
            }
            return Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        }

        internal static Vector3 StandFacing()
        {
            if (standFacing.HasValue)
                return standFacing.Value;
            var standPrefab = ZNetScene.instance?.GetPrefab("itemstand");
            if (!standPrefab)
                throw new InvalidOperationException("Native vertical item stand is unavailable");
            var stand = standPrefab.GetComponentInChildren<ItemStand>(true);
            var frame = (Transform)AccessTools.Field(typeof(ItemStand), "m_attachOther").GetValue(stand);
            var toStand = stand.transform.worldToLocalMatrix;
            var plate = new Bounds();
            bool found = false;
            foreach (var renderer in standPrefab.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !r.transform.IsChildOf(frame)))
            {
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                    continue;
                var matrix = toStand * renderer.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(
                        mesh.bounds.center + Vector3.Scale(
                            mesh.bounds.extents,
                            new Vector3(
                                (corner & 1) == 0
                                    ? -1
                                    : 1,
                                (corner & 2) == 0
                                    ? -1
                                    : 1,
                                (corner & 4) == 0
                                    ? -1
                                    : 1
                            )
                        )
                    );
                    if (!found)
                    {
                        plate = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                        plate.Encapsulate(point);
                }
            }
            if (!found)
                throw new InvalidOperationException("Native vertical item stand has no geometry");
            var offset = toStand.MultiplyPoint3x4(frame.position) - plate.center;
            standFacing = Mathf.Abs(offset.x) > Mathf.Abs(offset.z)
                ? new Vector3(Mathf.Sign(offset.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(offset.z));
            return standFacing.Value;
        }
    }
}
