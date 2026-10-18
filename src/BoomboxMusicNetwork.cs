using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class BoomboxMusicNetwork : MonoBehaviour
    {
        private readonly string epoch = Guid.NewGuid().ToString("N");
        private readonly HashSet<ZRpc> registered = new HashSet<ZRpc>();
        private readonly BoomboxMusicEvents events = new BoomboxMusicEvents();
        private readonly BoomboxMusicEvents applied = new BoomboxMusicEvents();
        private readonly Dictionary<string, BoomboxMusicHit> waiting = new Dictionary<string, BoomboxMusicHit>();
        private ZNet network;
        private long sequence;
        private float next;
        public static BoomboxMusicNetwork Current
        {
            get;
            private set;
        }
        public int AppliedHits
        {
            get;
            private set;
        }
        public float LastDamage
        {
            get;
            private set;
        }
        public int BegunEvents
        {
            get;
            private set;
        }
        public int RoutedHits
        {
            get;
            private set;
        }
        public int DroppedHits
        {
            get;
            private set;
        }

        internal static BoomboxMusicNetwork Attach(ZNet owner)
        {
            var service = owner.GetComponent<BoomboxMusicNetwork>()
                ?? owner.gameObject.AddComponent<BoomboxMusicNetwork>();
            service.network = owner;
            Current = service;
            return service;
        }
        internal void Connected(ZNetPeer peer)
        {
            if (!registered.Add(peer.m_rpc))
                return;
            peer.m_rpc.Register<ZPackage>("BoomboxMusicBegin", ReceiveBegin);
            peer.m_rpc.Register<ZPackage>("BoomboxMusicHit", ReceiveHit);
            peer.m_rpc.Register<ZPackage>("BoomboxMusicApply", ReceiveApplied);
            peer.m_rpc.Register<ZPackage>("BoomboxMusicResult", ReceiveResult);
        }
        internal static long NextEvent() => Current != null ? ++Current.sequence : 0L;
        internal static long Begin(AudioDeck deck)
        {
            var service = Current;
            if (service == null)
                return 0L;
            long sequence = NextEvent();
            if (!BoomboxPlugin.Instance.Settings.LocalGameplayEnabled
                || !BoomboxPlugin.Instance.Settings.Effect.CreatureDamage.Value
                || BoomboxAuraNetwork.Balance == null
                || BoomboxAuraNetwork.Balance.MusicOnly)
                return sequence;
            BoomboxAuraNetwork.PublishImmediate();
            BoomboxRemoteNetwork.PublishImmediate();
            var hit = new BoomboxMusicHit
            {
                Source = deck.State.id,
                Epoch = service.epoch,
                Sequence = sequence,
                Peer = ZNet.GetUID(),
                Created = Time.realtimeSinceStartup
            };
            if (service.network.IsServer())
                service.DeferBegin(hit);
            else
                service.Server()?.m_rpc.Invoke("BoomboxMusicBegin", hit.Request(true));
            return sequence;
        }
        internal static void Submit(AudioDeck deck, Character target, long sequence, float strength)
        {
            var service = Current;
            if (service == null || !BoomboxPlugin.Instance.Settings.LocalGameplayEnabled
            || !BoomboxPlugin.Instance.Settings.Effect.CreatureDamage.Value
            || target == null
            || !MusicEffectTargets.IsHostile(target, Player.m_localPlayer)
            || sequence < 1)
                return;
            var hit = new BoomboxMusicHit
            {
                Source = deck.State.id,
                Epoch = service.epoch,
                Sequence = sequence,
                Target = target.GetZDOID(),
                Strength = Mathf.Clamp01(strength)
            };
            if (service.network.IsServer())
            {
                hit.Peer = ZNet.GetUID();
                hit.Created = Time.realtimeSinceStartup;
                service.DeferHit(hit);
            }
            else
                service.Server()?.m_rpc.Invoke("BoomboxMusicHit", hit.Request(false));
        }
        private ZNetPeer Server() => network.GetPeers().FirstOrDefault(
            p => p.m_server && p.IsReady() && BoomboxNetworkHandshake.Attach(network).Accepts(p.m_rpc)
        );
        private ZNetPeer Peer(ZRpc rpc) => BoomboxNetworkHandshake.Attach(network).Accepts(rpc)
            ? network.GetPeers().FirstOrDefault(p => p.m_rpc == rpc && p.IsReady())
            : null;
        private void ReceiveBegin(ZRpc rpc, ZPackage package)
        {
            var peer = Peer(rpc);
            if (!network.IsServer() || peer == null)
                return;
            try
            {
                var hit = BoomboxMusicHit.Read(package, true);
                hit.Peer = peer.m_uid;
                DeferBegin(hit);
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance.LogError("Music begin rejected: " + error.Message);
            }
        }
        private void ReceiveHit(ZRpc rpc, ZPackage package)
        {
            var peer = Peer(rpc);
            if (!network.IsServer() || peer == null)
                return;
            try
            {
                var hit = BoomboxMusicHit.Read(package, false);
                hit.Peer = peer.m_uid;
                DeferHit(hit);
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance.LogError("Music hit rejected: " + error.Message);
            }
        }
        private bool Begin(BoomboxMusicHit hit, long peer, ZDOID attacker)
        {
            var source = BoomboxAuraNetwork.Sources.FirstOrDefault(s => s.Id == hit.Source && s.Peer == peer);
            var character = ZDOMan.instance.GetZDO(attacker);
            var visual = BoomboxRemoteNetwork.Current?.States.FirstOrDefault(s => s.Id == hit.Source && s.Peer == peer);
            var settings = BoomboxPlugin.Instance.Settings;
            if (source == null || character == null || visual == null)
                return false;
            if (character.GetBool(ZDOVars.s_dead, false) || settings.MusicOnly.Value
            || (hit.Peer == ZNet.GetUID() && !settings.Effect.CreatureDamage.Value))
            {
                hit.Cancelled = true;
                events.Begin(hit);
                return true;
            }
            if (!source.Active)
                return false;
            if (!visual.Active || visual.Worn != source.Worn || visual.WorldItem != source.WorldItem)
                return false;
            for (int index = 0; index < source.Ranks.Length; index++)
                if (source.Ranks[index]
                    != (visual.Appearance.insertedCassette?.Rank(BoomboxAuraSources.Properties[index]) ?? 0))
                    return false;
            hit.Peer = peer;
            hit.Attacker = attacker;
            hit.Center = source.Worn ? character.GetPosition() : source.Position;
            hit.Created = Time.realtimeSinceStartup;
            hit.Damage = settings.WaveDamage(visual.Appearance.upgradeTier)
                * (1f + BoomboxAuraMath.Aggregate(
                BoomboxAuraNetwork.Sources,
                character.GetPosition(),
                BoomboxAuraNetwork.Balance
            ).Values[0]);
            hit.Push = settings.WavePush(visual.Appearance.upgradeTier)
                * BoomboxSettings.Nonnegative(settings.EnemyPushMultiplier.Value);
            if (events.Begin(hit))
                BegunEvents++;
            return true;
        }
        private bool Hit(BoomboxMusicHit request, long peer)
        {
            var original = events.Find(peer, request.Epoch, request.Sequence, request.Source);
            if (original == null)
                return false;
            if (original.Cancelled)
                return true;
            var source = BoomboxAuraNetwork.Sources.FirstOrDefault(s => s.Id == request.Source
                && s.Peer == peer
                && s.Active);
            var target = ZDOMan.instance.GetZDO(request.Target);
            var visual = BoomboxRemoteNetwork.Current?.States.FirstOrDefault(s => s.Id == request.Source
                && s.Peer == peer);
            if (source == null || target == null || visual == null)
                return false;
            var offset = target.GetPosition() - original.Center;
            offset.y = 0f;
            float radius = BoomboxPlugin.Instance.Settings.WaveRadius(visual.Appearance.upgradeTier);
            var targetInstance = ZNetScene.instance.FindInstance(request.Target)?.GetComponent<Character>();
            if (offset.magnitude > radius + (targetInstance != null ? targetInstance.GetRadius() : 1f))
                return true;
            var hit = new BoomboxMusicHit
            {
                Source = original.Source,
                Epoch = original.Epoch,
                Sequence = original.Sequence,
                Peer = peer,
                Attacker = original.Attacker,
                Target = request.Target,
                Center = original.Center,
                Damage = original.Damage * request.Strength,
                Push = original.Push,
                Strength = request.Strength,
                Created = Time.realtimeSinceStartup
            };
            if (!events.Target(hit))
                return true;
            RoutedHits++;
            Deliver(hit);
            return true;
        }
        private void ReceiveApplied(ZRpc rpc, ZPackage package)
        {
            var peer = Peer(rpc);
            if (network.IsServer() || peer == null || !peer.m_server)
                return;
            try
            {
                Queue(BoomboxMusicHit.Read(package, false, true));
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance.LogError("Applied music hit rejected: " + error.Message);
            }
        }
        private void Queue(BoomboxMusicHit hit)
        {
            string key = hit.Peer + ":" + hit.Epoch + ":" + hit.Sequence + ":" + hit.Target;
            if (!waiting.ContainsKey(key))
                waiting[key] = hit;
        }
        private bool Apply(BoomboxMusicHit hit)
        {
            if (hit.Cancelled)
            {
                Complete(hit, 0);
                return true;
            }
            if (applied.ContainsTarget(hit))
            {
                Complete(hit, 1);
                return true;
            }
            var target = ZNetScene.instance.FindInstance(hit.Target)?.GetComponent<Character>();
            var owner = ZNetScene.instance.FindInstance(hit.Attacker)?.GetComponent<Player>();
            if (target == null || owner == null)
                return false;
            if (!MusicEffectTargets.IsHostile(target, owner) || owner.IsDead() || owner.IsTeleporting())
            {
                Complete(hit, 0);
                return true;
            }
            if (!target.IsOwner())
            {
                Complete(hit, 2);
                return true;
            }
            var source = BoomboxAuraNetwork.Sources.FirstOrDefault(s => s.Id == hit.Source && s.Peer == hit.Peer);
            if (source == null)
                return false;
            if (!source.Active)
            {
                if (network.IsServer())
                {
                    Complete(hit, 0);
                    return true;
                }
                return false;
            }
            if (applied.Find(hit.Peer, hit.Epoch, hit.Sequence, hit.Source) == null)
                applied.Begin(hit);
            if (!applied.Target(hit))
            {
                Complete(hit, 1);
                return true;
            }
            MusicEffectDamage.Apply(target, owner, hit.Center, hit.Damage, hit.Push);
            AppliedHits++;
            LastDamage = hit.Damage;
            Complete(hit, 1);
            return true;
        }
        internal static void AuraStateReceived(IReadOnlyList<BoomboxAuraSource> sources)
        {
            var service = Current;
            if (service == null || service.network.IsServer())
                return;
            foreach (var hit in service.waiting.Values)
                if (!sources.Any(source => source.Id == hit.Source && source.Peer == hit.Peer && source.Active))
                    hit.Cancelled = true;
        }
        private void Update()
        {
            if (network == null)
                return;
            foreach (var peer in network.GetPeers())
                Connected(peer);
            var rpcs = new HashSet<ZRpc>(network.GetPeers().Select(p => p.m_rpc));
            registered.RemoveWhere(rpc => !rpcs.Contains(rpc));
            if (Time.realtimeSinceStartup < next)
                return;
            next = Time.realtimeSinceStartup + .05f;
            if (network.IsServer())
                TickDelivery();
            foreach (var pair in waiting.ToArray())
            {
                if (Time.realtimeSinceStartup - pair.Value.Created > 2f)
                {
                    Complete(pair.Value, 0);
                    DroppedHits++;
                    waiting.Remove(pair.Key);
                }
                else if (Apply(pair.Value))
                    waiting.Remove(pair.Key);
            }
            var peers = new HashSet<long>(network.GetPeers().Where(p => p.IsReady()).Select(p => p.m_uid));
            peers.Add(ZNet.GetUID());
            if (network.IsServer())
                events.Prune(peers);
            foreach (var state in BoomboxRemoteNetwork.Current?.States ?? Array.Empty<BoomboxRemoteState>())
                peers.Add(state.Peer);
            applied.Prune(peers);
        }
        private void OnDestroy()
        {
            foreach (var rpc in registered)
            {
                rpc.Unregister("BoomboxMusicBegin");
                rpc.Unregister("BoomboxMusicHit");
                rpc.Unregister("BoomboxMusicApply");
                rpc.Unregister("BoomboxMusicResult");
            }
            registered.Clear();
            waiting.Clear();
            pendingBegins.Clear();
            pendingRequests.Clear();
            deliveries.Clear();
            if (Current == this)
                Current = null;
        }
    }
}
