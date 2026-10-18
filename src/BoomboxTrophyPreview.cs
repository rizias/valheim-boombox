using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxTrophyMarker : MonoBehaviour
    {
        public int Slot;
        public Renderer[] Mounts = new Renderer[0];
        private Material shown;

        internal void Present(bool visible, Material material)
        {
            foreach (var mount in Mounts)
                if (mount.enabled != visible)
                    mount.enabled = visible;
            if (!visible || material == shown)
                return;
            shown = material;
            foreach (var mount in Mounts)
                mount.sharedMaterials = Enumerable.Repeat(material, mount.sharedMaterials.Length).ToArray();
        }
    }

    internal sealed class BoomboxTrophyPreview : IDisposable
    {
        internal const int Layer = 31;
        internal static readonly Color LockedColor = new Color(.38f, .38f, .38f);
        internal static readonly Color OpenColor = new Color(.92f, .92f, .92f);
        internal static readonly Color FilledColor = new Color(.35f, .85f, .35f);
        private readonly RenderTexture texture;
        private readonly GameObject host;
        private readonly GameObject model;
        private readonly BoomboxVisualPresenter presenter;
        private readonly DeckState state = new DeckState();
        private readonly Camera camera;
        private readonly Light light;
        private readonly RawImage image;
        private readonly List<BoomboxTrophyMarker> markers = new List<BoomboxTrophyMarker>();
        private readonly Material lockedMaterial;
        private readonly Material openMaterial;
        private readonly Material selectedMaterial;
        private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
        private readonly HashSet<int> visible = new HashSet<int>();
        private readonly Dictionary<Transform, Renderer[]> decorationRenderers =
            new Dictionary<Transform, Renderer[]>();
        private int hoveredSlot = -1;
        private BoomboxVisualSnapshot signature;
        private float baseOrthographicSize;
        private Vector3 basePosition;
        private Vector3 modelPosition;
        private Quaternion modelRotation;
        private float focusDistance;
        private float zoomLevel = 1f;
        private float zoomAccumulator;
        private float nextRender;
        internal Action<int> SlotPicked;
        internal RectTransform Rect => image.rectTransform;
        internal GameObject Model => model;

        internal BoomboxTrophyPreview(RectTransform parent, float x, float y, float width, float height)
        {
            texture = new RenderTexture((int)(width * 2), (int)(height * 2), 16);
            var rect = BoomboxNativeUI.Rect("Preview", parent);
            BoomboxNativeUI.Place(rect, x, y, width, height);
            image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = true;
            var input = rect.gameObject.AddComponent<BoomboxTrophyPreviewInput>();
            input.Dragged = Rotate;
            input.Clicked = Pick;
            input.Hovered = point => hoveredSlot = point.HasValue ? SlotAt(point.Value) : -1;
            input.Scrolled = Zoom;
            host = new GameObject("BoomboxDecorationPreview");
            host.transform.position = new Vector3(0, -1000, 0);
            model = VikingBoomboxVisual.Create(host.transform);
            modelPosition = model.transform.localPosition;
            modelRotation = model.transform.localRotation;
            presenter = host.AddComponent<BoomboxVisualPresenter>();
            presenter.Initialize(state, model, Layer);
            var bounds = model.GetComponent<VikingBoomboxVisualState>().ModelBounds;
            var target = model.transform.TransformPoint(bounds.center);
            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(host.transform, false);
            cameraObject.transform.position = target + new Vector3(0, .3f, 1.8f);
            cameraObject.transform.LookAt(target);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = texture;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .035f, .03f, 1);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 4;
            camera.orthographic = true;
            camera.aspect = width / height;
            camera.orthographicSize = Mathf.Max(bounds.size.y * .8f, bounds.size.x * .65f / camera.aspect);
            baseOrthographicSize = camera.orthographicSize;
            basePosition = cameraObject.transform.position;
            focusDistance = Vector3.Distance(basePosition, target);
            var lightObject = new GameObject("PreviewLight");
            lightObject.transform.SetParent(host.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(35, -145, 0);
            light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.cullingMask = 1 << Layer;
            light.shadows = LightShadows.None;
            light.enabled = false;
            lockedMaterial = MarkerMaterial(LockedColor, .25f);
            openMaterial = MarkerMaterial(OpenColor, .6f);
            selectedMaterial = MarkerMaterial(new Color(1f, .75f, .2f), .8f);
            CreateMarkers();
        }

        private Material MarkerMaterial(Color color, float glow)
        {
            var material = BoomboxMaterials.Surface(model, color, "HammeredSteel");
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
            Glow(material, color, glow);
            return material;
        }

        private static void Glow(Material material, Color color, float strength)
        {
            material.color = color;
            if (!material.HasProperty("_EmissionColor"))
                return;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * strength);
        }

        private void CreateMarkers()
        {
            var body = model.GetComponent<VikingBoomboxVisualState>().BodyBounds;
            var mounts = BoomboxSlotMarkers.Mounts(model, body);
            float baseSize = body.size.x * .13f;
            foreach (var slot in BoomboxDecorationCatalog.Slots)
            {
                bool standing = slot.Kind == BoomboxDecorationKind.Figurine;
                var root = new GameObject("DecorationMarker_" + slot.Index);
                root.transform.SetParent(model.transform, false);
                root.transform.localPosition = mounts[slot.Index].Loop;
                root.layer = Layer;
                var zone = root.AddComponent<BoxCollider>();
                zone.center = standing ? new Vector3(0, .08f, 0) : new Vector3(0, -.07f, 0);
                zone.size = standing ? new Vector3(.08f, .16f, .08f) : new Vector3(.06f, .14f, .06f);
                var marker = root.AddComponent<BoomboxTrophyMarker>();
                marker.Slot = slot.Index;
                marker.Mounts = standing
                    ? new Renderer[] { StandMount(root.transform, baseSize) }
                    : HangingMount(root.transform, slot, mounts[slot.Index]);
                markers.Add(marker);
            }
        }

        private Renderer[] HangingMount(Transform root, BoomboxDecorationSlot slot, BoomboxPendantMount mount)
        {
            var staple = BoomboxStaple.Create(root, mount.Normal, mount.Along, lockedMaterial);
            return new[] { staple }.Concat(
                BoomboxChain.Create(root, lockedMaterial, slot.Upper).GetComponentsInChildren<Renderer>(true)
            ).ToArray();
        }

        private static Renderer StandMount(Transform root, float baseSize)
        {
            var mount = new GameObject("Mount");
            mount.transform.SetParent(root, false);
            mount.layer = Layer;
            mount.AddComponent<MeshFilter>().sharedMesh = BoomboxTrophyVisuals.Cylinder();
            mount.transform.localScale = new Vector3(baseSize * .6f, baseSize * .09f, baseSize * .6f);
            mount.transform.localPosition = Vector3.up * baseSize * .045f;
            var renderer = mount.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        internal void Present(AudioDeck deck, int selectedSlot, IEnumerable<BoomboxDecorationSlot> group)
        {
            host.SetActive(true);
            if (Time.unscaledTime < nextRender)
                return;
            nextRender = Time.unscaledTime + 1f / 30f;
            presenter.PresentSource(deck);
            var real = deck.State;
            if (signature == null || !signature.Matches(real))
            {
                signature = new BoomboxVisualSnapshot(real);
                state.upgradeTier = real.upgradeTier;
                state.insertedCassette = real.insertedCassette;
                state.decorationPlacements = real.decorationPlacements.Select(
                    p => new BoomboxDecorationPlacement { slot = p.slot, prefab = p.prefab, effect = p.effect }
                ).ToList();
                presenter.SetState(state);
            }
            float pulse = (Mathf.Sin(Time.unscaledTime * 6f) + 1f) * .5f;
            var gold = Color.Lerp(new Color(1f, .6f, .05f), new Color(1f, .95f, .55f), pulse);
            Glow(selectedMaterial, gold, .8f);
            visible.Clear();
            foreach (var slot in group)
                visible.Add(slot.Index);
            foreach (var marker in markers)
            {
                bool shown = visible.Contains(marker.Slot);
                if (marker.gameObject.activeSelf != shown)
                    marker.gameObject.SetActive(shown);
                if (!shown)
                    continue;
                bool filled = real.decorationPlacements.Any(p => p.slot == marker.Slot);
                marker.Present(
                    !filled,
                    marker.Slot == selectedSlot
                        ? selectedMaterial
                        : real.unlockedDecorationSlots.Contains(marker.Slot)
                            ? openMaterial
                            : lockedMaterial
                );
            }
            HighlightDecorations(selectedSlot, gold);
            light.enabled = true;
            camera.Render();
            light.enabled = false;
        }

        private void HighlightDecorations(int selectedSlot, Color gold)
        {
            var decorations = model.transform.Find("BoomboxDecorations");
            if (decorations == null)
                return;
            foreach (Transform holder in decorations)
            {
                if (!int.TryParse(holder.name.Substring(holder.name.LastIndexOf('_') + 1), out int slot))
                    continue;
                if (!decorationRenderers.TryGetValue(holder, out var renderers))
                    decorationRenderers[holder] = renderers = holder.GetComponentsInChildren<Renderer>(true)
                        .Where(r => !(r is ParticleSystemRenderer))
                        .ToArray();
                Color? color = slot == selectedSlot
                    ? gold
                    : slot == hoveredSlot
                        ? FilledColor
                        : (Color?)null;
                foreach (var renderer in renderers)
                {
                    if (renderer == null)
                        continue;
                    if (color == null)
                    {
                        renderer.SetPropertyBlock(null);
                        continue;
                    }
                    tint.Clear();
                    tint.SetColor("_Color", Color.Lerp(Color.white, color.Value, .7f));
                    tint.SetColor("_EmissionColor", color.Value * .6f);
                    renderer.SetPropertyBlock(tint);
                }
            }
            foreach (var stale in decorationRenderers.Keys.Where(k => k == null).ToList())
                decorationRenderers.Remove(stale);
        }

        private void Rotate(Vector2 delta)
        {
            nextRender = 0f;
            var center = model.transform.TransformPoint(model.GetComponent<VikingBoomboxVisualState>()
                .BodyBounds.center);
            float scale = Mathf.Max(.01f, image.rectTransform.lossyScale.x);
            model.transform.RotateAround(center, camera.transform.up, -delta.x * .4f / scale);
            model.transform.RotateAround(center, camera.transform.right, delta.y * .4f / scale);
        }

        private void Zoom(float ticks, Vector2? anchor)
        {
            nextRender = 0f;
            zoomAccumulator += ticks;
            int notches = (int)zoomAccumulator;
            if (notches == 0)
                return;
            zoomAccumulator -= notches;
            var point = anchor ?? new Vector2(.5f, .5f);
            var before = ViewportWorldPoint(point);
            zoomLevel = Mathf.Clamp(zoomLevel * Mathf.Pow(1.2f, notches), 1f, 3f);
            camera.orthographicSize = baseOrthographicSize / zoomLevel;
            camera.transform.position += before - ViewportWorldPoint(point);
        }

        private Vector3 ViewportWorldPoint(
            Vector2 normalized
        ) => camera.ViewportToWorldPoint(new Vector3(normalized.x, normalized.y, focusDistance));

        internal void ResetZoom()
        {
            nextRender = 0f;
            zoomLevel = 1f;
            zoomAccumulator = 0f;
            camera.orthographicSize = baseOrthographicSize;
            camera.transform.position = basePosition;
            model.transform.localPosition = modelPosition;
            model.transform.localRotation = modelRotation;
        }

        private int SlotAt(Vector2 normalized)
        {
            Physics.SyncTransforms();
            var ray = camera.ViewportPointToRay(new Vector3(normalized.x, normalized.y, 0));
            if (!Physics.Raycast(ray, out var hit, 10f, 1 << Layer))
                return -1;
            var marker = hit.collider.GetComponent<BoomboxTrophyMarker>();
            return marker == null ? -1 : marker.Slot;
        }

        private void Pick(Vector2 normalized)
        {
            int slot = SlotAt(normalized);
            if (slot >= 0)
                SlotPicked?.Invoke(slot);
        }

        internal Vector2 MarkerScreenPoint(int slot)
        {
            var marker = markers.First(m => m.Slot == slot);
            var viewport = camera.WorldToViewportPoint(
                marker.transform.TransformPoint(marker.GetComponent<BoxCollider>()
                    .center)
            );
            var rect = image.rectTransform;
            var local = new Vector2(
                rect.rect.xMin + viewport.x * rect.rect.width,
                rect.rect.yMin + viewport.y * rect.rect.height
            );
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(local));
        }

        internal void Hide()
        {
            if (presenter != null)
                presenter.Stop();
            if (host != null)
                host.SetActive(false);
        }

        public void Dispose()
        {
            if (presenter != null)
                presenter.DisposePreview();
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
            foreach (var material in new[] { lockedMaterial, openMaterial, selectedMaterial })
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            if (host != null)
                UnityEngine.Object.Destroy(host);
        }
    }
}
