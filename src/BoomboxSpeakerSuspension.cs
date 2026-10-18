using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    [DefaultExecutionOrder(11000)]
    public sealed class BoomboxSpeakerSuspension : MonoBehaviour
    {
        private const int Segments = 40;
        private const int Rings = 5;
        private sealed class Cone
        {
            internal Transform Membrane;
            internal Mesh Mesh;
            internal Vector3[] Vertices;
            internal Vector3 FixedCenter, LocalRim;
            internal Vector2 Radius;
            internal float Scale;
        }
        private readonly List<Cone> cones = new List<Cone>();
        private Transform frame;

        internal void Initialize(Transform root)
        {
            frame = root;
            var membranes = root.GetComponentsInChildren<Transform>(true).Where(
                t => t.name == "MembraneLeft" || t.name == "MembraneRight"
            ).ToArray();
            var rubber = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(
                m => m && m.name == "Rubber"
            );
            if (!rubber)
                return;
            foreach (var membrane in membranes)
            {
                var source = membrane.GetComponent<MeshFilter>()?.sharedMesh;
                if (!source || !source.isReadable)
                    continue;
                var localMatrix = MatrixInFrame(root, membrane);
                var points = source.vertices.Select(localMatrix.MultiplyPoint3x4).ToArray();
                float x = (points.Min(p => p.x) + points.Max(p => p.x)) * .5f;
                float y = (points.Min(p => p.y) + points.Max(p => p.y)) * .5f;
                float rx = points.Max(p => Mathf.Abs(p.x - x)), ry = points.Max(p => Mathf.Abs(p.y - y));
                if (rx <= .0001f || ry <= .0001f)
                    continue;
                var rim = points.Where(p => Mathf.Pow((p.x - x) / rx, 2) + Mathf.Pow((p.y - y) / ry, 2) > .96f).ToArray();
                var center = new Vector3(x, y, rim.Average(p => p.z));
                var cone = new Cone
                {
                    Membrane = membrane,
                    FixedCenter = center,
                    LocalRim = localMatrix.inverse.MultiplyPoint3x4(center),
                    Radius = new Vector2(rx, ry),
                    Scale = membrane.localScale.x,
                    Vertices = new Vector3[Segments * Rings],
                    Mesh = new Mesh { name = membrane.name + " flexible surround" }
                };
                cone.Mesh.MarkDynamic();
                var uv = new Vector2[cone.Vertices.Length];
                var triangles = new int[Segments * (Rings - 1) * 6];
                for (int ring = 0; ring < Rings; ring++)
                    for (int point = 0; point < Segments; point++)
                    {
                        uv[ring * Segments + point] = new Vector2((float)point / Segments, (float)ring / (Rings - 1));
                        if (ring == Rings - 1)
                            continue;
                        int start = (ring * Segments + point) * 6;
                        int a = ring * Segments + point, b = ring * Segments + (point + 1) % Segments, c = b + Segments, d = a + Segments;
                        triangles[start] = a;
                        triangles[start + 1] = b;
                        triangles[start + 2] = c;
                        triangles[start + 3] = a;
                        triangles[start + 4] = c;
                        triangles[start + 5] = d;
                    }
                cone.Mesh.vertices = cone.Vertices;
                cone.Mesh.triangles = triangles;
                cone.Mesh.uv = uv;
                var holder = new GameObject("SpeakerSuspension_" + membrane.name);
                holder.transform.SetParent(root, false);
                holder.layer = root.gameObject.layer;
                holder.AddComponent<MeshFilter>().sharedMesh = cone.Mesh;
                holder.AddComponent<MeshRenderer>().sharedMaterial = rubber;
                cones.Add(cone);
            }
            LateUpdate();
        }

        private void LateUpdate()
        {
            foreach (var cone in cones)
            {
                if (!cone.Membrane)
                    continue;
                var moving = PointInFrame(frame, cone.Membrane, cone.LocalRim);
                float growth = cone.Membrane.localScale.x / cone.Scale;
                for (int ring = 0; ring < Rings; ring++)
                {
                    float t = (float)ring / (Rings - 1);
                    float radius = Mathf.Lerp(1.07f, .985f * growth, t);
                    float z = Mathf.Lerp(cone.FixedCenter.z, moving.z, t) + .0015f * Mathf.Sin(t * Mathf.PI * 2);
                    for (int point = 0; point < Segments; point++)
                    {
                        float angle = point * Mathf.PI * 2 / Segments;
                        cone.Vertices[ring * Segments + point] = new Vector3(
                            cone.FixedCenter.x + cone.Radius.x * radius * Mathf.Cos(angle),
                            cone.FixedCenter.y + cone.Radius.y * radius * Mathf.Sin(angle),
                            z
                        );
                    }
                }
                cone.Mesh.vertices = cone.Vertices;
                cone.Mesh.RecalculateNormals();
                cone.Mesh.RecalculateBounds();
            }
        }

        internal static Vector3 PointInFrame(
            Transform root,
            Transform part,
            Vector3 point
        ) => MatrixInFrame(root, part).MultiplyPoint3x4(point);

        private static Matrix4x4 MatrixInFrame(Transform root, Transform part)
        {
            var matrix = Matrix4x4.identity;
            while (part != root)
            {
                if (!part)
                    throw new System.InvalidOperationException("Membrane is outside its case frame");
                matrix = Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale) * matrix;
                part = part.parent;
            }
            return matrix;
        }

        private void OnDestroy()
        {
            foreach (var cone in cones)
                if (cone.Mesh)
                    Destroy(cone.Mesh);
        }
    }
}
