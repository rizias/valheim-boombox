using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxParticleCoins
    {
        private static Mesh[] meshes;
        private static Material template;
        private static float extent;
        private static readonly List<Mesh> created = new List<Mesh>();

        internal static Mesh[] Meshes
        {
            get
            {
                Load();
                return meshes;
            }
        }

        internal static Material Template
        {
            get
            {
                Load();
                return template;
            }
        }

        internal static float Extent
        {
            get
            {
                Load();
                return extent;
            }
        }

        internal static void Clear()
        {
            foreach (var mesh in created)
                if (mesh != null)
                    UnityEngine.Object.Destroy(mesh);
            created.Clear();
            meshes = null;
            template = null;
            extent = 0f;
        }

        private static void Load()
        {
            if (meshes != null)
                return;
            var coins = ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab("Coins");
            if (coins == null)
                throw new InvalidOperationException(BoomboxText.Get("text.game_item_coins_was_not_found"));
            var renderers = Renderers(coins).ToArray();
            var found = renderers.Select(MeshOf).Distinct().Select(Single).ToArray();
            var material = renderers.Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            if (found.Length == 0 || material == null)
                throw new InvalidOperationException(BoomboxText.Get("text.item_coins_has_no_coin_model"));
            extent = found.Max(m => Mathf.Max(m.bounds.size.x, m.bounds.size.y, m.bounds.size.z));
            template = material;
            meshes = found;
        }

        private static IEnumerable<Renderer> Renderers(GameObject source)
        {
            var groups = source.GetComponentsInChildren<LODGroup>(true).Select(g => g.GetLODs()).Where(l => l.Length > 0).ToArray();
            var nearest = new HashSet<Renderer>(groups.SelectMany(l => l[0].renderers));
            var distant = new HashSet<Renderer>(groups.SelectMany(l => l.Skip(1).SelectMany(lod => lod.renderers)));
            return source.GetComponentsInChildren<Renderer>(true).Where(
                r => r.enabled && (nearest.Contains(r) || !distant.Contains(r))
                    && BoomboxItemPrefabs.VisibleUnder(r.transform, source.transform)
                    && MeshOf(r) != null
            );
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin)
                return skin.sharedMesh;
            if (!(renderer is MeshRenderer))
                return null;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private static Mesh Single(Mesh pile)
        {
            var vertices = pile.vertices;
            var triangles = pile.triangles;
            var parent = Enumerable.Range(0, vertices.Length).ToArray();
            var first = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 10000f);
                if (first.TryGetValue(key, out var root))
                    parent[i] = root;
                else
                    first[key] = i;
            }
            Func<int, int> find = i =>
            {
                while (parent[i] != i)
                    i = parent[i] = parent[parent[i]];
                return i;
            };
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = find(triangles[t]);
                parent[find(triangles[t + 1])] = a;
                parent[find(triangles[t + 2])] = a;
            }
            var groups = Enumerable.Range(
                0,
                triangles.Length / 3
            ).GroupBy(t => find(triangles[t * 3])).OrderBy(g => g.Count()).ToArray();
            if (groups.Length < 2)
                return pile;
            var chosen = groups[groups.Length / 2].SelectMany(
                t => new[] { triangles[t * 3], triangles[t * 3 + 1], triangles[t * 3 + 2] }
            ).ToArray();
            var used = chosen.Distinct().ToArray();
            var index = new Dictionary<int, int>();
            for (int i = 0; i < used.Length; i++)
                index[used[i]] = i;
            var normals = pile.normals;
            var uv = pile.uv;
            var coin = new Mesh { name = pile.name + " single" };
            created.Add(coin);
            coin.vertices = used.Select(i => vertices[i]).ToArray();
            if (normals.Length == vertices.Length)
                coin.normals = used.Select(i => normals[i]).ToArray();
            if (uv.Length == vertices.Length)
                coin.uv = used.Select(i => uv[i]).ToArray();
            coin.triangles = chosen.Select(i => index[i]).ToArray();
            if (normals.Length != vertices.Length)
                coin.RecalculateNormals();
            if (uv.Length == vertices.Length)
                coin.RecalculateTangents();
            coin.RecalculateBounds();
            var center = coin.bounds.center;
            coin.vertices = coin.vertices.Select(v => v - center).ToArray();
            coin.RecalculateBounds();
            return coin;
        }
    }
}
