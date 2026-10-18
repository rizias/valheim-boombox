using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class BoomboxMusicNetwork
    {
        private sealed class Delivery
        {
            internal BoomboxMusicHit Hit;
            internal long Owner;
            internal bool Complete, Redirect;
        }
        private readonly Dictionary<string, BoomboxMusicHit> pendingBegins = new Dictionary<string, BoomboxMusicHit>();
        private readonly Dictionary<string, BoomboxMusicHit> pendingRequests =
            new Dictionary<string, BoomboxMusicHit>();
        private readonly Dictionary<string, Delivery> deliveries = new Dictionary<string, Delivery>();
        public int ReroutedHits
        {
            get;
            private set;
        }
        public int CancelledBegins
        {
            get;
            private set;
        }
        private static string EventKey(
            BoomboxMusicHit hit
        ) => hit.Peer + ":" + hit.Epoch + ":" + hit.Sequence + ":" + hit.Source;
        private static string DeliveryKey(BoomboxMusicHit hit) => EventKey(hit) + ":" + hit.Target;
        private bool ReadyBegin(BoomboxMusicHit hit)
        {
            hit.ValidatedSource |= BoomboxAuraNetwork.Sources.Any(source => source.Id == hit.Source
                && source.Peer == hit.Peer);
            if (hit.Peer == ZNet.GetUID())
            {
                var player = Player.m_localPlayer;
                return player != null && Begin(hit, hit.Peer, player.GetZDOID());
            }
            var peer = network.GetPeers().FirstOrDefault(
                p => p.m_uid == hit.Peer && p.IsReady() && BoomboxNetworkHandshake.Attach(network).Accepts(p.m_rpc)
            );
            return BoomboxAuraSources.Identity(peer, out var character, out _, out _)
                && Begin(hit, hit.Peer, character.m_uid);
        }
        private void DeferBegin(BoomboxMusicHit hit)
        {
            if (ReadyBegin(hit))
                return;
            string key = EventKey(hit);
            if (!pendingBegins.ContainsKey(key) && pendingBegins.Count < 4096)
                pendingBegins[key] = hit;
        }
        private void DeferHit(BoomboxMusicHit hit)
        {
            var original = events.Find(hit.Peer, hit.Epoch, hit.Sequence, hit.Source);
            if (original != null && (original.Cancelled || Hit(hit, hit.Peer)))
                return;
            string key = DeliveryKey(hit);
            if ((original != null || pendingBegins.ContainsKey(EventKey(hit))) && !pendingRequests.ContainsKey(key)
            && pendingRequests.Count < 4096)
                pendingRequests[key] = hit;
        }
        internal static void SourcesReceived(long peer, IReadOnlyList<BoomboxAuraSource> sources)
        {
            var service = Current;
            if (service == null || !service.network.IsServer())
                return;
            service.events.Cancel(peer, sources);
            foreach (var delivery in service.deliveries.Values.Where(d => d.Hit.Peer == peer))
                if (!sources.Any(source => source.Id == delivery.Hit.Source && source.Active))
                {
                    delivery.Hit.Cancelled = true;
                    delivery.Complete = true;
                }
            foreach (var pair in service.pendingBegins.Where(p => p.Value.Peer == peer).ToArray())
            {
                pair.Value.ValidatedSource |= sources.Any(source => source.Id == pair.Value.Source
                    && source.Peer == peer);
                if (sources.Any(source => source.Id == pair.Value.Source && source.Active))
                    continue;
                pair.Value.Cancelled = true;
                if (pair.Value.ValidatedSource)
                    service.events.Begin(pair.Value);
                service.pendingBegins.Remove(pair.Key);
                service.CancelledBegins++;
            }
        }
        private void Deliver(BoomboxMusicHit hit)
        {
            if (deliveries.Count >= 4096)
                return;
            var delivery = new Delivery { Hit = hit, Redirect = true };
            deliveries[DeliveryKey(hit)] = delivery;
            SendDelivery(delivery);
        }
        private void SendDelivery(Delivery delivery)
        {
            if (delivery.Complete || delivery.Hit.Cancelled)
                return;
            var source = BoomboxAuraNetwork.Sources.FirstOrDefault(s => s.Id == delivery.Hit.Source
                && s.Peer == delivery.Hit.Peer);
            if (source == null || !source.Active)
            {
                delivery.Hit.Cancelled = true;
                delivery.Complete = true;
                return;
            }
            var target = ZDOMan.instance.GetZDO(delivery.Hit.Target);
            if (target == null)
                return;
            long owner = target.GetOwner();
            if (owner == 0 || delivery.Owner == owner)
                return;
            if (owner == ZNet.GetUID())
            {
                if (delivery.Owner != 0)
                    ReroutedHits++;
                delivery.Owner = owner;
                delivery.Redirect = false;
                Queue(delivery.Hit);
                return;
            }
            var recipient = network.GetPeers().FirstOrDefault(
                p => p.m_uid == owner && p.IsReady() && BoomboxNetworkHandshake.Attach(network).Accepts(p.m_rpc)
            );
            if (recipient == null)
                return;
            if (delivery.Owner != 0)
                ReroutedHits++;
            delivery.Owner = owner;
            delivery.Redirect = false;
            recipient.m_rpc.Invoke("BoomboxMusicApply", delivery.Hit.Applied());
        }
        private void Complete(BoomboxMusicHit hit, int disposition)
        {
            if (network.IsServer())
                Result(hit, ZNet.GetUID(), disposition);
            else
                Server()?.m_rpc.Invoke("BoomboxMusicResult", hit.Result(disposition));
        }
        private void ReceiveResult(ZRpc rpc, ZPackage package)
        {
            var peer = Peer(rpc);
            if (!network.IsServer() || peer == null)
                return;
            try
            {
                if (package.Size() > 2048)
                    return;
                var hit = BoomboxMusicHit.Read(new ZPackage(package.ReadByteArray()), false, true);
                int disposition = package.ReadInt();
                if (package.GetPos() != package.Size() || disposition < 0 || disposition > 2)
                    return;
                Result(hit, peer.m_uid, disposition);
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance.LogError("Music result rejected: " + error.Message);
            }
        }
        private void Result(BoomboxMusicHit hit, long recipient, int disposition)
        {
            if (!deliveries.TryGetValue(DeliveryKey(hit), out var delivery)
                || delivery.Complete
                || delivery.Owner != recipient)
                return;
            if (disposition != 2)
            {
                delivery.Complete = true;
                return;
            }
            delivery.Redirect = true;
            SendDelivery(delivery);
        }
        private void TickDelivery()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var pair in pendingBegins.ToArray())
                if (now - pair.Value.Created > 2f || ReadyBegin(pair.Value))
                    pendingBegins.Remove(pair.Key);
            foreach (var pair in pendingRequests.ToArray())
            {
                var hit = pair.Value;
                var original = events.Find(hit.Peer, hit.Epoch, hit.Sequence, hit.Source);
                if (now - hit.Created > 2f || original != null && (original.Cancelled || Hit(hit, hit.Peer))
                || original == null && !pendingBegins.ContainsKey(EventKey(hit)))
                    pendingRequests.Remove(pair.Key);
            }
            foreach (var pair in deliveries.ToArray())
            {
                var delivery = pair.Value;
                if (now - delivery.Hit.Created > 10f)
                {
                    deliveries.Remove(pair.Key);
                    continue;
                }
                if (delivery.Complete)
                    continue;
                if (now - delivery.Hit.Created > 2f)
                {
                    delivery.Hit.Cancelled = true;
                    delivery.Complete = true;
                    continue;
                }
                var source = BoomboxAuraNetwork.Sources.FirstOrDefault(
                    s => s.Id == delivery.Hit.Source && s.Peer == delivery.Hit.Peer
                );
                if (source == null || !source.Active)
                {
                    delivery.Hit.Cancelled = true;
                    delivery.Complete = true;
                    continue;
                }
                if (delivery.Redirect)
                    SendDelivery(delivery);
            }
        }
    }
}
