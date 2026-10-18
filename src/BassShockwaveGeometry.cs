using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimBoombox.Visuals
{
    internal sealed class BassShockwaveEdge
    {
        private const int Segments = 96;
        private const int Rows = 5;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly Vector3[] _vertices = new Vector3[(Segments + 1) * Rows];
        private readonly Color32[] _colors = new Color32[(Segments + 1) * Rows];
        private readonly float[] _rowPosition = { 0f, 0.22f, 0.62f, 0.91f, 1f };
        private readonly float[] _rowHeight = { 0f, 0.3f, 1f, 0.25f, 0f };
        private readonly float[] _rowAlpha = { 0f, 0.32f, 1f, 0.6f, 0f };
        private readonly Material _material;

        public BassShockwaveEdge(
            Transform parent,
            Material source,
            Material fallback,
            Color tint,
            List<UnityEngine.Object> owned
        )
        {
            Material template = source;
            if (template == null || template.shader == null || !template.shader.isSupported
            || template.shader.name == "Hidden/InternalErrorShader")
                template = fallback;
            if (template == null)
                throw new InvalidOperationException("A supported transparent material is required for the bass front.");
            Material material = new Material(
                template
            )
            {
                name = "Bass narrow textured edge",
                hideFlags = HideFlags.DontSave
            };
            _material = material;
            owned.Add(material);
            if (source != null && source.HasProperty("_MainTex") && material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                Texture texture = source.GetTexture("_MainTex");
                material.mainTextureScale = new Vector2(
                    texture != null && texture.wrapMode == TextureWrapMode.Repeat
                        ? 3f
                        : 1f,
                    1f
                );
                material.mainTextureOffset = Vector2.zero;
            }
            SetTint(tint);
            material.renderQueue = 3010;
            Vector2[] uv = new Vector2[_vertices.Length];
            Vector3[] normals = new Vector3[_vertices.Length];
            int[] triangles = new int[(Rows - 1) * Segments * 6];
            for (int row = 0; row < Rows; row++)
            {
                for (int segment = 0; segment <= Segments; segment++)
                {
                    int index = row * (Segments + 1) + segment;
                    uv[index] = new Vector2(segment / (float)Segments, 0.35f + row / (float)(Rows - 1) * 0.65f);
                    normals[index] = Vector3.up;
                    if (row == Rows - 1 || segment == Segments)
                        continue;
                    int triangle = (row * Segments + segment) * 6;
                    triangles[triangle] = index;
                    triangles[triangle + 1] = index + 1;
                    triangles[triangle + 2] = index + Segments + 1;
                    triangles[triangle + 3] = index + 1;
                    triangles[triangle + 4] = index + Segments + 2;
                    triangles[triangle + 5] = index + Segments + 1;
                }
            }
            _mesh = new Mesh { name = "Bass textured front edge", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.normals = normals;
            _mesh.uv = uv;
            _mesh.triangles = triangles;
            owned.Add(_mesh);
            GameObject child = new GameObject("Narrow textured front") { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = child.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public void Update(float radius, float maxRadius, float age)
        {
            float desiredWidth = Mathf.Lerp(0.15f, 0.25f, Mathf.Clamp01(radius / maxRadius));
            float width = Mathf.Min(desiredWidth, radius * 0.4f);
            float inner = radius - width;
            float height = Mathf.Min(0.075f, width * 0.3f);
            float attack = Mathf.SmoothStep(0f, 1f, age / 0.065f);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1f, age));
            for (int row = 0; row < Rows; row++)
            {
                float radial = Mathf.Lerp(inner, radius, _rowPosition[row]);
                for (int segment = 0; segment <= Segments; segment++)
                {
                    float angle = segment / (float)Segments * Mathf.PI * 2f;
                    float ripple = 0.85f + 0.15f * Mathf.Sin(angle * 9f + age * 5f);
                    float elevation = height * _rowHeight[row] * ripple;
                    elevation = Mathf.Min(
                        elevation,
                        Mathf.Sqrt(Mathf.Max(0f, radius * radius - radial * radial)) * 0.8f
                    );
                    int index = row * (Segments + 1) + segment;
                    _vertices[index] = new Vector3(Mathf.Cos(angle) * radial, elevation, Mathf.Sin(angle) * radial);
                    _colors[index] = new Color(1f, 1f, 1f, _rowAlpha[row] * attack * fade);
                }
            }
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.RecalculateBounds();
            _renderer.enabled = radius > 0.005f && attack * fade > 0.001f;
        }

        public void SetVisible(bool visible)
        {
            _renderer.enabled = visible && _renderer.enabled;
        }

        public void SetTint(Color tint)
        {
            string property = _material.HasProperty("_TintColor")
                ? "_TintColor"
                : _material.HasProperty("_Color")
                    ? "_Color"
                    : null;
            if (property != null)
                _material.SetColor(property, new Color(tint.r, tint.g, tint.b, 0.6f * tint.a));
        }
    }
}
