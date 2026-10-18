using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ValheimBoombox.Visuals
{
    public sealed class BassShockwaveVisual : MonoBehaviour
    {
        public const string DefaultResourceName = "ValheimBoombox.BassEffects.bundle";
        public float Intensity = 1f;
        public Quaternion FrontRotation = Quaternion.identity;
        private Color _color;
        private static AssetBundle _bundle;
        private static GameObject _magicPrefab;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(8);
        private Transform _root;
        private BassShockwaveEdge _front;
        private bool _initialized;

        public void SetColor(Color color)
        {
            _color = color;
        }

        public static bool LoadEmbeddedAssets(Assembly assembly, string resourceName = DefaultResourceName)
        {
            if (_bundle != null)
                return true;
            if (assembly == null)
                throw new ArgumentNullException("assembly");
            using (Stream source = assembly.GetManifestResourceStream(resourceName))
            {
                if (source == null)
                    return false;
                using (MemoryStream bytes = new MemoryStream())
                {
                    source.CopyTo(bytes);
                    return LoadAssets(bytes.ToArray());
                }
            }
        }

        public static bool LoadAssets(byte[] bytes)
        {
            if (_bundle != null)
                return true;
            if (bytes == null || bytes.Length == 0)
                return false;
            AssetBundle bundle = AssetBundle.LoadFromMemory(bytes);
            if (bundle == null)
                return false;
            GameObject magic = bundle.LoadAsset<GameObject>("bass-magicblast");
            if (magic == null)
            {
                bundle.Unload(true);
                return false;
            }
            _bundle = bundle;
            _magicPrefab = magic;
            return true;
        }

        public static void ReleaseAssets()
        {
            if (_bundle != null)
                _bundle.Unload(false);
            _bundle = null;
            _magicPrefab = null;
        }

        public void Init(Color color, Material alphaMaterial)
        {
            if (_initialized)
                throw new InvalidOperationException("Init must be called once per bass wave.");
            if (_magicPrefab == null)
                throw new InvalidOperationException("Load the BassEffects asset bundle before creating a bass wave.");
            GameObject root = new GameObject("Bass native pressure front") { hideFlags = HideFlags.DontSave };
            _root = root.transform;
            _root.position = transform.position;
            _color = color;
            Material frontSource = _magicPrefab.GetComponentInChildren<ParticleSystemRenderer>(true).sharedMaterial;
            _front = new BassShockwaveEdge(_root, frontSource, alphaMaterial, WithIntensity(Intensity), _owned);
            _initialized = true;
            _root.gameObject.SetActive(isActiveAndEnabled);
        }

        public void UpdateRadius(float radius, float maxRadius, float normalizedAge)
        {
            if (!_initialized || !Finite(radius) || !Finite(maxRadius) || !Finite(normalizedAge))
                return;
            radius = Mathf.Max(0f, radius);
            maxRadius = Mathf.Max(0.01f, maxRadius);
            float age = Mathf.Clamp01(normalizedAge);
            _root.position = transform.position;
            _root.rotation = FrontRotation;
            _root.localScale = Vector3.one;
            _front.SetTint(WithIntensity(Intensity));
            _front.Update(radius, maxRadius, age);
            _front.SetVisible(Intensity > 0f);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private Color WithIntensity(float value)
        {
            return new Color(_color.r, _color.g, _color.b, _color.a * Mathf.Max(0f, value));
        }

        private void OnEnable()
        {
            if (_root != null)
                _root.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_root != null)
                Destroy(_root.gameObject);
            for (int index = 0; index < _owned.Count; index++)
            {
                if (_owned[index] != null)
                    Destroy(_owned[index]);
            }
            _owned.Clear();
        }
    }
}
