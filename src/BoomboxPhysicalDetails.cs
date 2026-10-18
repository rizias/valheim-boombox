using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxPhysicalDetails : MonoBehaviour
    {
        private sealed class Revision
        {
            internal MeshFilter filter;
            internal Mesh original;
            internal Mesh replacement;
            internal MeshRenderer renderer;
            internal Material[] materials;
        }
        private sealed class Region
        {
            internal string name;
            internal Bounds bounds;
            internal Color color;
            internal bool glyphs;
            internal bool removed;
        }
        private readonly List<Revision> revisions = new List<Revision>();
        private readonly List<Material> materials = new List<Material>();
        private Mesh patternMesh;
        private MeshRenderer patternRenderer;
        private Material patternMaterial;
        private Texture2D patternTexture;
        private MaterialPropertyBlock patternProperties;
        public int ButtonTriangles
        {
            get;
            private set;
        }
        public int SymbolTriangles
        {
            get;
            private set;
        }

        internal void Initialize(Transform frame)
        {
            if (frame == null)
                throw new InvalidOperationException("Control detail frame is missing from the model.");
            Split(
                frame,
                "Body_HammeredSteel",
                new Region
                {
                    name = "Transport button faces",
                    bounds = Box(new Vector3(-.069f, -.130f, .144f), new Vector3(.069f, -.110f, .180f)),
                    color = new Color(.36f, .35f, .32f)
                }
            );
            Split(
                frame,
                "Body_RecessBlack",
                new Region
                {
                    name = "Transport symbols",
                    bounds = Box(new Vector3(-.069f, -.130f, .149f), new Vector3(.069f, -.123f, .174f)),
                    color = new Color(.035f, .028f, .018f),
                    glyphs = true
                },
                new Region
                {
                    name = "Passive cassette ward",
                    bounds = Box(new Vector3(-.020f, -.115f, .289f), new Vector3(.020f, -.105f, .328f)),
                    removed = true
                }
            );
            AddRamsundDragonHead(frame);
        }

        private static Bounds Box(Vector3 min, Vector3 max) => new Bounds((min + max) * .5f, max - min);

        private void Split(Transform frame, string name, params Region[] regions)
        {
            var filter = frame.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == name);
            var renderer = filter.GetComponent<MeshRenderer>();
            var source = filter.sharedMesh;
            var mesh = Instantiate(source);
            mesh.name = source.name + " readable details";
            var vertices = mesh.vertices;
            var toFrame = frame.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var fromFrame = toFrame.inverse;
            var points = vertices.Select(toFrame.MultiplyPoint3x4).ToArray();
            int originalCount = source.subMeshCount;
            var kept = new List<int>[originalCount];
            var selected = regions.Select(_ => new List<int>()).ToArray();
            for (int sub = 0; sub < originalCount; sub++)
            {
                kept[sub] = new List<int>();
                var triangles = source.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int group = Array.FindIndex(
                        regions,
                        r => r.bounds.Contains(points[triangles[i]]) && r.bounds.Contains(points[triangles[i + 1]])
                        && r.bounds.Contains(points[triangles[i + 2]])
                    );
                    if (group < 0)
                    {
                        kept[sub].Add(triangles[i]);
                        kept[sub].Add(triangles[i + 1]);
                        kept[sub].Add(triangles[i + 2]);
                    }
                    else if (!regions[group].removed)
                    {
                        selected[group].Add(triangles[i]);
                        selected[group].Add(triangles[i + 1]);
                        selected[group].Add(triangles[i + 2]);
                    }
                }
            }
            var added = new List<Material>();
            for (int group = 0; group < regions.Length; group++)
            {
                var region = regions[group];
                if (region.removed)
                    continue;
                var material = BoomboxMaterials.Surface(gameObject, region.color, "HammeredSteel");
                material.name = region.name;
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", Texture2D.whiteTexture);
                if (material.HasProperty("_BumpMap"))
                    material.SetTexture("_BumpMap", null);
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", Color.black);
                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", region.glyphs ? 0f : .2f);
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness", .25f);
                material.DisableKeyword("_EMISSION");
                material.DisableKeyword("_NORMALMAP");
                added.Add(material);
                materials.Add(material);
                foreach (int vertex in selected[group].Distinct())
                {
                    var p = points[vertex];
                    if (region.glyphs)
                    {
                        int button = Mathf.Clamp(Mathf.RoundToInt((p.x + .05182128f) / .03454752f), 0, 3);
                        float center = -.05182128f + button * .03454752f;
                        p.x = center + (p.x - center) * 1.2f;
                        p.z = .162f + (p.z - .162f) * 1.2f;
                    }
                    vertices[vertex] = fromFrame.MultiplyPoint3x4(p);
                }
                if (region.glyphs)
                    SymbolTriangles = selected[group].Count / 3;
                else
                    ButtonTriangles = selected[group].Count / 3;
            }
            mesh.vertices = vertices;
            mesh.subMeshCount = originalCount + added.Count;
            for (int sub = 0; sub < originalCount; sub++)
                mesh.SetTriangles(kept[sub], sub);
            int detailSubmesh = originalCount;
            for (int group = 0; group < regions.Length; group++)
                if (!regions[group].removed)
                    mesh.SetTriangles(selected[group], detailSubmesh++);
            mesh.RecalculateBounds();
            revisions.Add(
                new Revision
                {
                    filter = filter,
                    original = source,
                    replacement = mesh,
                    renderer = renderer,
                    materials = renderer.sharedMaterials
                }
            );
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = renderer.sharedMaterials.Concat(added).ToArray();
        }

        private void AddRamsundDragonHead(Transform frame)
        {
            using (var stream = typeof(BoomboxPhysicalDetails).Assembly.GetManifestResourceStream(
                "ValheimBoombox.RamsundDragonHead.png"
            ))
            {
                if (stream == null)
                    throw new InvalidOperationException("Ramsund dragon-head resource is missing.");
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    patternTexture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (!patternTexture.LoadImage(buffer.ToArray(), true))
                        throw new InvalidOperationException("Ramsund dragon-head resource is invalid.");
                }
            }
            patternTexture.name = "Ramsund dragon head mask";
            patternTexture.wrapMode = TextureWrapMode.Clamp;
            patternTexture.filterMode = FilterMode.Bilinear;
            patternMesh = new Mesh { name = "Ramsund dragon head decal" };
            const float width = .103f, bottom = .2815f;
            float height = width / patternTexture.width * patternTexture.height;
            float left = -width * .5f;
            patternMesh.vertices = new[] {
                new Vector3(left, -.118f, bottom),
                new Vector3(left + width, -.118f, bottom),
                new Vector3(left + width, -.118f, bottom + height),
                new Vector3(left, -.118f, bottom + height)
            };
            patternMesh.uv = new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) };
            patternMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            patternMesh.RecalculateNormals();
            patternMesh.RecalculateBounds();
            var ornament = new GameObject("RamsundDragonHead").transform;
            ornament.SetParent(frame, false);
            ornament.gameObject.AddComponent<MeshFilter>().sharedMesh = patternMesh;
            patternRenderer = ornament.gameObject.AddComponent<MeshRenderer>();
            patternMaterial = new Material(BoomboxWaveMaterials.Alpha)
            {
                name = "Ramsund dragon head color",
                mainTexture = patternTexture,
                color = BoomboxPlugin.Instance.Settings.PatternColor.Value
            };
            patternRenderer.sharedMaterial = patternMaterial;
            patternProperties = new MaterialPropertyBlock();
            ApplyPattern(0f);
        }

        internal void ApplyPattern(float strength)
        {
            if (patternRenderer == null || patternMaterial == null)
                return;
            var settings = BoomboxPlugin.Instance.Settings;
            Color color = settings.PatternColor.Value;
            patternRenderer.GetPropertyBlock(patternProperties);
            patternProperties.SetColor(
                "_Color",
                color * (settings.ShowVisualEffects.Value && settings.PatternPulse.Value
                    ? 1f + Mathf.Clamp01(strength) * 1.5f
                    : 1f)
            );
            patternRenderer.SetPropertyBlock(patternProperties);
        }

        private void OnDestroy()
        {
            foreach (var revision in revisions)
            {
                if (revision.filter)
                    revision.filter.sharedMesh = revision.original;
                if (revision.renderer)
                    revision.renderer.sharedMaterials = revision.materials;
                if (revision.replacement)
                    Destroy(revision.replacement);
            }
            foreach (var material in materials)
                if (material)
                    Destroy(material);
            if (patternMesh)
                Destroy(patternMesh);
            if (patternMaterial)
                Destroy(patternMaterial);
            if (patternTexture)
                Destroy(patternTexture);
        }
    }
}
