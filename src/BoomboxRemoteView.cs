using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxRemoteView : MonoBehaviour
    {
        private BoomboxRemoteState state;
        private BoomboxVisualPresenter presenter;
        private BoomboxMount mount;
        private BoomboxRemoteEffects effects;
        private BoomboxDisplay display;
        private Player wearer;
        private ItemDrop drop;
        private GameObject nativeVisual;
        private bool nativeWasActive;
        private bool disposed;
        private string appearance, epoch;
        private long revision;
        private readonly List<MusicStrike> fresh = new List<MusicStrike>();
        public string SourceId => state?.Id;
        public long SourceOwner => state?.Owner ?? 0L;
        public GameObject Model => presenter != null ? presenter.Model : null;
        internal bool Available => this != null && !disposed;
        public bool Playing => state != null && state.Active;
        public long PresentedRevision
        {
            get;
            private set;
        }
        public int PresentedEvents
        {
            get;
            private set;
        }

        internal static BoomboxRemoteView Create(BoomboxRemoteState state, Player player, ItemDrop drop)
        {
            var root = new GameObject("BoomboxRemote_" + state.Id);
            var view = root.AddComponent<BoomboxRemoteView>();
            var model = VikingBoomboxVisual.Create(root.transform);
            view.presenter = root.AddComponent<BoomboxVisualPresenter>();
            view.presenter.Initialize(state.Appearance, model);
            view.effects = new BoomboxRemoteEffects(root.transform, model);
            view.Bind(player, drop);
            view.Receive(state);
            return view;
        }

        internal void Bind(Player player, ItemDrop worldDrop)
        {
            if (!Available)
                return;
            if (wearer == player && drop == worldDrop)
                return;
            RestoreNative();
            wearer = player;
            drop = worldDrop;
            if (mount != null)
            {
                mount.enabled = false;
                Destroy(mount);
                mount = null;
            }
            transform.SetParent(drop != null ? drop.transform : null, false);
            if (wearer != null)
            {
                mount = gameObject.AddComponent<BoomboxMount>();
                mount.InitializeVisual(presenter.Model, wearer);
                mount.Follow(wearer);
            }
            else
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
                presenter.Model.GetComponent<VikingBoomboxVisualState>().BackMountAnchor.gameObject.SetActive(false);
            }
            nativeVisual = drop != null ? drop.transform.Find("attach")?.gameObject : null;
            if (nativeVisual != null)
            {
                nativeWasActive = nativeVisual.activeSelf;
                nativeVisual.SetActive(false);
            }
        }

        internal void Receive(BoomboxRemoteState incoming)
        {
            if (!Available)
                return;
            bool changed = incoming.Revision != revision || incoming.Epoch != epoch;
            state = incoming;
            if (changed)
            {
                if (incoming.Epoch != epoch)
                {
                    fresh.Clear();
                    effects?.Stop();
                }
                if (incoming.Active)
                    fresh.AddRange(incoming.Strikes);
                else
                    fresh.Clear();
                epoch = incoming.Epoch;
                revision = incoming.Revision;
            }
            string key = incoming.AppearanceKey();
            if (key != appearance)
            {
                appearance = key;
                presenter.SetState(incoming.Appearance);
                mount?.RefreshModelBounds();
            }
        }

        private void LateUpdate()
        {
            if (disposed || state == null || presenter == null)
                return;
            if (display == null && InventoryGui.instance != null)
            {
                display = gameObject.AddComponent<BoomboxDisplay>();
                display.InitializeVisual(presenter.Model);
            }
            bool active = state.Active && Time.realtimeSinceStartup - state.Received <= 1.25f;
            display?.ShowRemote(active);
            if (!active)
            {
                presenter.Stop();
                effects.Stop();
                fresh.Clear();
                return;
            }
            presenter.PresentFrame(true, state.Motion, state.Glow);
            foreach (var strike in fresh)
            {
                presenter.OnMusicEvent(strike.Strength);
                PresentedEvents++;
                if (BoomboxPlugin.Instance.Settings.ShowOthersWaves.Value
                    && BoomboxPlugin.Instance.Settings.ShowVisualEffects.Value)
                {
                    var model = presenter.Model.GetComponent<VikingBoomboxVisualState>();
                    BoomboxSpeakerPulse.Emit(model, strike.Strength);
                    BoomboxCassetteSymbol.Emit(model, state.Appearance.insertedCassette, strike.Strength);
                }
            }
            effects.Present(
                state,
                wearer ?? ZNetScene.instance?.FindInstance(state.Character)?.GetComponent<Player>(),
                fresh
            );
            fresh.Clear();
            PresentedRevision = state.Revision;
        }

        internal void Dispose()
        {
            if (!Available)
                return;
            disposed = true;
            RestoreNative();
            effects?.Stop();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
        private void RestoreNative()
        {
            if (nativeVisual != null && nativeWasActive
            && (state == null || BoomboxPlugin.Instance?.Decks.ContainsKey(state.Id) != true))
                nativeVisual.SetActive(true);
            nativeVisual = null;
            nativeWasActive = false;
        }
        private void OnDestroy()
        {
            RestoreNative();
            effects?.Stop();
        }
    }
}
