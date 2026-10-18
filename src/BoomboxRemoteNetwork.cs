using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxRemoteNetwork : MonoBehaviour
    {
        private readonly string epoch = Guid.NewGuid().ToString("N");
        private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
        private readonly Dictionary<string, List<MusicStrike>> pending = new Dictionary<string, List<MusicStrike>>();
        private readonly Dictionary<string, BoomboxRemoteState> states = new Dictionary<string, BoomboxRemoteState>();
        private readonly Dictionary<string, BoomboxRemoteView> views = new Dictionary<string, BoomboxRemoteView>();
        private sealed class Watermark
        {
            internal string Epoch;
            internal long Revision;
            internal ZDOID Character;
        }
        private readonly Dictionary<long, Watermark> watermarks = new Dictionary<long, Watermark>();
        private ZNet network;
        private long revision;
        private float next;
        public static BoomboxRemoteNetwork Current
        {
            get;
            private set;
        }
        public IReadOnlyCollection<BoomboxRemoteState> States => states.Values;
        public IReadOnlyCollection<BoomboxRemoteView> Views => views.Values;

        internal static BoomboxRemoteNetwork Attach(ZNet owner)
        {
            var service = owner.GetComponent<BoomboxRemoteNetwork>()
                ?? owner.gameObject.AddComponent<BoomboxRemoteNetwork>();
            service.network = owner;
            Current = service;
            return service;
        }

        internal void Connected(ZNetPeer peer)
        {
            if (registered.Add(peer.m_rpc))
                peer.m_rpc.Register<ZPackage>("BoomboxVisualFrame", Receive);
        }
        internal static void PublishImmediate()
        {
            if (Current != null)
                Current.Publish();
        }

        internal static void Observe(AudioDeck deck, bool active)
        {
            var service = Current;
            if (service == null || deck.State.owner != Player.m_localPlayer?.GetPlayerID())
                return;
            if (!service.pending.TryGetValue(deck.State.id, out var list))
                service.pending[deck.State.id] = list = new List<MusicStrike>();
            if (active)
                list.AddRange(deck.Moment.Fresh);
            else
                list.Clear();
        }

        private void Receive(ZRpc rpc, ZPackage package)
        {
            if (!BoomboxNetworkHandshake.Attach(network).Accepts(rpc))
                return;
            var peer = network.GetPeers().FirstOrDefault(p => p.m_rpc == rpc && p.IsReady());
            if (peer == null || !network.IsServer() && !peer.m_server)
                return;
            try
            {
                var state = BoomboxRemoteCodec.Read(package);
                if (network.IsServer())
                {
                    if (!BoomboxAuraSources.Identity(peer, out var character, out long player, out string name)
                    || !Validate(state, peer.m_uid, player, name, character))
                        return;
                    if (!Accept(state))
                        return;
                    Relay(state);
                }
                else
                    Accept(state);
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance?.LogError("Boombox visual state rejected: " + error.Message);
            }
        }

        private bool Validate(BoomboxRemoteState state, long peer, long player, string name, ZDO character)
        {
            if (state.Owner != player || state.Character != character.m_uid)
                return false;
            var source = new BoomboxAuraSource
            {
                Id = state.Id,
                Owner = player,
                OwnerName = name,
                Worn = state.Worn,
                Active = state.Active,
                Position = state.Position,
                WorldItem = state.WorldItem
            };
            for (int i = 0; i < 5; i++)
                source.Ranks[i] = state.Appearance.insertedCassette?.Rank(BoomboxAuraSources.Properties[i]) ?? 0;
            foreach (var p in state.Appearance.decorationPlacements)
                source.Decorations.Add(
                    new BoomboxAuraDecoration
                    {
                        Slot = p.slot,
                        Prefab = p.prefab,
                        Effect = (int)BoomboxDecorationCatalog.DataEffect(
                        p.prefab,
                        p.effect,
                        BoomboxAuraNetwork.Balance?.Fallback ?? "Health"
                    )
                    }
                );
            if (!BoomboxAuraSources.ValidateAndNormalize(source, peer, player, name, character))
                return false;
            state.Peer = peer;
            state.Active = source.Active;
            state.Position = source.Position;
            state.Radius = BoomboxAuraNetwork.WaveRadius(state.Appearance.upgradeTier);
            if (!state.WorldItem.IsNone())
                state.Rotation = ZDOMan.instance.GetZDO(state.WorldItem).GetRotation();
            state.Received = Time.realtimeSinceStartup;
            return true;
        }

        private bool Accept(BoomboxRemoteState state)
        {
            if (watermarks.TryGetValue(state.Peer, out var watermark)
            && (watermark.Epoch != state.Epoch || state.Revision <= watermark.Revision))
                return false;
            if (network.IsServer() && !BoomboxAuraNetwork.Sources.Any(s => s.Id == state.Id && s.Peer == state.Peer))
                return false;
            watermarks[state.Peer] = new Watermark
            {
                Epoch = state.Epoch,
                Revision = state.Revision,
                Character = state.Character
            };
            states[state.Id] = state;
            if (views.TryGetValue(state.Id, out var view))
            {
                if (view != null && view.Available)
                    view.Receive(state);
                else
                    views.Remove(state.Id);
            }
            return true;
        }

        private void Relay(BoomboxRemoteState state)
        {
            var package = BoomboxRemoteCodec.Write(state);
            foreach (var peer in network.GetPeers())
                if (peer.IsReady() && BoomboxNetworkHandshake.Attach(network).Accepts(peer.m_rpc))
                    peer.m_rpc.Invoke("BoomboxVisualFrame", package);
        }

        private void Update()
        {
            if (network == null || BoomboxPlugin.Instance == null)
                return;
            var peers = network.GetPeers();
            foreach (var peer in peers)
                Connected(peer);
            var rpcs = new HashSet<ZRpc>(peers.Select(p => p.m_rpc));
            registered.RemoveWhere(rpc => !rpcs.Contains(rpc));
            if (!network.IsServer() && !peers.Any(p => p.m_server))
            {
                states.Clear();
                watermarks.Clear();
                ClearViews();
            }
            float now = Time.realtimeSinceStartup;
            if (now < next)
                return;
            next = now + .05f;
            Publish();
            var connected = new HashSet<long>(peers.Where(p => p.IsReady()).Select(p => p.m_uid));
            connected.Add(ZNet.GetUID());
            foreach (string id in states.Where(p => network.IsServer()
                && !connected.Contains(p.Value.Peer)
                || now - p.Value.Received > 1.25f).Select(
                p => p.Key
            ).ToArray())
                states.Remove(id);
            foreach (long peer in watermarks.Where(
                p => network.IsServer()
                    ? !connected.Contains(p.Key)
                    : ZDOMan.instance?.GetZDO(p.Value.Character) == null && !states.Values.Any(s => s.Peer == p.Key)
            ).Select(
                p => p.Key
            ).ToArray())
                watermarks.Remove(peer);
            if (VikingBoomboxVisual.CanRender)
                SynchronizeViews();
        }

        private void Publish()
        {
            var plugin = BoomboxPlugin.Instance;
            var player = Player.m_localPlayer;
            if (player == null || !plugin.AudioWorldActive)
                return;
            var server = network.IsServer()
                ? null
                : network.GetPeers().FirstOrDefault(
                p => p.m_server && p.IsReady() && BoomboxNetworkHandshake.Attach(network).Accepts(p.m_rpc)
            );
            if (!network.IsServer() && server == null)
                return;
            var live = new HashSet<string>();
            foreach (var deck in plugin.Decks.Values)
            {
                if (deck == null || deck.Item == null || deck.State.owner != player.GetPlayerID())
                    continue;
                live.Add(deck.State.id);
                var state = BoomboxRemoteState.Capture(deck, epoch, ++revision);
                if (pending.TryGetValue(state.Id, out var list))
                {
                    if (state.Active)
                        state.Strikes.AddRange(list);
                    list.Clear();
                }
                if (network.IsServer())
                {
                    if (Validate(
                        state,
                        ZNet.GetUID(),
                        player.GetPlayerID(),
                        player.GetPlayerName(),
                        player.GetComponent<ZNetView>().GetZDO()
                    )
                    && Accept(state))
                        Relay(state);
                }
                else
                    server.m_rpc.Invoke("BoomboxVisualFrame", BoomboxRemoteCodec.Write(state));
            }
            foreach (string id in pending.Keys.Where(id => !live.Contains(id)).ToArray())
                pending.Remove(id);
        }

        private void SynchronizeViews()
        {
            var live = new HashSet<string>();
            long local = Player.m_localPlayer?.GetPlayerID() ?? 0L;
            foreach (var state in states.Values)
            {
                if (state.Owner == local || !state.Worn && state.WorldItem.IsNone())
                    continue;
                var wearer = state.Worn
                    ? ZNetScene.instance?.FindInstance(state.Character)?.GetComponent<Player>()
                    : null;
                var drop = state.Worn
                    ? null
                    : ZNetScene.instance?.FindInstance(state.WorldItem)?.GetComponent<ItemDrop>();
                if (state.Worn && (wearer == null || wearer.IsDead()) || !state.Worn && drop == null)
                    continue;
                Show(state, wearer, drop, live);
            }
            foreach (var drop in BoomboxItems.WorldDrops)
            {
                if (!drop.gameObject.activeInHierarchy)
                    continue;
                var state = BoomboxItemData.Read(drop.m_itemData).deck;
                if (live.Contains(state.id) || BoomboxPlugin.Instance.Decks.ContainsKey(state.id))
                    continue;
                var view = drop.GetComponent<ZNetView>();
                if (view == null || !view.IsValid())
                    continue;
                var inactive = new BoomboxRemoteState
                {
                    Id = state.id,
                    Owner = state.owner,
                    WorldItem = view.GetZDO().m_uid,
                    Position = drop.transform.position,
                    Rotation = drop.transform.rotation,
                    Appearance = BoomboxRemoteState.VisualState(state)
                };
                Show(inactive, null, drop, live);
            }
            foreach (string id in views.Keys.Where(id => !live.Contains(id)).ToArray())
            {
                if (views[id] != null)
                    views[id].Dispose();
                views.Remove(id);
            }
        }

        private void Show(BoomboxRemoteState state, Player player, ItemDrop drop, HashSet<string> live)
        {
            live.Add(state.Id);
            if (!views.TryGetValue(state.Id, out var view) || view == null || !view.Available)
            {
                view = BoomboxRemoteView.Create(state, player, drop);
                views[state.Id] = view;
            }
            view.Bind(player, drop);
            view.Receive(state);
        }

        private void ClearViews()
        {
            foreach (var view in views.Values)
                if (view != null)
                    view.Dispose();
            views.Clear();
        }
        private void OnDestroy()
        {
            foreach (var rpc in registered)
                rpc.Unregister("BoomboxVisualFrame");
            registered.Clear();
            pending.Clear();
            states.Clear();
            watermarks.Clear();
            ClearViews();
            if (Current == this)
                Current = null;
        }
    }
}
