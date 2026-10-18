using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VikingBoombox
{
    [DefaultExecutionOrder(10000)]
    public sealed class BoomboxMount : MonoBehaviour
    {
        private float CarriedScale => BoomboxSettings.PositiveOr(BoomboxPlugin.Instance.Settings.BackScale.Value, 0.8f);
        private float verticalOffset;
        private float mountRear;
        private static readonly FieldInfo BackItemField = typeof(VisEquipment).GetField(
            "m_leftBackItemInstance",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        private static readonly FieldInfo ShoulderItemsField = typeof(VisEquipment).GetField(
            "m_shoulderItemInstances",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        public Transform BackAnchor;
        private Vector3 shoulderAnchorOffset;
        public Vector3 AttachmentPosition;
        public Quaternion AttachmentRotation;
        public float OutwardOffset;
        public float BodySurfaceDepth;
        public float EquipmentSurfaceDepth;
        public GameObject Holder => holder;
        private Player wearer;
        private SkinnedMeshRenderer bodyRenderer;
        private Transform neck;
        private Transform lowerSpine;
        private Transform leftShoulder;
        private Transform rightShoulder;
        private GameObject holder;
        private AudioDeck deck;
        private GameObject visual;
        private bool visualOnly;
        private Bounds localBodyBounds;
        private GameObject backItem;
        private readonly List<GameObject> currentShoulderRoots = new List<GameObject>();
        private readonly List<BackSurfaceSampler> equipmentSurfaces = new List<BackSurfaceSampler>();
        private readonly List<Vector2> blockedOffsets = new List<Vector2>();
        private BackSurfaceSampler bodySurface;
        private bool surfacesChanged = true;
        private bool clearanceReady;
        private Vector4 fittedSettings;
        private Vector3 fittedOffset, fittedRotation;
        private bool fittedAdaptive;

        public void Initialize()
        {
            deck = GetComponent<AudioDeck>();
            visual = deck.Visual;
            RefreshModelBounds();
        }

        internal void InitializeVisual(GameObject model, Player player)
        {
            visualOnly = true;
            visual = model;
            wearer = player;
            RefreshModelBounds();
        }

        public void RefreshModelBounds()
        {
            var model = visual.GetComponent<VikingBoomboxVisualState>();
            localBodyBounds = model.BodyBounds;
            var mount = transform.InverseTransformPoint(model.BackMountAnchor.position);
            verticalOffset = -mount.y * CarriedScale;
            mountRear = mount.z * CarriedScale;
            holder = model.BackMountAnchor.gameObject;
            surfacesChanged = true;
        }

        public void Follow(Player player)
        {
            var equipment = player.GetVisEquipment();
            var settings = BoomboxPlugin.Instance.Settings;
            var body = equipment != null ? equipment.m_bodyModel : null;
            if (wearer != player || bodyRenderer != body || BackAnchor == null)
                ResolveSkeleton(player, body, equipment);
            RefreshEquipment(equipment);
            UpdateFrame(player);
            var model = visual.GetComponent<VikingBoomboxVisualState>();
            var mount = transform.InverseTransformPoint(model.BackMountAnchor.position);
            verticalOffset = -mount.y * CarriedScale;
            mountRear = mount.z * CarriedScale;
            if (!clearanceReady)
            {
                BodySurfaceDepth = BoomboxSettings.Nonnegative(settings.BackBodyDepth.Value);
                OutwardOffset = BodySurfaceDepth + BoomboxSettings.Nonnegative(settings.BackClearance.Value)
                    + settings.BackEquipmentOffset.Value
                    - Mathf.Min(
                    localBodyBounds.min.z * CarriedScale,
                    mountRear
                );
            }
            ApplyPose(settings);
        }

        private void ApplyPose(BoomboxSettings settings)
        {
            holder.SetActive(true);
            transform.localScale = Vector3.one * CarriedScale;
            transform.rotation = AttachmentRotation * Quaternion.Euler(settings.BackRotation.Value);
            transform.position = AttachmentPosition
                + AttachmentRotation * (new Vector3(0f, verticalOffset, OutwardOffset) + settings.BackOffset.Value);
        }

        private void OnEnable()
        {
            Camera.onPreRender += BeforeCamera;
        }
        private void OnDisable()
        {
            Camera.onPreRender -= BeforeCamera;
        }

        private void BeforeCamera(Camera camera)
        {
            if (BoomboxPlugin.Instance == null
                || !BoomboxPlugin.Instance.AudioWorldActive
                || BackAnchor == null
                || visual == null
                || !visual.activeInHierarchy)
                return;
            var player = visualOnly
                ? wearer
                : deck != null && deck.State.onBack
                    ? Player.m_localPlayer
                    : null;
            if (player == null || player.IsDead())
                return;
            var settings = BoomboxPlugin.Instance.Settings;
            var configuration = new Vector4(
                CarriedScale,
                settings.BackBodyDepth.Value,
                settings.BackClearance.Value,
                settings.BackEquipmentOffset.Value
            );
            if (configuration != fittedSettings || settings.BackOffset.Value != fittedOffset
            || settings.BackRotation.Value != fittedRotation
            || settings.AdaptiveBackClearance.Value != fittedAdaptive)
                surfacesChanged = true;
            if (surfacesChanged)
            {
                bool bodyReady = bodySurface == null || bodySurface.Prepare();
                bool equipmentReady = true;
                int activeSurfaces = 0;
                foreach (var surface in equipmentSurfaces)
                {
                    bool active = surface.Active;
                    bool ready = surface.Prepare();
                    if (active)
                    {
                        activeSurfaces++;
                        equipmentReady &= ready;
                    }
                }
                if (bodyReady && equipmentReady && (equipmentSurfaces.Count == 0 || activeSurfaces > 0))
                {
                    UpdateFrame(player);
                    FitRenderedVolume(settings);
                    surfacesChanged = false;
                    clearanceReady = true;
                    fittedSettings = configuration;
                    fittedOffset = settings.BackOffset.Value;
                    fittedRotation = settings.BackRotation.Value;
                    fittedAdaptive = settings.AdaptiveBackClearance.Value;
                }
            }
            UpdateFrame(player);
            ApplyPose(settings);
        }

        private void FitRenderedVolume(BoomboxSettings settings)
        {
            Matrix4x4 worldToFrame = Matrix4x4.TRS(AttachmentPosition, AttachmentRotation, Vector3.one).inverse;
            var min = localBodyBounds.min * CarriedScale + Vector3.up * verticalOffset;
            var max = localBodyBounds.max * CarriedScale + Vector3.up * verticalOffset;
            var area = Rect.MinMaxRect(min.x - .002f, min.y - .002f, max.x + .002f, max.y + .002f);
            float rear = Mathf.Min(min.z, mountRear);
            float front = Mathf.Max(
                max.z,
                visual.GetComponent<VikingBoomboxVisualState>().ModelBounds.max.z * CarriedScale
            );
            float clearance = BoomboxSettings.Nonnegative(settings.BackClearance.Value);
            var torso = Rect.MinMaxRect(-.06f, area.yMin, .06f, area.yMax);
            float torsoDepth = bodySurface != null && bodySurface.Sample(worldToFrame, torso, out float depth, true)
                ? Mathf.Max(0f, depth)
                : 0f;
            BodySurfaceDepth = Mathf.Max(
                settings.AdaptiveBackClearance.Value
                    ? 0f
                    : BoomboxSettings.Nonnegative(settings.BackBodyDepth.Value),
                torsoDepth
            );
            float start = BodySurfaceDepth + clearance + settings.BackEquipmentOffset.Value - rear;
            blockedOffsets.Clear();
            foreach (var surface in equipmentSurfaces)
                surface.BlockedOffsets(worldToFrame, area, rear, front, clearance, blockedOffsets);
            OutwardOffset = NearestFreeOffset(start);
            EquipmentSurfaceDepth = equipmentSurfaces.Count == 0 ? 0f : OutwardOffset + rear - clearance;
        }

        private float NearestFreeOffset(float offset)
        {
            blockedOffsets.Sort((left, right) => left.x.CompareTo(right.x));
            foreach (var interval in blockedOffsets)
            {
                if (offset < interval.x)
                    break;
                if (offset <= interval.y)
                    offset = interval.y + .0001f;
            }
            return offset;
        }

        private void ResolveSkeleton(Player player, SkinnedMeshRenderer body, VisEquipment equipment)
        {
            wearer = player;
            bodyRenderer = body;
            var bones = body != null ? body.bones : new Transform[0];
            var shoulder = FindBone(bones, "Spine2", "UpperChest", "Chest", "Spine1", "Spine");
            BackAnchor = equipment != null && equipment.m_backShield != null ? equipment.m_backShield.parent : shoulder;
            shoulderAnchorOffset = BackAnchor != null && shoulder != null
                ? BackAnchor.InverseTransformPoint(shoulder.position)
                : Vector3.zero;
            neck = FindBone(bones, "Neck", "Neck1", "Head");
            lowerSpine = FindBone(bones, "Spine", "Spine1", "Hips");
            leftShoulder = FindBone(bones, "LeftShoulder", "LeftUpperArm", "LeftArm");
            rightShoulder = FindBone(bones, "RightShoulder", "RightUpperArm", "RightArm");
            bodySurface?.Dispose();
            bodySurface = body != null ? new BackSurfaceSampler(body) : null;
            surfacesChanged = true;
        }

        private static Transform FindBone(Transform[] bones, params string[] names)
        {
            foreach (string name in names)
            {
                var result = bones.FirstOrDefault(b => b != null
                    && (b.name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || b.name.EndsWith(":" + name, StringComparison.OrdinalIgnoreCase)));
                if (result != null)
                    return result;
            }
            return null;
        }

        private void UpdateFrame(Player player)
        {
            AttachmentPosition = BackAnchor != null
                ? BackAnchor.TransformPoint(shoulderAnchorOffset)
                : player.transform.position + player.transform.up * 1.35f;
            if (BackAnchor != null)
            {
                AttachmentRotation = BackAnchor.rotation * Quaternion.Euler(0f, 180f, 0f);
                return;
            }
            Vector3 up = neck != null && lowerSpine != null ? neck.position - lowerSpine.position : player.transform.up;
            if (up.sqrMagnitude < 0.0001f)
                up = player.transform.up;
            up.Normalize();
            Vector3 right = leftShoulder != null && rightShoulder != null
                ? rightShoulder.position - leftShoulder.position
                : player.transform.right;
            right = Vector3.ProjectOnPlane(right, up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.ProjectOnPlane(player.transform.right, up);
            right.Normalize();
            Vector3 outward = Vector3.Cross(up, right).normalized;
            if (Vector3.Dot(outward, player.transform.forward) > 0f)
                outward = -outward;
            AttachmentRotation = Quaternion.LookRotation(outward, up);
        }

        private void RefreshEquipment(VisEquipment equipment)
        {
            var nextBack = equipment != null && BackItemField != null
                ? BackItemField.GetValue(equipment) as GameObject
                : null;
            var nextShoulders = equipment != null && ShoulderItemsField != null
                ? ShoulderItemsField.GetValue(equipment) as List<GameObject>
                : null;
            // Destroyed Unity objects compare equal to null. CLR identity still
            // detects removal of the old shield and releases its cached clearance.
            bool changed = !ReferenceEquals(nextBack, backItem)
                || nextBack == null
                && (nextShoulders?.Count ?? 0) != currentShoulderRoots.Count;
            if (!changed && nextBack == null && nextShoulders != null)
                for (int i = 0; i < nextShoulders.Count; i++)
                    if (!ReferenceEquals(nextShoulders[i], currentShoulderRoots[i]))
                    {
                        changed = true;
                        break;
                    }
            if (!changed)
                return;
            backItem = nextBack;
            currentShoulderRoots.Clear();
            if (nextShoulders != null)
                currentShoulderRoots.AddRange(nextShoulders);
            foreach (var surface in equipmentSurfaces)
                surface.Dispose();
            equipmentSurfaces.Clear();
            EquipmentSurfaceDepth = 0f;
            var renderers = new HashSet<Renderer>();
            AddSurfaces(backItem, renderers);
            if (backItem == null)
                foreach (var root in currentShoulderRoots)
                    AddSurfaces(root, renderers);
            surfacesChanged = true;
        }

        private void AddSurfaces(GameObject root, HashSet<Renderer> added)
        {
            if (root == null)
                return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (BackSurfaceSampler.Supports(renderer) && added.Add(renderer))
                    equipmentSurfaces.Add(new BackSurfaceSampler(renderer));
        }

        private void LateUpdate()
        {
            if (BoomboxPlugin.Instance == null || !BoomboxPlugin.Instance.AudioWorldActive)
                return;
            if (visualOnly)
            {
                if (wearer != null && !wearer.IsDead())
                    Follow(wearer);
            }
            else if (deck != null && deck.State.onBack && Player.m_localPlayer != null)
                Follow(Player.m_localPlayer);
        }

        public void StandOnGround()
        {
            holder.SetActive(false);
            transform.localScale = Vector3.one;
        }

        private void OnDestroy()
        {
            Camera.onPreRender -= BeforeCamera;
            bodySurface?.Dispose();
            foreach (var surface in equipmentSurfaces)
                surface.Dispose();
        }
    }
}
