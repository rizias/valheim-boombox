using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class BoomboxTrophyVisuals : MonoBehaviour
    {
        private sealed class Motion
        {
            internal Transform pivot;
            internal Vector3 previousPosition;
            internal Vector3 previousVelocity;
            internal Vector2 angle;
            internal Vector2 velocity;
            internal Quaternion restRotation = Quaternion.identity;
            internal BoomboxPendulum pendulum;
            internal float bob = -1f;
        }
        private DeckState state;
        private GameObject visual;
        private Transform root;
        private readonly List<Motion> motions = new List<Motion>();
        private readonly List<BoomboxPendulum> swinging = new List<BoomboxPendulum>();
        private string signature;
        private BoomboxPendantMount[] mounts;
        private float nextCheck;
        private Material metal;
        private Material wood;
        private static Mesh cylinder;
        private static Mesh nativeTrollHead;
        private Material nativeTrollMaterial;
        internal int PreviewLayer = -1;
        public float HeadSpring = 110f;
        public float PendantDamping = 5f;
        public float HeadDamping = 7f;
        public float PendantMaximumAngle = 50f;
        public float ShortSidePendantSize = .08f;
        public float ContactPushFraction = .5f;
        public float ContactFriction = 20f;
        public float PendantWeight = 20f;
        public float PendantWholeLimit = .25f;
        public float HeadMaximumAngle = 18f;
        public float AccelerationResponse = 10f;
        public float PendantMusicImpulse = 35f;
        internal void Initialize(AudioDeck deck) => Initialize(deck.State, deck.Visual);
        internal void Initialize(DeckState deckState, GameObject model)
        {
            state = deckState;
            visual = model;
            carrierPosition = visual.transform.position;
            carrierRotation = visual.transform.rotation;
            root = new GameObject("BoomboxDecorations").transform;
            root.SetParent(visual.transform, false);
            if (!VikingBoomboxVisual.CanRender)
                return;
            metal = BoomboxMaterials.Surface(visual, new Color(.38f, .32f, .19f), "HammeredSteel");
            if (metal.HasProperty("_MainTex"))
                metal.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (metal.HasProperty("_EmissionColor"))
                metal.SetColor("_EmissionColor", Color.black);
            wood = BoomboxMaterials.Surface(visual, new Color(.36f, .20f, .10f), "OldOak");
            Refresh();
        }
        internal void SetState(DeckState deckState)
        {
            state = deckState;
            signature = null;
        }
        public void OnMusicEvent(float strength)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            if (!settings.ShowVisualEffects.Value
                || !settings.DecorationMusicMovement.Value
                || !settings.DecorationMovement.Value)
                return;
            for (int i = 0; i < motions.Count; i++)
            {
                if (motions[i].pendulum == null)
                {
                    Beat(motions[i]);
                    continue;
                }
                motions[i].pendulum.Push(new Vector2(
                        (i % 2 == 0 ? 1 : -1) * 8f,
                        PendantMusicImpulse
                    ) * Mathf.Clamp01(strength));
            }
        }
        private void LateUpdate()
        {
            if (state == null)
                return;
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + .2f;
                if (Signature() != signature)
                    Refresh();
            }
            var settings = BoomboxPlugin.Instance.Settings;
            bool moving = settings.ShowVisualEffects.Value && settings.DecorationMovement.Value;
            bool frozen = Time.timeScale <= 0 || AudioListener.pause;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            swinging.Clear();
            foreach (var motion in motions)
            {
                if (!motion.pivot)
                    continue;
                if (motion.pendulum == null)
                {
                    StepHead(motion, moving, frozen, dt);
                    continue;
                }
                if (!moving)
                    motion.pendulum.Rest();
                else if (frozen || dt <= 0)
                    motion.pendulum.Hold();
                else if (motion.pendulum.Begin(visual.transform))
                    swinging.Add(motion.pendulum);
            }
            if (swinging.Count > 0)
                Swing(Time.deltaTime);
            carrierPosition = visual.transform.position;
            carrierRotation = visual.transform.rotation;
        }
        private void StepHead(Motion motion, bool moving, bool frozen, float dt)
        {
            if (!moving || frozen)
            {
                motion.previousPosition = motion.pivot.position;
                motion.previousVelocity = Vector3.zero;
                if (!moving)
                {
                    motion.angle = motion.velocity = Vector2.zero;
                    motion.bob = -1f;
                    motion.pivot.localRotation = motion.restRotation;
                }
                return;
            }
            if (dt <= 0)
                return;
            Vector3 position = motion.pivot.position;
            Vector3 travel = position - motion.previousPosition;
            Vector3 speed = travel / dt;
            Vector3 acceleration = motion.pivot.parent.InverseTransformVector((speed - motion.previousVelocity) / dt);
            motion.previousPosition = position;
            motion.previousVelocity = speed;
            if (travel.sqrMagnitude > 1f)
            {
                motion.angle = motion.velocity = Vector2.zero;
                motion.previousVelocity = Vector3.zero;
            }
            else
            {
                acceleration = Vector3.ClampMagnitude(acceleration, 25f);
                motion.velocity += (new Vector2(-acceleration.z, acceleration.x) * AccelerationResponse - motion.angle * HeadSpring)
                    * dt;
                motion.velocity *= Mathf.Exp(-HeadDamping * dt);
                motion.angle = Vector2.ClampMagnitude(motion.angle + motion.velocity * dt, HeadMaximumAngle);
            }
            motion.pivot.localRotation = motion.restRotation
                * Quaternion.Euler(
                motion.angle.x + Bob(motion, dt),
                0,
                motion.angle.y
            );
        }
        private string Signature() => string.Join(
            "|",
            state.decorationPlacements.OrderBy(p => p.slot).Select(p => p.slot + ":" + p.prefab)
        );
        public void Refresh()
        {
            if (root == null || state == null || !VikingBoomboxVisual.CanRender)
                return;
            signature = Signature();
            motions.Clear();
            speakers.Clear();
            carvings.Clear();
            foreach (Transform child in root)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            var body = visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
            mounts = state.decorationPlacements.Count > 0 ? BoomboxSlotMarkers.Mounts(visual, body) : null;
            foreach (var placement in state.decorationPlacements)
                if (placement.slot >= 0 && placement.slot < BoomboxDecorationCatalog.Slots.Length)
                    Create(placement, BoomboxDecorationCatalog.Slots[placement.slot], body);
            if (PreviewLayer >= 0)
                SetLayer(root, PreviewLayer);
        }
        private void Create(BoomboxDecorationPlacement placement, BoomboxDecorationSlot slot, Bounds body)
        {
            bool hasVisual = BoomboxDecorationCatalog.TryVisual(
                BoomboxDecorationCatalog.Prefab(placement.prefab),
                out var record
            );
            if (!hasVisual && slot.Kind != BoomboxDecorationKind.Figurine)
                return;
            var attach = record?.Attach;
            var holder = new GameObject("DecorationSlot_" + slot.Index).transform;
            holder.SetParent(root, false);
            holder.localPosition = mounts[slot.Index].Loop;
            if (slot.Kind == BoomboxDecorationKind.Figurine)
            {
                if (!hasVisual || attach == null)
                {
                    Destroy(holder.gameObject);
                    return;
                }
                CreateStandingHead(holder, attach, body.size.x * .13f, placement.prefab, slot.Index);
                return;
            }
            BoomboxStaple.Create(holder, mounts[slot.Index].Normal, mounts[slot.Index].Along, null);
            var chain = BoomboxChain.Create(holder, null, slot.Upper);
            if (!hasVisual || attach == null)
                return;
            CreatePendant(holder, chain, slot, placement.prefab, record);
        }
        internal static void SetLayer(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            foreach (Transform child in target)
                SetLayer(child, layer);
        }
        private void OnDestroy()
        {
            if (nativeTrollMaterial != null)
                Destroy(nativeTrollMaterial);
            if (metal != null)
                Destroy(metal);
            if (wood != null)
                Destroy(wood);
        }
    }
}
