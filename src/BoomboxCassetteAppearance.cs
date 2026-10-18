using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxCassetteAppearance : MonoBehaviour
    {
        private int mask = -1;
        private Mesh ownedMesh;
        private Material[] ownedMaterials;
        private Material[] backingMaterials;
        private static readonly Color[] palette = {
            new Color(.42f, .045f, .025f),
            new Color(.62f, .39f, .035f),
            new Color(.065f, .31f, .095f),
            new Color(.04f, .18f, .43f),
            new Color(.43f, .12f, .66f)
        };

        public static void Apply(GameObject model, IEnumerable<string> properties)
        {
            var wanted = new HashSet<string>(properties);
            int value = 0;
            for (int i = 0; i < BoomboxItemData.Properties.Length; i++)
                if (wanted.Contains(BoomboxItemData.Properties[i]))
                    value |= 1 << i;
            var appearance = model.GetComponent<BoomboxCassetteAppearance>()
                ?? model.AddComponent<BoomboxCassetteAppearance>();
            appearance.Show(value);
        }

        public static void Apply(GameObject model, RecordedCassette cassette)
        {
            if (model.GetComponent<VikingBoomboxVisualState>() != null)
            {
                var embedded = model.GetComponent<BoomboxEmbeddedCassetteAppearance>()
                    ?? model.AddComponent<BoomboxEmbeddedCassetteAppearance>();
                embedded.Apply(cassette);
                return;
            }
            int value = 0;
            for (int i = 0; i < BoomboxItemData.Properties.Length; i++)
                if (cassette?.Rank(BoomboxItemData.Properties[i]) > 0)
                    value |= 1 << i;
            var appearance = model.GetComponent<BoomboxCassetteAppearance>()
                ?? model.AddComponent<BoomboxCassetteAppearance>();
            appearance.Show(value);
        }

        private void Show(int value)
        {
            if (mask == value)
                return;
            mask = value;
            foreach (var node in GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("Property", StringComparison.Ordinal))
                    node.gameObject.SetActive(false);
            var previous = transform.Find("CassetteBands");
            if (previous != null)
                DestroyImmediate(previous.gameObject);
            Release();
            if (backingMaterials == null)
            {
                var backing = new List<Material>();
                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    bool property = false;
                    for (var ancestor = renderer.transform; ancestor != transform
                        && ancestor != null; ancestor = ancestor.parent)
                        if (ancestor.name.StartsWith("Property", StringComparison.Ordinal))
                            property = true;
                    if (property)
                        continue;
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                    {
                        if (!original.name.StartsWith("CassetteLabel", StringComparison.Ordinal))
                            return original;
                        var material = new Material(original) { name = "Cassette label backing" };
                        VikingBoomboxVisualState.Color(material, new Color(.1f, .105f, .11f));
                        backing.Add(material);
                        return material;
                    }).ToArray();
                }
                backingMaterials = backing.ToArray();
            }
            var selected = Enumerable.Range(0, palette.Length).Where(i => (value & (1 << i)) != 0).ToArray();
            if (selected.Length == 0)
                return;
            var template = GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).First(
                m => m.name.StartsWith("CassetteLabel", StringComparison.Ordinal)
            );
            ownedMaterials = selected.Select(i =>
            {
                var material = new Material(template) { name = "Cassette band " + BoomboxItemData.Properties[i] };
                VikingBoomboxVisualState.Color(material, palette[i]);
                return material;
            }).ToArray();
            var parentToLocal = Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale)
                .inverse;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int[]>();
            float width = .079f / selected.Length;
            for (int index = 0; index < selected.Length; index++)
            {
                var indices = new List<int>();
                float left = -.0395f + index * width, right = left + width;
                if (selected.Length > 1)
                {
                    left += .00025f;
                    right -= .00025f;
                }
                foreach (float side in new[] { -1f, 1f })
                    foreach (var row in new[] { new Vector2(.052f, .011f), new Vector2(.018f, .0035f) })
                    {
                        int start = vertices.Count;
                        float bottom = row.x - row.y / 2, top = row.x + row.y / 2;
                        foreach (var point in new[] {
                            new Vector3(left, bottom, side * .008f),
                            new Vector3(right, bottom, side * .008f),
                            new Vector3(right, top, side * .008f),
                            new Vector3(left, top, side * .008f)
                        })
                            vertices.Add(parentToLocal.MultiplyPoint3x4(point));
                        for (int n = 0; n < 4; n++)
                            normals.Add(parentToLocal.MultiplyVector(Vector3.forward * side).normalized);
                        uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                        if (side > 0)
                            indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                        else
                            indices.AddRange(new[] { start + 2, start + 1, start, start + 3, start + 2, start });
                    }
                triangles.Add(indices.ToArray());
            }
            ownedMesh = new Mesh { name = "Cassette property bands" };
            ownedMesh.SetVertices(vertices);
            ownedMesh.SetNormals(normals);
            ownedMesh.SetUVs(0, uv);
            ownedMesh.subMeshCount = selected.Length;
            for (int i = 0; i < triangles.Count; i++)
                ownedMesh.SetTriangles(triangles[i], i);
            ownedMesh.RecalculateBounds();
            ownedMesh.RecalculateTangents();
            var bands = new GameObject("CassetteBands");
            bands.transform.SetParent(transform, false);
            bands.AddComponent<MeshFilter>().sharedMesh = ownedMesh;
            bands.AddComponent<MeshRenderer>().sharedMaterials = ownedMaterials;
        }

        private void Release()
        {
            if (ownedMesh != null)
                Destroy(ownedMesh);
            if (ownedMaterials != null)
                foreach (var material in ownedMaterials)
                    if (material != null)
                        Destroy(material);
            ownedMesh = null;
            ownedMaterials = null;
        }
        private void OnDestroy()
        {
            Release();
            if (backingMaterials != null)
                foreach (var material in backingMaterials)
                    if (material != null)
                        Destroy(material);
        }
    }
}
