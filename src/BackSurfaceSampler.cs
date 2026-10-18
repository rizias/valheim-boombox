using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BackSurfaceSampler : IDisposable
    {
        private readonly Renderer renderer;
        private readonly SkinnedMeshRenderer skinned;
        private readonly MeshFilter filter;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> projected = new List<Vector3>();
        private readonly Vector3[] clipA = new Vector3[12];
        private readonly Vector3[] clipB = new Vector3[12];
        private Mesh baked;
        private Mesh original;
        private int[] triangles = new int[0];

        public BackSurfaceSampler(Renderer source)
        {
            renderer = source;
            skinned = source as SkinnedMeshRenderer;
            filter = source.GetComponent<MeshFilter>();
        }

        public static bool Supports(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer
                || renderer is MeshRenderer
                && renderer.GetComponent<MeshFilter>() != null;
        }

        public bool Active => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;

        public bool Prepare()
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                vertices.Clear();
                return false;
            }
            var source = skinned != null
                ? skinned.sharedMesh
                : filter != null
                    ? filter.sharedMesh
                    : null;
            if (source == null)
            {
                vertices.Clear();
                return false;
            }
            bool changed = source != original;
            original = source;
            if (skinned != null)
            {
                if (baked == null)
                {
                    baked = new Mesh { name = "Boombox surface snapshot" };
                    baked.MarkDynamic();
                }
                skinned.BakeMesh(baked, true);
                baked.GetVertices(vertices);
                if (changed || triangles.Length == 0)
                    triangles = baked.triangles;
            }
            else if (source.isReadable)
            {
                source.GetVertices(vertices);
                if (changed || triangles.Length == 0)
                    triangles = source.triangles;
            }
            else
            {
                var readable = BoomboxHeadPlacement.CollisionMesh(filter);
                readable.GetVertices(vertices);
                if (changed || triangles.Length == 0)
                    triangles = readable.triangles;
            }
            return true;
        }

        public bool Sample(Matrix4x4 worldToFrame, Rect area, out float depth, bool prepared = false)
        {
            depth = float.NegativeInfinity;
            if ((!prepared && !Prepare()) || vertices.Count == 0 || renderer == null)
                return false;
            Project(worldToFrame);
            bool found = false;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int count = ClipTriangle(i, area);
                for (int j = 0; j < count; j++)
                {
                    if (!float.IsNaN(clipA[j].z) && !float.IsInfinity(clipA[j].z))
                    {
                        depth = Mathf.Max(depth, clipA[j].z);
                        found = true;
                    }
                }
            }
            return found;
        }

        public void BlockedOffsets(
            Matrix4x4 worldToFrame,
            Rect area,
            float rear,
            float front,
            float clearance,
            List<Vector2> result
        )
        {
            if (renderer == null || vertices.Count == 0)
                return;
            Project(worldToFrame);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int count = ClipTriangle(i, area);
                if (count == 0)
                    continue;
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                for (int n = 0; n < count; n++)
                {
                    low = Mathf.Min(low, clipA[n].z);
                    high = Mathf.Max(high, clipA[n].z);
                }
                if (!float.IsInfinity(low) && !float.IsInfinity(high) && !float.IsNaN(low) && !float.IsNaN(high))
                    result.Add(new Vector2(low - front - clearance, high - rear + clearance));
            }
        }

        private void Project(Matrix4x4 worldToFrame)
        {
            Matrix4x4 toFrame = worldToFrame * renderer.transform.localToWorldMatrix;
            projected.Clear();
            foreach (var point in vertices)
                projected.Add(toFrame.MultiplyPoint3x4(point));
        }

        private int ClipTriangle(int triangle, Rect area)
        {
            int a = triangles[triangle], b = triangles[triangle + 1], c = triangles[triangle + 2];
            if (a >= projected.Count || b >= projected.Count || c >= projected.Count)
                return 0;
            Vector3 p = projected[a], q = projected[b], r = projected[c];
            if (Mathf.Max(p.x, Mathf.Max(q.x, r.x)) < area.xMin || Mathf.Min(p.x, Mathf.Min(q.x, r.x)) > area.xMax
                || Mathf.Max(p.y, Mathf.Max(q.y, r.y)) < area.yMin || Mathf.Min(p.y, Mathf.Min(q.y, r.y)) > area.yMax)
                return 0;
            clipA[0] = p;
            clipA[1] = q;
            clipA[2] = r;
            int count = Clip(clipA, 3, clipB, 0, area.xMin, true);
            count = Clip(clipB, count, clipA, 0, area.xMax, false);
            count = Clip(clipA, count, clipB, 1, area.yMin, true);
            return Clip(clipB, count, clipA, 1, area.yMax, false);
        }

        private static int Clip(Vector3[] input, int count, Vector3[] output, int axis, float boundary, bool greater)
        {
            if (count == 0)
                return 0;
            int written = 0;
            Vector3 previous = input[count - 1];
            float previousDistance = (previous[axis] - boundary) * (greater ? 1f : -1f);
            for (int i = 0; i < count; i++)
            {
                Vector3 current = input[i];
                float distance = (current[axis] - boundary) * (greater ? 1f : -1f);
                if ((distance >= 0f) != (previousDistance >= 0f))
                    output[written++] = Vector3.LerpUnclamped(
                        previous,
                        current,
                        previousDistance / (previousDistance - distance)
                    );
                if (distance >= 0f)
                    output[written++] = current;
                previous = current;
                previousDistance = distance;
            }
            return written;
        }

        public void Dispose()
        {
            if (baked != null)
                UnityEngine.Object.Destroy(baked);
        }
    }
}
