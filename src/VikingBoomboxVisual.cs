using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public static class VikingBoomboxVisual
    {
        public const string ModelResourceName = "ValheimBoombox.Model.bundle";
        public const string ModelPrefabKey = "ValheimBoombox_V3_1";
        public const string CarvingMaterial = "UrnesCarving";
        private const string GameFrame = "ValheimBoombox_V3_1_Game";
        internal static UnityEngine.Color CarvingBaseGlow
        {
            get;
            private set;
        }
        private static AssetBundle bundle;
        private static GameObject prefab;
        internal static bool CanRender => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null
        && (ZNet.instance == null || !ZNet.instance.IsDedicated());

        public static GameObject CreateAsset(string key, Transform parent)
        {
            if (prefab == null)
                LoadModel();
            var asset = bundle.LoadAsset<GameObject>(key == "CassettePurple" ? "CassetteBlue" : key);
            if (asset == null)
                throw new InvalidOperationException(BoomboxText.Get("text.model_bundle_is_missing") + key);
            var model = UnityEngine.Object.Instantiate(asset, parent, false);
            model.name = key;
            if (CanRender && key.StartsWith("Cassette", StringComparison.Ordinal))
                AddSpeedMark(model, key == "CassettePurple");
            if (CanRender && key == ModelPrefabKey)
                PrepareItemBody(model);
            return model;
        }

        private static void PrepareItemBody(GameObject model)
        {
            model.AddComponent<BoomboxPhysicalDetails>().Initialize(model.transform.Find(GameFrame));
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true).Where(
                r => r.sharedMaterial != null && r.sharedMaterial.name == CarvingMaterial
            ))
            {
                CarvingBaseGlow = renderer.sharedMaterial.GetColor("_EmissionColor");
                var dark = new Material(renderer.sharedMaterial) { name = CarvingMaterial };
                dark.SetColor("_EmissionColor", UnityEngine.Color.black);
                renderer.sharedMaterial = dark;
            }
        }

        private static void AddSpeedMark(GameObject model, bool selected)
        {
            var blue = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PropertyBlue");
            if (blue == null)
                throw new InvalidOperationException("Cassette property mark is missing");
            var purple = UnityEngine.Object.Instantiate(blue.gameObject, blue.parent, false);
            purple.name = "PropertyPurple";
            if (selected)
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                    {
                        if (!original.name.StartsWith("CassetteLabelBlue", StringComparison.Ordinal))
                            return original;
                        var material = new Material(original) { name = "CassetteLabelPurple" };
                        VikingBoomboxVisualState.Color(material, new Color(.43f, .12f, .66f));
                        return material;
                    }).ToArray();
            foreach (var renderer in purple.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                {
                    var material = new Material(original) { name = "CassetteLabelPurple" };
                    VikingBoomboxVisualState.Color(material, new Color(.43f, .12f, .66f));
                    return material;
                }).ToArray();
            foreach (string property in BoomboxItemData.Properties)
            {
                var mark = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name
                    == "Property" + property);
                if (mark == null)
                    continue;
                mark.localScale = Vector3.Scale(mark.localScale, new Vector3(.804878f, 1, 1));
                mark.localPosition += Vector3.right * (property == "Purple" ? .00825f : -.00825f);
                mark.gameObject.SetActive(false);
            }
            purple.SetActive(false);
            var propertyName = model.name.Substring("Cassette".Length);
            BoomboxCassetteAppearance.Apply(
                model,
                BoomboxItemData.Properties.Contains(propertyName)
                    ? new[] { propertyName }
                    : new string[0]
            );
        }

        public static GameObject Create(Transform parent)
        {
            if (prefab == null)
                LoadModel();
            var root = new GameObject("VikingBoomboxVisual");
            root.transform.SetParent(parent, false);
            try
            {
                var model = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                model.name = ModelPrefabKey;
                var state = root.AddComponent<VikingBoomboxVisualState>();
                state.Initialize();
                if (CanRender)
                    root.AddComponent<BoomboxPhysicalDetails>().Initialize(model.transform.Find(GameFrame));
                return root;
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        private static void LoadModel()
        {
            using (var source = typeof(VikingBoomboxVisual).Assembly.GetManifestResourceStream(ModelResourceName))
            {
                if (source == null)
                    throw new InvalidOperationException(BoomboxText.Get("text.the_dll_contains_no_boombox_3_1_model"));
                using (var bytes = new MemoryStream())
                {
                    source.CopyTo(bytes);
                    bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                }
            }
            if (bundle == null)
                throw new InvalidOperationException(BoomboxText.Get("text.could_not_load_the_boombox_3_1_model_bundle"));
            prefab = bundle.LoadAsset<GameObject>(ModelPrefabKey);
            if (prefab == null)
            {
                bundle.Unload(true);
                bundle = null;
                throw new InvalidOperationException(BoomboxText.Get("text.model_bundle_is_missing") + ModelPrefabKey);
            }
        }

        internal static UnityEngine.Color CarvingGlow(UnityEngine.Color baseColor, float strength)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            float intensity = settings.ShowVisualEffects.Value && settings.ShowSigurdGlow.Value
                ? BoomboxSettings.Nonnegative(settings.SigurdGlowIntensity.Value)
                : 0f;
            return baseColor * settings.EngravingGlowTint.Value * intensity
                * (.45f + Mathf.Clamp01(strength) * 3f * Mathf.Clamp(settings.SideGlowReaction.Value, 0f, 3f));
        }

        public static void SetBass(GameObject root, float motion, float glow)
        {
            if (root == null)
                return;
            var state = root.GetComponent<VikingBoomboxVisualState>();
            if (state == null)
                return;
            state.ApplyMembranes(Strength(motion));
            state.ApplyGlow(Strength(glow));
        }

        internal static float Strength(float value) => float.IsNaN(value) ? 0f : Mathf.Clamp01(value);

        public static void ReleaseAssets()
        {
            if (bundle != null)
                bundle.Unload(false);
            bundle = null;
            prefab = null;
        }
    }

    public sealed class VikingBoomboxVisualState : MonoBehaviour
    {
        public Transform Left
        {
            get;
            private set;
        }
        public Transform Right
        {
            get;
            private set;
        }
        public Transform DisplayAnchor
        {
            get;
            private set;
        }
        public Transform BackMountAnchor
        {
            get;
            private set;
        }
        public Transform ReelLeft
        {
            get;
            private set;
        }
        public Transform ReelRight
        {
            get;
            private set;
        }
        public Transform DisplayTitleAnchor
        {
            get;
            private set;
        }
        public Transform DisplayProgressAnchor
        {
            get;
            private set;
        }
        public Bounds ModelBounds
        {
            get;
            private set;
        }
        public Bounds BodyBounds
        {
            get;
            private set;
        }
        public const float DisplayWidth = 0.240f;
        public const float DisplayHeight = 0.040f;
        public static readonly Vector3 OverlaySize = new Vector3(0.07f, 0.046f, 0.003f);
        public static readonly Vector3[] OverlayCenters = {
            new Vector3(-0.223f, 0.254f, -0.0805f),
            new Vector3(0.223f, 0.254f, -0.0805f)
        };
        private Vector3 leftPosition;
        private Vector3 rightPosition;
        private Vector3 leftScale;
        private Vector3 rightScale;
        private Renderer[] glowRenderers;
        private UnityEngine.Color[] glowColors;
        private bool[] sigurdGlow;
        private MaterialPropertyBlock pulse;
        private Quaternion reelLeftRotation;
        private Quaternion reelRightRotation;
        private float reelAngle;

        internal void Initialize()
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            Left = Required(transforms, "MembraneLeft");
            Right = Required(transforms, "MembraneRight");
            DisplayAnchor = Required(transforms, "DisplayAnchor");
            BackMountAnchor = Required(transforms, "BackMountAnchor");
            ReelLeft = Required(transforms, "ReelLeft");
            ReelRight = Required(transforms, "ReelRight");
            DisplayTitleAnchor = Required(transforms, "DisplayTitleAnchor");
            DisplayProgressAnchor = Required(transforms, "DisplayProgressAnchor");
            if (ReelLeft != null)
                reelLeftRotation = ReelLeft.localRotation;
            if (ReelRight != null)
                reelRightRotation = ReelRight.localRotation;
            leftPosition = Left.localPosition;
            rightPosition = Right.localPosition;
            leftScale = Left.localScale;
            rightScale = Right.localScale;
            ModelBounds = CalculateBounds();
            BodyBounds = CalculateBounds(false);
            glowRenderers = GetComponentsInChildren<Renderer>(true)
            .Where(r => r.sharedMaterial != null
                && (r.sharedMaterial.name == "RuneIce" || r.sharedMaterial.name == VikingBoomboxVisual.CarvingMaterial)
                && r.sharedMaterial.HasProperty("_EmissionColor"))
            .ToArray();
            glowColors = glowRenderers.Select(r => r.sharedMaterial.GetColor("_EmissionColor")).ToArray();
            sigurdGlow = glowRenderers.Select(r => r.sharedMaterial.name == VikingBoomboxVisual.CarvingMaterial).ToArray();
            ApplyMembranes(0f);
            ApplyGlow(0f);
            gameObject.AddComponent<BoomboxSpeakerSuspension>().Initialize(transform);
        }

        private static Transform Required(Transform[] transforms, string name)
        {
            var matches = transforms.Where(t => t.name == name).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(BoomboxText.Get("text.model_3_1_requires_exactly_one_anchor") + name);
            return matches[0];
        }

        private Bounds CalculateBounds(bool includeHandle = true)
        {
            var result = new Bounds();
            bool found = false;
            var modelRoot = transform.Find(VikingBoomboxVisual.ModelPrefabKey);
            bool hasCase = modelRoot.GetComponentsInChildren<MeshFilter>(true).Any(f => f.name == "Body_OldOak");
            foreach (var filter in modelRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!includeHandle && hasCase && filter.name != "Body_OldOak")
                    continue;
                if (!BoomboxItemPrefabs.VisibleUnder(filter.transform, modelRoot))
                    continue;
                if (!includeHandle
                && filter.GetComponentsInParent<Transform>(true).Any(
                    t => t.name.IndexOf("handle", StringComparison.OrdinalIgnoreCase) >= 0
                        || t.name.IndexOf("grip", StringComparison.OrdinalIgnoreCase) >= 0
                ))
                    continue;
                if (filter.sharedMesh == null)
                    continue;
                var bounds = filter.sharedMesh.bounds;
                var matrix = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
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
            if (!found)
                throw new InvalidOperationException(BoomboxText.Get("text.model_3_1_contains_no_geometry"));
            return result;
        }

        private static UnityEngine.Color BodyGlow(UnityEngine.Color baseColor, float strength, BoomboxSettings settings)
        {
            float intensity = settings.ShowVisualEffects.Value && settings.ShowBodyGlow.Value
                ? BoomboxSettings.Nonnegative(settings.BodyGlowIntensity.Value)
                : 0f;
            return baseColor * settings.BodyGlowTint.Value * intensity * (.45f + strength * 3f);
        }

        internal static void Color(Material material, UnityEngine.Color color)
        {
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }

        public void AnimateReels(bool playing)
        {
            if (!playing || !BoomboxPlugin.Instance.Settings.ShowVisualEffects.Value
            || !BoomboxPlugin.Instance.Settings.ShowReelMotion.Value)
                return;
            reelAngle = (reelAngle + 120f * Time.unscaledDeltaTime) % 360f;
            if (ReelLeft != null)
                ReelLeft.localRotation = Quaternion.AngleAxis(
                    reelAngle,
                    ReelLeft.parent.InverseTransformDirection(transform.forward)
                )
                    * reelLeftRotation;
            if (ReelRight != null)
                ReelRight.localRotation = Quaternion.AngleAxis(
                    reelAngle,
                    ReelRight.parent.InverseTransformDirection(transform.forward)
                )
                    * reelRightRotation;
        }

        internal void ApplyMembranes(float strength)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            float motion = settings.ShowVisualEffects.Value && settings.ShowMembraneMotion.Value
                ? strength * BoomboxSettings.Nonnegative(settings.MembraneMotion.Value)
                : 0f;
            Vector3 displacement = transform.TransformVector(Vector3.forward * (motion * 0.075f));
            Left.localPosition = leftPosition + Left.parent.InverseTransformVector(displacement);
            Right.localPosition = rightPosition + Right.parent.InverseTransformVector(displacement);
            Left.localScale = leftScale * (1f + motion * 0.08f);
            Right.localScale = rightScale * (1f + motion * 0.08f);
        }

        internal void ApplyGlow(float strength)
        {
            GetComponent<BoomboxPhysicalDetails>()?.ApplyPattern(strength);
            var settings = BoomboxPlugin.Instance.Settings;
            if (pulse == null)
                pulse = new MaterialPropertyBlock();
            for (int i = 0; i < glowRenderers.Length; i++)
            {
                var renderer = glowRenderers[i];
                if (renderer == null)
                    continue;
                renderer.GetPropertyBlock(pulse);
                var color = sigurdGlow[i]
                    ? VikingBoomboxVisual.CarvingGlow(glowColors[i], strength)
                    : BodyGlow(glowColors[i], strength, settings);
                pulse.SetColor("_EmissionColor", color);
                pulse.SetFloat("_Metallic", 0f);
                pulse.SetFloat("_Glossiness", .15f);
                renderer.SetPropertyBlock(pulse);
            }
        }
    }
}
