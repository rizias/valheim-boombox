using System;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxVisualPresenter : MonoBehaviour
    {
        private DeckState state;
        private GameObject model;
        private VikingBoomboxVisualState modelState;
        private BoomboxTrophyVisuals decorations;
        private BoomboxPendantParticles particles;
        private BoomboxVisualSnapshot signature;
        private bool stopped;
        private bool preview;
        private int presentedFrame = -1;
        private float nextStateCheck;
        private int renderLayer = -1;
        private BoomboxDisplay display;
        private AudioDeck sourceDeck;
        private int sourceEvents = -1;

        public BoomboxTrophyVisuals Decorations => decorations;
        public GameObject Model => model;

        public static BoomboxVisualPresenter CreatePreview(DeckState state, Transform parent, int layer)
        {
            var host = new GameObject("BoomboxVisualPreview");
            host.transform.SetParent(parent, false);
            var visual = VikingBoomboxVisual.Create(host.transform);
            var presenter = host.AddComponent<BoomboxVisualPresenter>();
            presenter.preview = true;
            presenter.Initialize(state, visual, layer);
            return presenter;
        }

        public void Initialize(DeckState deckState, GameObject visual, int layer = -1)
        {
            preview |= layer >= 0;
            renderLayer = layer;
            state = deckState;
            model = visual;
            modelState = model.GetComponent<VikingBoomboxVisualState>();
            decorations = gameObject.GetComponent<BoomboxTrophyVisuals>()
                ?? gameObject.AddComponent<BoomboxTrophyVisuals>();
            decorations.PreviewLayer = layer;
            decorations.Initialize(state, model);
            particles = gameObject.GetComponent<BoomboxPendantParticles>()
                ?? gameObject.AddComponent<BoomboxPendantParticles>();
            particles.Initialize(state, model);
            if (preview)
            {
                display = gameObject.AddComponent<BoomboxDisplay>();
                display.InitializeVisual(model);
            }
            if (layer >= 0)
                BoomboxTrophyVisuals.SetLayer(model.transform, layer);
            RefreshState(true);
        }

        public void RefreshState(bool force = false)
        {
            if (state == null || modelState == null)
                return;
            if (!force && Time.unscaledTime < nextStateCheck)
                return;
            nextStateCheck = Time.unscaledTime + .15f;
            if (!force && signature != null && signature.Matches(state))
                return;
            signature = new BoomboxVisualSnapshot(state);
            BoomboxCassetteAppearance.Apply(model, state.insertedCassette);
            decorations.Refresh();
            if (renderLayer >= 0)
                BoomboxTrophyVisuals.SetLayer(model.transform, renderLayer);
        }

        public void PresentFrame(bool playing, float motion, float glow)
        {
            if (presentedFrame == Time.frameCount)
                return;
            presentedFrame = Time.frameCount;
            stopped = false;
            RefreshState();
            VikingBoomboxVisual.SetBass(model, motion, glow);
            decorations.ApplyMembranes(VikingBoomboxVisual.Strength(motion));
            decorations.ApplyGlow(VikingBoomboxVisual.Strength(glow));
            modelState.AnimateReels(playing);
        }

        public void OnMusicEvent(float strength)
        {
            if (stopped)
                return;
            decorations.OnMusicEvent(strength);
        }

        public void PresentSource(AudioDeck source)
        {
            if (sourceDeck != source)
                sourceEvents = -1;
            sourceDeck = source;
            particles.SetSource(source.State);
            bool active = source.IsActivePlayback || source.IsSyntheticActive;
            PresentFrame(active, source.VisualStrength, source.GlowStrength);
            var realModel = source.Visual.GetComponent<VikingBoomboxVisualState>();
            modelState.ReelLeft.localRotation = realModel.ReelLeft.localRotation;
            modelState.ReelRight.localRotation = realModel.ReelRight.localRotation;
            if (display != null)
            {
                display.Show(source, source.GlowStrength);
                display.SyncScroll(source.Display);
            }
            if (preview && sourceEvents >= 0 && source.EmittedWaves != sourceEvents && active)
                OnMusicEvent(source.LastBeatStrength);
            sourceEvents = source.EmittedWaves;
        }

        private void LateUpdate()
        {
            if (preview && !stopped && sourceDeck != null)
                PresentSource(sourceDeck);
        }

        public void Stop()
        {
            if (stopped)
                return;
            stopped = true;
            sourceDeck = null;
            sourceEvents = -1;
            if (model != null)
                VikingBoomboxVisual.SetBass(model, 0f, 0f);
            decorations?.ApplyMembranes(0f);
            decorations?.ApplyGlow(0f);
            modelState?.AnimateReels(false);
        }

        public void DisposePreview()
        {
            Stop();
            if (preview)
                Destroy(gameObject);
        }

        public void SetState(DeckState deckState)
        {
            state = deckState;
            decorations.SetState(deckState);
            particles.SetState(deckState);
            signature = null;
            RefreshState(true);
        }

    }
}
