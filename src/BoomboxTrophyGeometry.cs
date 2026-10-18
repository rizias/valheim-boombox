using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class BoomboxTrophyVisuals
    {
        private static Bounds StandingBounds(Transform item, Transform frame)
        {
            var result = new Bounds();
            bool found = false;
            foreach (var triangle in BoomboxHeadPlacement.Triangles(item, frame))
                foreach (var point in triangle)
                    if (!found)
                    {
                        result = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                        result.Encapsulate(point);
            return result;
        }
        private static readonly Dictionary<string, string> Creatures = new Dictionary<string, string>(
            System.StringComparer.Ordinal
        )
        {
            { "TrophyForestTroll", "Troll" },
            { "TrophyFrostTroll", "FrostTroll" },
            { "TrophyGoblin", "Goblin" },
            { "TrophyGoblinBrute", "GoblinBrute" },
            { "TrophyGoblinShaman", "GoblinShaman" },
            { "TrophyGoblinKing", "GoblinKing" },
            { "TrophySkeleton", "Skeleton" },
            { "TrophySkeletonPoison", "Skeleton_Poison" },
            { "TrophyDraugr", "Draugr" },
            { "TrophyDraugrElite", "Draugr_Elite" },
            { "TrophyDraugrFem", "Draugr_Fem" },
            { "TrophyGreydwarf", "Greydwarf" },
            { "TrophyGreydwarfShaman", "Greydwarf_Shaman" },
            { "TrophyGreydwarfBrute", "Greydwarf_Elite" },
            { "TrophyFenring", "Fenring" },
            { "TrophyWraith", "Wraith" },
            { "TrophySeeker", "Seeker" },
            { "TrophySeekerBrute", "SeekerBrute" },
            { "TrophyAbomination", "Abomination" },
            { "TrophyLox", "Lox" },
            { "TrophyWolf", "Wolf" },
            { "TrophyBoar", "Boar" },
            { "TrophyHatchling", "Hatchling" },
            { "TrophySurtling", "Surtling" },
            { "TrophyTick", "Tick" },
            { "TrophySerpent", "Serpent" },
            { "TrophyEikthyr", "Eikthyr" },
            { "TrophyBonemass", "Bonemass" },
            { "TrophyDragonQueen", "Dragon" },
            { "TrophyTheElder", "gd_king" },
            { "TrophyFader", "Fader" }
        };
        private static ZNetScene creatureScene;
        private static int creatureCount;
        private static readonly Dictionary<string, GameObject> nativeDrops = new Dictionary<string, GameObject>(
            System.StringComparer.Ordinal
        );
        internal static GameObject CreaturePrefab(string trophy)
        {
            var scene = ZNetScene.instance;
            if (scene == null)
                return null;
            if (Creatures.TryGetValue(trophy, out var name))
            {
                var preferred = scene.GetPrefab(name);
                if (preferred != null)
                    return preferred;
            }
            if (creatureScene != scene || creatureCount != scene.m_prefabs.Count)
            {
                creatureScene = scene;
                creatureCount = scene.m_prefabs.Count;
                nativeDrops.Clear();
                foreach (var prefab in scene.m_prefabs)
                {
                    if (prefab == null)
                        continue;
                    var drops = prefab.GetComponent<CharacterDrop>();
                    if (drops == null)
                        continue;
                    foreach (var drop in drops.m_drops)
                        if (drop.m_prefab != null && !nativeDrops.ContainsKey(drop.m_prefab.name))
                            nativeDrops.Add(drop.m_prefab.name, prefab);
                }
            }
            return nativeDrops.TryGetValue(trophy, out var creature) ? creature : null;
        }
        private static Transform Shape(
            Transform parent,
            string name,
            Mesh mesh,
            Material material,
            Vector3 scale,
            Vector3 position
        )
        {
            var part = new GameObject(name).transform;
            part.SetParent(parent, false);
            part.localPosition = position;
            part.localScale = scale;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return part;
        }
        internal static Mesh Cylinder()
        {
            if (cylinder != null)
                return cylinder;
            const int sides = 12;
            var vertices = new Vector3[sides * 2 + 2];
            var triangles = new int[sides * 12];
            vertices[sides * 2] = Vector3.up * .5f;
            vertices[sides * 2 + 1] = Vector3.down * .5f;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                vertices[i * 2] = new Vector3(Mathf.Cos(a) * .5f, -.5f, Mathf.Sin(a) * .5f);
                vertices[i * 2 + 1] = vertices[i * 2] + Vector3.up;
                int n = (i + 1) % sides, k = i * 12;
                int[] face = {
                    i * 2,
                    i * 2 + 1,
                    n * 2 + 1,
                    i * 2,
                    n * 2 + 1,
                    n * 2,
                    sides * 2,
                    n * 2 + 1,
                    i * 2 + 1,
                    sides * 2 + 1,
                    i * 2,
                    n * 2
                };
                for (int j = 0; j < 12; j++)
                    triangles[k + j] = face[j];
            }
            cylinder = new Mesh { name = "BoomboxFigurineBody" };
            cylinder.vertices = vertices;
            cylinder.triangles = triangles;
            cylinder.RecalculateNormals();
            cylinder.RecalculateBounds();
            return cylinder;
        }
        private Transform CloneVisual(Transform source, Transform parent)
        {
            var wrapper = new GameObject("NativeDecoration").transform;
            wrapper.SetParent(parent, false);
            wrapper.gameObject.SetActive(false);
            var copyRoot = Instantiate(ItemStand.GetAttachGameObject(source.gameObject), wrapper, false).transform;
            bool whole = source.parent == null;
            if (whole)
            {
                BoomboxPieceLook.Freeze(copyRoot, wrapper.gameObject);
                BoomboxPieceLook.NearestOnly(copyRoot);
                BoomboxPieceLook.Still(copyRoot, wrapper.gameObject);
            }
            copyRoot.localPosition = whole ? Vector3.zero : source.localPosition;
            copyRoot.localRotation = whole ? Quaternion.identity : source.localRotation;
            copyRoot.localScale = source.localScale;
            copyRoot.gameObject.SetActive(true);
            var working = BoomboxPieceLook.Working(copyRoot);
            foreach (var audio in wrapper.GetComponentsInChildren<AudioSource>(true))
            {
                audio.enabled = false;
                DestroyImmediate(audio);
            }
            foreach (var collider in wrapper.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                DestroyImmediate(collider);
            }
            foreach (var joint in wrapper.GetComponentsInChildren<Joint>(true))
                DestroyImmediate(joint);
            foreach (var body in wrapper.GetComponentsInChildren<Rigidbody>(true))
                DestroyImmediate(body);
            foreach (var light in wrapper.GetComponentsInChildren<Light>(true))
                light.enabled = false;
            foreach (var behaviour in wrapper.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is ItemStyle) && !(behaviour is SoftReferenceableAssets.ReferenceHolder)
                && !(behaviour is BoomboxOwnedAssets))
                    DestroyImmediate(behaviour);
            foreach (var particles in wrapper.GetComponentsInChildren<ParticleSystem>(true)
                .Where(p => !working.Contains(p)))
            {
                var main = particles.main;
                main.playOnAwake = false;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (source.GetComponentsInParent<Transform>(true).Any(t => t.name == "TrophyForestTroll"))
                foreach (var filter in wrapper.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        filter.sharedMesh = NativeTrollHead();
                    var skinMaterial = NativeTrollMaterial();
                    if (skinMaterial != null && filter.GetComponent<MeshRenderer>() is MeshRenderer renderer
                    && !renderer.sharedMaterials.Any(ValidMaterial))
                        renderer.sharedMaterials = Enumerable.Repeat(
                            skinMaterial,
                            Mathf.Max(
                                1,
                                filter.sharedMesh != null
                                    ? filter.sharedMesh.subMeshCount
                                    : 1
                            )
                        ).ToArray();
                }
            foreach (var lod in wrapper.GetComponentsInChildren<LODGroup>(true))
            {
                lod.ForceLOD(0);
            }
            wrapper.gameObject.SetActive(true);
            wrapper.GetComponentInChildren<ItemStyle>()?.Setup(0);
            foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return wrapper;
        }
        private static bool ValidMaterial(
            Material material
        ) => material != null && material.shader != null && material.shader.isSupported
        && !material.shader.name.Contains("InternalErrorShader");
        internal static Mesh NativeTrollHead()
        {
            if (nativeTrollHead != null)
                return nativeTrollHead;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                var path = bundle.GetAllAssetNames().FirstOrDefault(
                    n => n.EndsWith("/trophy_troll.obj", System.StringComparison.OrdinalIgnoreCase)
                );
                if (path == null)
                    continue;
                var model = bundle.LoadAsset<GameObject>(path);
                if (model != null)
                    nativeTrollHead = model.GetComponentsInChildren<MeshFilter>(true)
                        .Select(f => f.sharedMesh)
                        .FirstOrDefault(
                        m => m != null && m.vertexCount > 0
                    );
                if (nativeTrollHead != null)
                    return nativeTrollHead;
                var meshes = bundle.LoadAssetWithSubAssets<Mesh>(path);
                nativeTrollHead = meshes.FirstOrDefault(m => m != null
                    && m.name == "head")
                    ?? meshes.FirstOrDefault(m => m != null
                    && m.vertexCount > 0);
                if (nativeTrollHead != null)
                    return nativeTrollHead;
            }
            return null;
        }
        internal static bool NativeTrollMaterialAvailable()
        {
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle.GetAllAssetNames().Any(
                    n => n.EndsWith(
                        "/characters/troll/model/material/troll.mat",
                        System.StringComparison.OrdinalIgnoreCase
                    )
                ))
                    return true;
            var troll = ZNetScene.instance?.GetPrefab("Troll");
            return troll == null
                || troll.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Any(ValidMaterial)
                || BoomboxPlugin.Instance != null;
        }
        private Material NativeTrollMaterial()
        {
            if (nativeTrollMaterial != null)
                return nativeTrollMaterial;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                var path = bundle.GetAllAssetNames().FirstOrDefault(
                    n => n.EndsWith(
                        "/characters/troll/model/material/troll.mat",
                        System.StringComparison.OrdinalIgnoreCase
                    )
                );
                if (path == null)
                    continue;
                var material = bundle.LoadAsset<Material>(path);
                if (material != null && material.shader != null && material.shader.isSupported
                && !material.shader.name.Contains("InternalErrorShader"))
                {
                    nativeTrollMaterial = new Material(material);
                    return nativeTrollMaterial;
                }
            }
            var troll = ZNetScene.instance?.GetPrefab("Troll");
            var native = troll == null
                ? null
                : troll.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).FirstOrDefault(
                m => m != null
                        && m.shader != null
                        && m.shader.isSupported
                        && !m.shader.name.Contains("InternalErrorShader")
            );
            nativeTrollMaterial = native != null
                ? new Material(native)
                : BoomboxMaterials.Surface(visual, Color.white, "OldOak");
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                var path = bundle.GetAllAssetNames().FirstOrDefault(
                    n => n.EndsWith(
                        "/characters/troll/model/material/troll_diffuse.png",
                        System.StringComparison.OrdinalIgnoreCase
                    )
                );
                if (path == null)
                    continue;
                var texture = bundle.LoadAsset<Texture2D>(path);
                if (texture != null && nativeTrollMaterial.HasProperty("_MainTex"))
                    nativeTrollMaterial.SetTexture("_MainTex", texture);
                break;
            }
            if (nativeTrollMaterial.HasProperty("_BumpMap"))
                nativeTrollMaterial.SetTexture("_BumpMap", null);
            if (nativeTrollMaterial.HasProperty("_EmissionColor"))
                nativeTrollMaterial.SetColor("_EmissionColor", Color.black);
            if (nativeTrollMaterial.HasProperty("_Metallic"))
                nativeTrollMaterial.SetFloat("_Metallic", 0);
            return nativeTrollMaterial;
        }
        private static Bounds BoundsOf(Transform item, Transform frame)
        {
            var result = new Bounds();
            bool found = false;
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
            {
                if (!BoomboxItemPrefabs.VisibleUnder(renderer.transform, item))
                    continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (!(renderer is SkinnedMeshRenderer) && mesh == null)
                    continue;
                var bounds = renderer is SkinnedMeshRenderer skin ? skin.localBounds : mesh.bounds;
                var matrix = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                if (!(renderer is SkinnedMeshRenderer) && mesh != null && mesh.isReadable)
                {
                    var vertices = mesh.vertices;
                    int included = 0;
                    foreach (var index in mesh.triangles.Distinct())
                    {
                        var vertex = vertices[index];
                        if (!bounds.Contains(vertex))
                            continue;
                        included++;
                        var point = matrix.MultiplyPoint3x4(vertex);
                        if (!found)
                        {
                            result = new Bounds(point, Vector3.zero);
                            found = true;
                        }
                        else
                            result.Encapsulate(point);
                    }
                    if (included > 0)
                        continue;
                }
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(
                        bounds.center + Vector3.Scale(
                            bounds.extents,
                            new Vector3(
                                (i & 1) == 0
                                    ? -1
                                    : 1,
                                (i & 2) == 0
                                    ? -1
                                    : 1,
                                (i & 4) == 0
                                    ? -1
                                    : 1
                            )
                        )
                    );
                    if (!found)
                    {
                        result = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                        result.Encapsulate(point);
                }
            }
            return result;
        }
    }
}
