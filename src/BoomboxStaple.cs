using System;
using System.Linq;
using SoftReferenceableAssets;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal static class BoomboxStaple
    {
        internal const float LoopGap = .015f;
        private const string Asset = "Assets/world/Props/Morkhalla/Morkhalla_WallChain1.prefab";
        private const string PartPath = "GameObject/block (17)";
        private const string WallPath = "GameObject/block (2)";
        private const string Ripple = "_RippleDistance";
        private static SoftReference<GameObject> reference;
        private static bool loaded;
        private static Mesh mesh;
        private static Material[] materials;
        private static Matrix4x4 part;
        private static Vector3 loop;
        private static float scale;
        private static Vector3[] points;

        internal static void AllowLoading() => Runtime.MakeAllAssetsLoadable();

        internal static Vector2 Span(Quaternion rotation, Vector3 axis)
        {
            Measure();
            var along = Quaternion.Inverse(rotation) * axis;
            return new Vector2(
                points.Min(point => Vector3.Dot(point, along)),
                points.Max(point => Vector3.Dot(point, along))
            );
        }

        internal static Renderer Create(Transform parent, Vector3 normal, Vector3 along, Material material)
        {
            Measure();
            var rotation = Quaternion.LookRotation(normal, along);
            var staple = new GameObject("Staple").transform;
            staple.SetParent(parent, false);
            staple.localRotation = rotation;
            staple.localScale = Vector3.one * scale;
            staple.localPosition = -(rotation * loop) * scale;
            var piece = new GameObject("StaplePart");
            piece.layer = parent.gameObject.layer;
            piece.transform.SetParent(staple, false);
            piece.transform.localPosition = part.GetColumn(3);
            piece.transform.localRotation = part.rotation;
            piece.transform.localScale = part.lossyScale;
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = material == null
                ? materials
                : Enumerable.Repeat(material, mesh.subMeshCount).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        internal static void Clear()
        {
            if (loaded)
                reference.Release();
            if (materials != null)
                foreach (var material in materials)
                    if (material)
                        UnityEngine.Object.Destroy(material);
            loaded = false;
            mesh = null;
            materials = null;
        }

        private static void Measure()
        {
            if (mesh != null)
                return;
            var ids = Runtime.GetAllAssetPathsInBundleMappedToAssetID();
            string key = ids.Keys.FirstOrDefault(k => string.Equals(k, Asset, StringComparison.OrdinalIgnoreCase));
            if (key == null)
                throw new InvalidOperationException("Staple model is unavailable: " + Asset);
            reference = new SoftReference<GameObject>(ids[key]);
            reference.Load();
            loaded = true;
            var prefab = reference.Asset;
            if (prefab == null)
                throw new InvalidOperationException("Staple model did not load: " + Asset);
            var filter = Filter(prefab, PartPath);
            var wall = Filter(prefab, WallPath);
            var frame = prefab.transform.worldToLocalMatrix;
            var toWall = frame * wall.transform.localToWorldMatrix;
            float surface = float.NegativeInfinity;
            var box = wall.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
                surface = Mathf.Max(
                    surface,
                    toWall.MultiplyPoint3x4(box.center + Vector3.Scale(
                            box.extents,
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
                        )).z
                );
            part = frame * filter.transform.localToWorldMatrix;
            var readable = BoomboxHeadPlacement.CollisionMesh(filter);
            var vertices = readable.vertices.Select(v => part.MultiplyPoint3x4(v)).ToArray();
            var apex = vertices.OrderByDescending(v => v.z).First();
            var origin = new Vector3((vertices.Min(v => v.x) + vertices.Max(v => v.x)) * .5f, apex.y, surface);
            float distance = BoomboxRayMesh.Nearest(origin, Vector3.forward, vertices, readable.triangles);
            if (float.IsPositiveInfinity(distance) || distance <= 0f)
                throw new InvalidOperationException("Staple loop was not found");
            loop = origin + Vector3.forward * distance;
            scale = LoopGap / distance;
            points = vertices.Select(v => (v - loop) * scale).ToArray();
            materials = filter.GetComponent<MeshRenderer>().sharedMaterials.Select(Still).ToArray();
            mesh = filter.sharedMesh;
        }

        private static Material Still(Material source)
        {
            var material = new Material(source) { name = source.name + " (hammered staple)" };
            if (material.HasProperty(Ripple))
                material.SetFloat(Ripple, 0f);
            return material;
        }

        private static MeshFilter Filter(GameObject prefab, string path)
        {
            var node = prefab.transform.Find(path);
            var filter = node == null ? null : node.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null)
                throw new InvalidOperationException("Staple model part is missing: " + path);
            return filter;
        }
    }
}
