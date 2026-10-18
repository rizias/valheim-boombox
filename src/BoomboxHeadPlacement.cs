using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal static class BoomboxHeadPlacement
    {
        private static readonly Dictionary<Mesh, Mesh> collisionMeshes = new Dictionary<Mesh, Mesh>();
        private static readonly Dictionary<Texture, Texture2D> readableTextures = new Dictionary<Texture, Texture2D>();

        internal static void Clear()
        {
            foreach (var mesh in collisionMeshes.Values)
                if (mesh)
                    UnityEngine.Object.Destroy(mesh);
            collisionMeshes.Clear();
            foreach (var texture in readableTextures.Values)
                if (texture)
                    UnityEngine.Object.Destroy(texture);
            readableTextures.Clear();
        }

        internal static Mesh CollisionMesh(MeshFilter filter)
        {
            var source = filter.sharedMesh;
            if (collisionMeshes.TryGetValue(source, out var cached))
                return cached;
            if (source.isReadable)
            {
                var vertices = source.vertices;
                var bounds = source.bounds;
                bounds.Expand(Mathf.Max(.00001f, bounds.size.magnitude * .0001f));
                if (vertices.Length > 0 && vertices.Any(v => (v - vertices[0]).sqrMagnitude > .000001f)
                && vertices.All(v => bounds.Contains(v)))
                    return source;
            }
            var renderer = filter.GetComponent<MeshRenderer>();
            var positions = renderer && renderer.additionalVertexStreams
            && renderer.additionalVertexStreams.HasVertexAttribute(VertexAttribute.Position)
                ? renderer.additionalVertexStreams
                : source;
            int streamIndex = positions.GetVertexAttributeStream(VertexAttribute.Position);
            int offset = positions.GetVertexAttributeOffset(VertexAttribute.Position);
            var format = positions.GetVertexAttributeFormat(VertexAttribute.Position);
            byte[] data;
            int stride;
            using (var buffer = positions.GetVertexBuffer(streamIndex))
            {
                stride = buffer.stride;
                data = BoomboxMeshReadback.Read(buffer, "Cannot read rendered geometry: " + source.name);
            }
            var points = new Vector3[source.vertexCount];
            for (int i = 0; i < points.Length; i++)
            {
                int start = i * stride + offset;
                if (format == VertexAttributeFormat.Float32)
                    points[i] = new Vector3(
                        BitConverter.ToSingle(data, start),
                        BitConverter.ToSingle(data, start + 4),
                        BitConverter.ToSingle(data, start + 8)
                    );
                else if (format == VertexAttributeFormat.Float16)
                    points[i] = new Vector3(
                        BoomboxMeshReadback.Half(data, start),
                        BoomboxMeshReadback.Half(data, start + 2),
                        BoomboxMeshReadback.Half(data, start + 4));
                else
                    throw new InvalidOperationException("Unsupported vertex position format: " + format);
            }
            byte[] indexData;
            using (var buffer = source.GetIndexBuffer())
            {
                indexData = BoomboxMeshReadback.Read(buffer, "Cannot read rendered indices: " + source.name);
            }
            var indices = new List<int>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var part = source.GetSubMesh(sub);
                if (part.topology != MeshTopology.Triangles)
                    continue;
                for (int i = 0; i < part.indexCount; i++)
                {
                    int at = part.indexStart + i;
                    indices.Add(BoomboxMeshReadback.Index(indexData, source.indexFormat, at, part.baseVertex));
                }
            }
            var mesh = new Mesh { name = source.name + " collision readback", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = points;
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            if (source.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                int uvStream = source.GetVertexAttributeStream(VertexAttribute.TexCoord0),
                    uvOffset = source.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
                var uvFormat = source.GetVertexAttributeFormat(VertexAttribute.TexCoord0);
                using (var buffer = source.GetVertexBuffer(uvStream))
                {
                    var request = AsyncGPUReadback.Request(buffer);
                    request.WaitForCompletion();
                    if (!request.hasError)
                    {
                        var bytes = request.GetData<byte>().ToArray();
                        var uv = new Vector2[points.Length];
                        for (int i = 0; i < uv.Length; i++)
                        {
                            int at = i * buffer.stride + uvOffset;
                            uv[i] = uvFormat == VertexAttributeFormat.Float16
                                ? new Vector2(BoomboxMeshReadback.Half(bytes, at), BoomboxMeshReadback.Half(bytes, at + 2))
                                : new Vector2(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4));
                        }
                        mesh.uv = uv;
                    }
                }
            }
            if (mesh.bounds.size.sqrMagnitude < .00000001f)
                throw new InvalidOperationException("Rendered mesh has no extent: " + source.name);
            collisionMeshes[source] = mesh;
            return mesh;
        }

        internal static void Orient(Transform head, GameObject prefab)
        {
            var attach = ItemStand.GetAttachPrefab(prefab).transform;
            var copy = head.GetChild(0);
            var item = prefab.GetComponent<ItemDrop>().m_itemData;
            copy.localPosition = attach.localPosition;
            copy.localRotation = attach.localRotation;
            copy.localScale = Vector3.Scale(attach.localScale, item.GetScale(1));
            var offset = item.m_shared.m_itemStandOffsets.FirstOrDefault(
                o => (o.m_orientations & ItemStand.Orientation.Vertical) != ItemStand.Orientation.None
            );
            if (offset != null)
            {
                copy.localPosition += offset.m_position;
                copy.localRotation *= Quaternion.Euler(offset.m_rotation);
                copy.localScale = Vector3.Scale(copy.localScale, offset.m_scale);
            }
            var standPrefab = ZNetScene.instance.GetPrefab("itemstand");
            if (!standPrefab)
                throw new InvalidOperationException("Native vertical item stand is unavailable");
            var stand = standPrefab.GetComponentInChildren<ItemStand>(true);
            var frame = (Transform)AccessTools.Field(typeof(ItemStand), "m_attachOther").GetValue(stand);
            head.localRotation = Quaternion.Inverse(stand.transform.rotation) * frame.rotation;
        }

        internal static List<MeshCollider> Colliders(IEnumerable<MeshFilter> filters)
        {
            var result = new List<MeshCollider>();
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<Renderer>();
                if (!filter.sharedMesh || !renderer)
                    continue;
                var obj = new GameObject("BoomboxFitCollider");
                obj.layer = 31;
                obj.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                obj.transform.localScale = filter.transform.lossyScale;
                var collider = obj.AddComponent<MeshCollider>();
                collider.sharedMesh = CollisionMesh(filter);
                result.Add(collider);
            }
            Physics.SyncTransforms();
            return result;
        }

        internal static void Release(IEnumerable<MeshCollider> colliders)
        {
            foreach (var collider in colliders)
                if (collider)
                {
                    collider.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(collider.gameObject);
                }
        }

        internal static bool Cast(
            IEnumerable<MeshCollider> colliders,
            Vector3 origin,
            Vector3 direction,
            float length,
            out Vector3 point
        )
        {
            float distance = float.PositiveInfinity;
            point = default;
            foreach (var collider in colliders)
                if (collider.Raycast(new Ray(origin, direction), out var hit, length) && hit.distance < distance)
                {
                    distance = hit.distance;
                    point = hit.point;
                }
            return !float.IsPositiveInfinity(distance);
        }

        internal static Vector3[] Mounts(GameObject model, Bounds body)
        {
            var frame = model.transform;
            var colliders = Colliders(
                model.GetComponentsInChildren<MeshFilter>(true).Where(
                    f => f.name == "Body_OldOak" || f.name == "Body_HammeredSteel"
                )
            );
            var result = new Vector3[7];
            float radius = body.size.x * .13f * .3f;
            try
            {
                for (int slot = 0; slot < result.Length; slot++)
                {
                    var nominal = new Vector3(
                        body.center.x + Mathf.Lerp(-.4f, .4f, slot / 6f) * body.size.x,
                        body.max.y,
                        body.center.z + body.extents.z * .55f
                    );
                    bool found = false;
                    string reason = "no candidates";
                    var shifts = new[] { 0f, .02f, -.02f, .04f, -.04f, .06f, -.06f, .08f, -.08f };
                    var candidates = shifts.SelectMany(
                        x => new[] { 0f, .02f, -.02f, .04f, -.04f }.Select(
                            z => new Vector3(x * body.size.x, 0, z * body.size.z)
                        )
                    ).OrderBy(
                        v => v.sqrMagnitude
                    );
                    foreach (var shift in candidates)
                    {
                        var position = nominal + shift;
                        float high = float.NegativeInfinity, low = float.PositiveInfinity;
                        bool complete = true;
                        for (int sample = 0; sample < 13; sample++)
                        {
                            float angle = (sample - 1) * Mathf.PI / 6;
                            var point = position + (sample == 0
                                ? Vector3.zero
                                : new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
                            point.y = body.max.y + body.size.x * .002f;
                            if (!Cast(
                                colliders,
                                frame.TransformPoint(point),
                                -frame.up,
                                frame.TransformVector(Vector3.up * (body.size.y + 1)).magnitude,
                                out var hit
                            ))
                            {
                                complete = false;
                                break;
                            }
                            float y = frame.InverseTransformPoint(hit).y;
                            high = Mathf.Max(high, y);
                            low = Mathf.Min(low, y);
                        }
                        reason = "colliders=" + colliders.Count + ", complete=" + complete + ", heightRange="
                            + (high - low);
                        if (complete && high - low <= body.size.x / 1000f)
                        {
                            position.y = high;
                            result[slot] = position;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        throw new InvalidOperationException("No supported pedestal position for slot " + slot + ": "
                            + reason);
                }
            }
            finally
            {
                Release(colliders);
            }
            return result;
        }

        internal static bool VisibleContact(MeshFilter filter, RaycastHit hit)
        => VisibleContact(filter, hit.triangleIndex, hit.textureCoord);

        private static bool VisibleContact(MeshFilter filter, int triangle, Vector2 coordinates)
        {
            int sub = 0;
            for (; sub < filter.sharedMesh.subMeshCount - 1; sub++)
            {
                int count = (int)filter.sharedMesh.GetIndexCount(sub) / 3;
                if (triangle < count)
                    break;
                triangle -= count;
            }
            var materials = filter.GetComponent<Renderer>().sharedMaterials;
            if (sub >= materials.Length || !materials[sub])
                return true;
            var material = materials[sub];
            if (!material.HasProperty("_Cutoff") || !material.HasProperty("_MainTex"))
                return true;
            var source = material.GetTexture("_MainTex");
            if (!source)
                return true;
            if (!readableTextures.TryGetValue(source, out var texture))
            {
                var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(source, target);
                    RenderTexture.active = target;
                    texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                    texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                    texture.Apply();
                    readableTextures[source] = texture;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                }
            }
            var uv = Vector2.Scale(coordinates, material.GetTextureScale("_MainTex"))
                + material.GetTextureOffset("_MainTex");
            return texture.GetPixelBilinear(uv.x, uv.y).a >= material.GetFloat("_Cutoff");
        }

        internal static IEnumerable<Vector3[]> Triangles(Transform root, Transform frame)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                Mesh temporary = null;
                try
                {
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        temporary = new Mesh();
                        skin.BakeMesh(temporary, false);
                        mesh = temporary;
                    }
                    else
                    {
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (!filter || !filter.sharedMesh)
                            continue;
                        mesh = CollisionMesh(filter);
                    }
                    var vertices = mesh.vertices;
                    var indices = mesh.triangles;
                    var matrix = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    for (int i = 0; i < indices.Length; i += 3)
                        yield return new[] {
                            matrix.MultiplyPoint3x4(vertices[indices[i]]),
                            matrix.MultiplyPoint3x4(vertices[indices[i + 1]]),
                            matrix.MultiplyPoint3x4(vertices[indices[i + 2]])
                        };
                }
                finally
                {
                    if (temporary)
                        UnityEngine.Object.Destroy(temporary);
                }
            }
        }
    }
}
