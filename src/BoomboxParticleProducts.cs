using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxParticleProducts
    {
        private sealed class Shape
        {
            internal Mesh[] Meshes;
            internal Material Material;
            internal float Extent;
        }

        private static readonly Dictionary<string, Shape> shapes = new Dictionary<string, Shape>(
            StringComparer.Ordinal
        );

        internal static Mesh[] Meshes(string product) => product == null
            ? BoomboxParticleCoins.Meshes
            : Of(product).Meshes;

        internal static float Extent(string product) => product == null
            ? BoomboxParticleCoins.Extent
            : Of(product).Extent;

        internal static Material Material(string product, BoomboxParticleLayer layer) => product == null
            ? BoomboxParticleAssets.Coin(layer)
            : Of(product).Material;

        internal static bool Droppable(string product)
        {
            try
            {
                return Of(product) != null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static void Clear()
        {
            foreach (var shape in shapes.Values)
                foreach (var mesh in shape.Meshes)
                    if (mesh != null)
                        UnityEngine.Object.Destroy(mesh);
            shapes.Clear();
        }

        private static Shape Of(string product)
        {
            if (shapes.TryGetValue(product, out var shape))
                return shape;
            var prefab = ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(product);
            if (prefab == null)
                throw new InvalidOperationException(BoomboxText.Get("text.product") + product + BoomboxText.Get("text.was_not_found"));
            var lods = prefab.GetComponentsInChildren<LODGroup>(true).Select(g => g.GetLODs()).Where(l => l.Length > 1).SelectMany(
                l => l.Skip(1).SelectMany(lod => lod.renderers)
            ).ToArray();
            var filter = prefab.GetComponentsInChildren<MeshFilter>(true)
            .Where(
                f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() is MeshRenderer renderer && renderer.enabled
                    && renderer.sharedMaterial != null
                    && !lods.Contains(renderer)
                    && BoomboxItemPrefabs.VisibleUnder(f.transform, prefab.transform)
            )
            .OrderByDescending(f => f.sharedMesh.vertexCount).FirstOrDefault();
            if (filter == null)
                throw new InvalidOperationException(BoomboxText.Get("text.product_2") + product + BoomboxText.Get("text.has_no_model"));
            var source = BoomboxHeadPlacement.CollisionMesh(filter);
            var scale = filter.transform.lossyScale;
            var mesh = new Mesh { name = product + " product", indexFormat = source.indexFormat };
            var points = source.vertices.Select(v => Vector3.Scale(v, scale)).ToArray();
            var center = points.Aggregate(
            new Bounds(points[0], Vector3.zero),
            (b, p) =>
                {
                    b.Encapsulate(p);
                    return b;
                }
            ).center;
            mesh.vertices = points.Select(p => p - center).ToArray();
            if (source.uv.Length == points.Length)
                mesh.uv = source.uv;
            mesh.triangles = source.triangles;
            mesh.RecalculateNormals();
            if (mesh.uv.Length == points.Length)
                mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            shape = new Shape
            {
                Meshes = new[] { mesh },
                Material = filter.GetComponent<MeshRenderer>().sharedMaterial,
                Extent = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z)
            };
            shapes[product] = shape;
            return shape;
        }
    }
}
