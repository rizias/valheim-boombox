using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal sealed class BoomboxChainLinks
    {
        internal Mesh[] Meshes;
        internal Material[] Materials;
        internal float[] Tops;
        internal float[] Bottoms;
        internal float Width;
    }

    internal static class BoomboxChainMesh
    {
        internal static BoomboxChainLinks Build(GameObject prefab, int required)
        {
            var renderers = Renderers(prefab).ToArray();
            if (renderers.Length != 1)
                throw new InvalidOperationException("Native chain must have one visible mesh, found " + renderers.Length);
            var renderer = renderers[0];
            var source = renderer.GetComponent<MeshFilter>().sharedMesh;
            var positions = Read(source, renderer, VertexAttribute.Position, 3);
            var normals = Read(source, renderer, VertexAttribute.Normal, 3);
            var tangents = Read(source, renderer, VertexAttribute.Tangent, 4);
            var uv = Read(source, renderer, VertexAttribute.TexCoord0, 2);
            var triangles = Triangles(source);
            var groups = Components(positions, triangles, source.vertexCount);
            if (groups.Count < required)
                throw new InvalidOperationException("Native chain has " + groups.Count + " links, " + required
                    + " required");
            var links = new BoomboxChainLinks
            {
                Meshes = new Mesh[groups.Count],
                Materials = renderer.sharedMaterials,
                Tops = new float[groups.Count],
                Bottoms = new float[groups.Count]
            };
            var highest = groups.Select(g => g.Max(i => positions[i * 3 + 1])).ToArray();
            for (int link = 0; link < groups.Count; link++)
            {
                links.Bottoms[link] = groups[link].Min(i => positions[i * 3 + 1]);
                links.Tops[link] = link == 0 ? highest[0] : (links.Bottoms[link - 1] + highest[link]) * .5f;
                links.Meshes[link] = LinkMesh(
                    link,
                    groups[link],
                    links.Tops[link],
                    positions,
                    normals,
                    tangents,
                    uv,
                    triangles
                );
                links.Width = Mathf.Max(
                    links.Width,
                    links.Meshes[link].bounds.size.x,
                    links.Meshes[link].bounds.size.z
                );
            }
            return links;
        }

        private static Mesh LinkMesh(
            int link,
            List<int> group,
            float top,
            float[] positions,
            float[] normals,
            float[] tangents,
            float[] uv,
            List<int>[] triangles
        )
        {
            var map = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var linkNormals = new List<Vector3>();
            var linkTangents = new List<Vector4>();
            var linkUv = new List<Vector2>();
            foreach (int index in group)
            {
                map[index] = vertices.Count;
                vertices.Add(
                    new Vector3(positions[index * 3], positions[index * 3 + 1] - top, positions[index * 3 + 2])
                );
                linkNormals.Add(new Vector3(normals[index * 3], normals[index * 3 + 1], normals[index * 3 + 2]));
                linkTangents.Add(
                    new Vector4(
                        tangents[index * 4],
                        tangents[index * 4 + 1],
                        tangents[index * 4 + 2],
                        tangents[index * 4 + 3]
                    )
                );
                linkUv.Add(new Vector2(uv[index * 2], uv[index * 2 + 1]));
            }
            var mesh = new Mesh { name = "ChainLink" + link };
            mesh.SetVertices(vertices);
            mesh.SetNormals(linkNormals);
            mesh.SetTangents(linkTangents);
            mesh.SetUVs(0, linkUv);
            mesh.subMeshCount = triangles.Length;
            for (int sub = 0; sub < triangles.Length; sub++)
            {
                var indices = new List<int>();
                for (int corner = 0; corner < triangles[sub].Count; corner += 3)
                    if (map.ContainsKey(triangles[sub][corner]))
                        for (int offset = 0; offset < 3; offset++)
                            indices.Add(map[triangles[sub][corner + offset]]);
                mesh.SetTriangles(indices, sub);
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<List<int>> Components(float[] positions, List<int>[] triangles, int count)
        {
            var parent = new int[count];
            var canonical = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < count; i++)
            {
                var key = new Vector3Int(
                    Mathf.RoundToInt(positions[i * 3] * 100000f),
                    Mathf.RoundToInt(positions[i * 3 + 1] * 100000f),
                    Mathf.RoundToInt(positions[i * 3 + 2] * 100000f)
                );
                if (!canonical.TryGetValue(key, out int first))
                    canonical[key] = first = i;
                parent[i] = first;
            }
            foreach (var list in triangles)
                for (int corner = 0; corner < list.Count; corner += 3)
                {
                    int a = Root(parent, list[corner]);
                    parent[Root(parent, list[corner + 1])] = a;
                    parent[Root(parent, list[corner + 2])] = a;
                }
            return Enumerable.Range(0, count).GroupBy(i => Root(parent, i)).Select(g => g.ToList()).OrderByDescending(
                g => g.Average(i => positions[i * 3 + 1])
            ).ToList();
        }

        private static int Root(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }
            return index;
        }

        private static float[] Read(Mesh source, MeshRenderer renderer, VertexAttribute attribute, int dimension)
        {
            var mesh = renderer.additionalVertexStreams
                && renderer.additionalVertexStreams.HasVertexAttribute(attribute)
                ? renderer.additionalVertexStreams
                : source;
            if (!mesh.HasVertexAttribute(attribute))
                throw new InvalidOperationException("Native chain mesh has no " + attribute);
            var format = mesh.GetVertexAttributeFormat(attribute);
            if (format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16)
                throw new InvalidOperationException("Unsupported chain " + attribute + " format: " + format);
            int width = format == VertexAttributeFormat.Float32 ? 4 : 2;
            int offset = mesh.GetVertexAttributeOffset(attribute),
                components = Mathf.Min(dimension, mesh.GetVertexAttributeDimension(attribute));
            byte[] data;
            int stride;
            using (var buffer = mesh.GetVertexBuffer(mesh.GetVertexAttributeStream(attribute)))
            {
                stride = buffer.stride;
                data = BoomboxMeshReadback.Read(buffer, "Cannot read chain " + attribute);
            }
            var result = new float[source.vertexCount * dimension];
            for (int vertex = 0; vertex < source.vertexCount; vertex++)
                for (int component = 0; component < components; component++)
                {
                    int at = vertex * stride + offset + component * width;
                    result[vertex * dimension + component] = width == 4
                        ? BitConverter.ToSingle(data, at)
                        : BoomboxMeshReadback.Half(data, at);
                }
            return result;
        }

        private static List<int>[] Triangles(Mesh source)
        {
            byte[] data;
            using (var buffer = source.GetIndexBuffer())
            {
                data = BoomboxMeshReadback.Read(buffer, "Cannot read chain indices");
            }
            var result = new List<int>[source.subMeshCount];
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var part = source.GetSubMesh(sub);
                if (part.topology != MeshTopology.Triangles)
                    throw new InvalidOperationException("Native chain submesh " + sub + " is " + part.topology);
                result[sub] = new List<int>(part.indexCount);
                for (int i = 0; i < part.indexCount; i++)
                {
                    int at = part.indexStart + i;
                    result[sub].Add(BoomboxMeshReadback.Index(data, source.indexFormat, at, part.baseVertex));
                }
            }
            return result;
        }

        private static IEnumerable<MeshRenderer> Renderers(GameObject prefab)
        {
            var groups = prefab.GetComponentsInChildren<LODGroup>(true).Select(group => group.GetLODs()).Where(
                lods => lods.Length > 0
            ).ToArray();
            var nearest = new HashSet<Renderer>(groups.SelectMany(lods => lods[0].renderers));
            var distant = new HashSet<Renderer>(groups.SelectMany(lods => lods.Skip(1)
                .SelectMany(lod => lod.renderers)));
            return prefab.GetComponentsInChildren<MeshRenderer>(true).Where(renderer =>
                renderer.enabled
                && (nearest.Contains(renderer) || !distant.Contains(renderer))
                && BoomboxItemPrefabs.VisibleUnder(renderer.transform, prefab.transform)
                && renderer.GetComponent<MeshFilter>() is MeshFilter filter && filter.sharedMesh != null);
        }
    }
}
