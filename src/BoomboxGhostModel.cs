using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal sealed class BoomboxGhostModel
    {
        internal const float Opacity = .45f;
        private const float CoreOpacity = .8f;
        private readonly GameObject root;
        private readonly float glowOpacity;
        private readonly float coreOpacity;
        private readonly Material glow;
        private readonly List<Material> cores = new List<Material>();
        private readonly List<Mesh> baked = new List<Mesh>();
        private readonly List<Renderer> renderers = new List<Renderer>();

        internal BoomboxGhostModel(
            GameObject source,
            Vector3 pivot,
            string name,
            bool textured,
            float glowOpacity = Opacity,
            float coreOpacity = CoreOpacity
        )
        {
            this.glowOpacity = glowOpacity;
            this.coreOpacity = coreOpacity;
            root = new GameObject(name);
            root.transform.position = pivot;
            glow = BoomboxParticleAssets.Ghost();
            foreach (var renderer in source.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    mesh = new Mesh { name = "Boombox ghost bake" };
                    skin.BakeMesh(mesh, true);
                    baked.Add(mesh);
                }
                else if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() is MeshFilter filter)
                    mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;
                var sourceMaterial = renderer.sharedMaterial;
                var core = BoomboxParticleAssets.GhostCore(
                    textured && sourceMaterial != null && sourceMaterial.HasProperty("_MainTex")
                        ? sourceMaterial.mainTexture
                        : null
                );
                cores.Add(core);
                Part(renderer, mesh, core, "Core");
                Part(renderer, mesh, glow, "Glow");
            }
            glow.renderQueue = cores.Count > 0 ? cores[0].renderQueue + 1 : glow.renderQueue;
            Center = Bounds.center;
            Show(0f);
        }

        internal Transform Root => root.transform;

        internal Vector3 Center
        {
            get;
        }

        internal bool Empty => renderers.Count == 0;

        internal Bounds Bounds
        {
            get
            {
                if (renderers.Count == 0)
                    return new Bounds(root.transform.position, Vector3.zero);
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Count; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }
        }

        internal void Show(float opacity)
        {
            if (BoomboxPlugin.Instance?.Settings?.DrawEnchantment != true)
            {
                root.SetActive(false);
                return;
            }
            float brightness = BoomboxParticleLook.Brightness;
            float share = Mathf.Clamp01(opacity / Opacity);
            var blue = BoomboxEnchantSystems.Blue;
            var pale = BoomboxEnchantSystems.Pale;
            glow.SetColor(
                "_TintColor",
                new Color(
                    .5f * blue.r * brightness,
                    .5f * blue.g * brightness,
                    .5f * blue.b * brightness,
                    .5f * glowOpacity * share
                )
            );
            foreach (var core in cores)
                core.SetColor(
                    "_TintColor",
                    new Color(
                        .5f * pale.r * brightness,
                        .5f * pale.g * brightness,
                        .5f * pale.b * brightness,
                        .5f * coreOpacity * share
                    )
                );
            root.SetActive(share > 0f);
        }

        internal void Destroy()
        {
            Object.Destroy(root);
            Object.Destroy(glow);
            foreach (var core in cores)
                Object.Destroy(core);
            foreach (var mesh in baked)
                Object.Destroy(mesh);
        }

        private void Part(Renderer renderer, Mesh mesh, Material material, string layer)
        {
            var part = new GameObject(renderer.name + layer);
            part.transform.SetParent(root.transform, false);
            part.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            part.transform.localScale = renderer is SkinnedMeshRenderer ? Vector3.one : renderer.transform.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var copy = part.AddComponent<MeshRenderer>();
            copy.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, mesh.subMeshCount)).ToArray();
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderers.Add(copy);
        }
    }
}
