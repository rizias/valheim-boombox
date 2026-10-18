using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxAuraNetwork : MonoBehaviour
    {
        private readonly BoomboxAuraRegistry registry = new BoomboxAuraRegistry();
        private readonly string session = System.Guid.NewGuid().ToString("N");
        private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
        private readonly Dictionary<ZRpc, string> acceptedSessions = new Dictionary<ZRpc, string>();
        private ZNet network;
        private long revision, receivedRevision;
        private string receivedSession;
        private float next, lastSnapshot;
        private List<BoomboxAuraSource> snapshot = new List<BoomboxAuraSource>();
        private BoomboxAuraBalance balance;
        public static BoomboxAuraNetwork Current
        {
            get;
            private set;
        }
        public static bool CanEditBalance => ZNet.instance == null || ZNet.instance.IsServer();
        public static BoomboxAuraBalance Balance => Current != null && Current.balance != null
            ? Current.balance
            : !CanEditBalance
                ? null
                : BoomboxPlugin.Instance?.Settings != null
                    ? BoomboxPartySettings.Capture(BoomboxPlugin.Instance.Settings)
                    : null;
        public static IReadOnlyList<BoomboxAuraSource> Sources => Current == null
            ? Array.Empty<BoomboxAuraSource>()
            : Current.network.IsServer()
                ? Current.registry.Sources
                : Time.realtimeSinceStartup - Current.lastSnapshot <= 1.25f
                    ? Current.snapshot
                    : Array.Empty<BoomboxAuraSource>();
        internal static float WaveRadius(int tier) => Balance != null
            ? Balance.WaveRadius[Mathf.Clamp(tier, 0, BoomboxProgression.MaxTier)]
            : CanEditBalance
                ? BoomboxPlugin.Instance.Settings.WaveRadius(tier)
                : 0f;

        internal static BoomboxAuraNetwork Attach(ZNet owner)
        {
            var service = owner.GetComponent<BoomboxAuraNetwork>()
                ?? owner.gameObject.AddComponent<BoomboxAuraNetwork>();
            service.network = owner;
            Current = service;
            return service;
        }

        internal void Connected(ZNetPeer peer)
        {
            if (!registered.Add(peer.m_rpc))
                return;
            peer.m_rpc.Register<ZPackage>("BoomboxAuraSources", ReceiveSources);
            peer.m_rpc.Register<ZPackage>("BoomboxAuraState", ReceiveState);
        }
        internal static void PublishImmediate()
        {
            var service = Current;
            if (service == null || service.network == null)
                return;
            service.revision++;
            if (service.network.IsServer())
                service.TickServer(service.network.GetPeers(), Time.realtimeSinceStartup);
            else
                service.TickClient(service.network.GetPeers());
        }

        public static BoomboxAuraAggregate For(Player player)
        {
            var plugin = BoomboxPlugin.Instance;
            if (player == null
                || player.IsDead()
                || player.IsTeleporting()
                || plugin == null
                || !plugin.AudioWorldActive)
                return new BoomboxAuraAggregate();
            return BoomboxAuraMath.Aggregate(Sources, player.transform.position, Balance);
        }

        private void ReceiveSources(ZRpc rpc, ZPackage package)
        {
            if (!network.IsServer() || !BoomboxNetworkHandshake.Attach(network).Accepts(rpc))
                return;
            var peer = network.GetPeers().FirstOrDefault(p => p.m_rpc == rpc);
            if (!BoomboxAuraSources.Identity(peer, out var character, out long player, out string name))
                return;
            try
            {
                string remoteSession = BoomboxAuraCodec.ReadHeader(package, out long remoteRevision);
                if (acceptedSessions.TryGetValue(rpc, out var accepted) && accepted != remoteSession)
                    return;
                var incoming = BoomboxAuraCodec.ReadSources(package);
                if (incoming.Any(source => !BoomboxAuraSources.ValidateAndNormalize(source, peer.m_uid, player, name, character)))
                    return;
                if (registry.Replace(peer.m_uid, remoteSession, remoteRevision, incoming, Time.realtimeSinceStartup))
                {
                    acceptedSessions[rpc] = remoteSession;
                    BoomboxMusicNetwork.SourcesReceived(peer.m_uid, incoming);
                }
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance?.LogError("Boombox aura source rejected: " + error.Message);
            }
        }

        private void ReceiveState(ZRpc rpc, ZPackage package)
        {
            if (network.IsServer() || !BoomboxNetworkHandshake.Attach(network).Accepts(rpc))
                return;
            var peer = network.GetPeers().FirstOrDefault(p => p.m_rpc == rpc && p.m_server && p.IsReady());
            if (peer == null)
                return;
            try
            {
                string remoteSession = BoomboxAuraCodec.ReadHeader(package, out long remoteRevision);
                if (receivedSession != null && (receivedSession != remoteSession || remoteRevision <= receivedRevision))
                    return;
                var hostBalance = BoomboxAuraCodec.ReadBalance(package);
                var sources = BoomboxAuraCodec.ReadSources(package);
                balance = hostBalance;
                snapshot = sources;
                receivedSession = remoteSession;
                receivedRevision = remoteRevision;
                lastSnapshot = Time.realtimeSinceStartup;
                BoomboxMusicNetwork.AuraStateReceived(sources);
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance?.LogError("Boombox aura state rejected: " + error.Message);
            }
        }

        private void Update()
        {
            if (network == null || BoomboxPlugin.Instance?.Settings == null)
                return;
            var peers = network.GetPeers();
            foreach (var peer in peers)
                Connected(peer);
            var rpcs = new HashSet<ZRpc>(peers.Select(p => p.m_rpc));
            registered.RemoveWhere(rpc => !rpcs.Contains(rpc));
            foreach (var rpc in acceptedSessions.Keys.Where(r => !rpcs.Contains(r)).ToArray())
                acceptedSessions.Remove(rpc);
            if (!network.IsServer() && !peers.Any(p => p.m_server))
            {
                snapshot.Clear();
                balance = null;
                receivedSession = null;
                receivedRevision = 0;
            }
            float now = Time.realtimeSinceStartup;
            if (now < next)
                return;
            next = now + .25f;
            revision++;
            if (network.IsServer())
                TickServer(peers, now);
            else
                TickClient(peers);
        }

        private void TickClient(List<ZNetPeer> peers)
        {
            var server = peers.FirstOrDefault(
                p => p.m_server && p.IsReady() && BoomboxNetworkHandshake.Attach(network).Accepts(p.m_rpc)
            );
            if (server == null || Player.m_localPlayer == null || balance == null)
                return;
            var package = new ZPackage();
            BoomboxAuraCodec.Header(package, session, revision);
            BoomboxAuraCodec.Sources(package, BoomboxAuraSources.Local());
            server.m_rpc.Invoke("BoomboxAuraSources", package);
        }

        private void TickServer(List<ZNetPeer> peers, float now)
        {
            var connected = new HashSet<long>(
                peers.Where(p => p.IsReady() && BoomboxNetworkHandshake.Attach(network).Accepts(p.m_rpc)).Select(
                    p => p.m_uid
                )
            );
            var local = Player.m_localPlayer;
            if (local != null)
            {
                long uid = ZNet.GetUID();
                connected.Add(uid);
                var character = local.GetComponent<ZNetView>()?.GetZDO();
                var sources = BoomboxAuraSources.Local();
                if (character != null
                && sources.All(
                    s => BoomboxAuraSources.ValidateAndNormalize(s, uid, local.GetPlayerID(), local.GetPlayerName(), character)
                )
                && registry.Replace(uid, session, revision, sources, now))
                    BoomboxMusicNetwork.SourcesReceived(uid, sources);
            }
            registry.Prune(connected, now);
            balance = BoomboxPartySettings.Capture(BoomboxPlugin.Instance.Settings);
            var package = new ZPackage();
            BoomboxAuraCodec.Header(package, session, revision);
            BoomboxAuraCodec.Balance(package, balance);
            BoomboxAuraCodec.Sources(package, registry.Sources.ToList());
            foreach (var peer in peers)
                if (connected.Contains(peer.m_uid))
                    peer.m_rpc.Invoke("BoomboxAuraState", package);
        }

        private void OnDestroy()
        {
            foreach (var rpc in registered)
            {
                rpc.Unregister("BoomboxAuraSources");
                rpc.Unregister("BoomboxAuraState");
            }
            registered.Clear();
            acceptedSessions.Clear();
            snapshot.Clear();
            balance = null;
            if (Current == this)
            {
                Current = null;
                BoomboxAuraRecipient.Clear();
            }
        }
    }
}
