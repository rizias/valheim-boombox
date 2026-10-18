using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxNetworkHandshake : MonoBehaviour
    {
        private sealed class Join
        {
            public ZNetPeer Peer;
            public float Deadline;
            public bool Validated, Rejected;
            public ZPackage Info;
        }
        private readonly Dictionary<ZRpc, Join> joins = new Dictionary<ZRpc, Join>();
        private ZNet network;
        internal bool Accepts(ZRpc rpc) => joins.TryGetValue(rpc, out var join) && join.Validated && !join.Rejected;
        internal static BoomboxNetworkHandshake Attach(ZNet owner)
        {
            var gate = owner.GetComponent<BoomboxNetworkHandshake>()
                ?? owner.gameObject.AddComponent<BoomboxNetworkHandshake>();
            gate.network = owner;
            return gate;
        }
        internal void Connected(ZNetPeer peer)
        {
            if (!network.IsServer())
                BoomboxNetworkProtocol.LastReason = null;
            joins[peer.m_rpc] = new Join { Peer = peer, Deadline = Time.realtimeSinceStartup + 5f };
            peer.m_rpc.Register<ZPackage>("BoomboxHello", Hello);
            peer.m_rpc.Register<ZPackage>("BoomboxReject", Rejection);
            peer.m_rpc.Register("BoomboxRejectAck", Acknowledged);
            peer.m_rpc.Invoke("BoomboxHello", BoomboxNetworkProtocol.Hello());
        }
        private void Hello(ZRpc rpc, ZPackage package)
        {
            if (!joins.TryGetValue(rpc, out var join) || join.Rejected)
                return;
            try
            {
                if (package.Size() > 1024)
                    throw new InvalidOperationException("Invalid Boombox hello size");
                string guid = package.ReadString();
                int protocol = package.ReadInt();
                string version = package.ReadString();
                if (guid != BoomboxNetworkProtocol.Guid || protocol != BoomboxNetworkProtocol.Protocol
                || version != BoomboxNetworkProtocol.Version)
                {
                    if (network.IsServer() || guid != BoomboxNetworkProtocol.Guid)
                        Reject(join, guid == BoomboxNetworkProtocol.Guid ? version : "wrong GUID", protocol);
                    else
                        Reject(
                            join,
                            BoomboxNetworkProtocol.Version,
                            BoomboxNetworkProtocol.Protocol,
                            version,
                            protocol
                        );
                    return;
                }
                join.Validated = true;
                if (join.Info == null)
                    return;
                var info = join.Info;
                join.Info = null;
                AccessTools.Method(typeof(ZNet), "RPC_PeerInfo").Invoke(network, new object[] { rpc, info });
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance?.LogError("Boombox handshake: " + error.Message);
                Reject(join, "invalid handshake", -1);
            }
        }
        internal bool PeerInfo(ZRpc rpc, ZPackage package)
        {
            if (!joins.TryGetValue(rpc, out var join))
                return false;
            if (join.Rejected)
                return false;
            if (join.Validated)
                return true;
            if (package.Size() > 65536)
            {
                Reject(join, "invalid peer info", -1);
                return false;
            }
            if (join.Info == null)
                join.Info = new ZPackage((byte[])package.GetArray().Clone());
            return false;
        }
        private void Reject(Join join, string received, int protocol, string required = null, int requiredProtocol = -1)
        {
            if (join.Rejected)
                return;
            string reason = required == null
                ? BoomboxNetworkProtocol.Reason(received, protocol)
                : BoomboxNetworkProtocol.Reason(received, protocol, required, requiredProtocol);
            join.Rejected = true;
            join.Info = null;
            join.Deadline = Time.realtimeSinceStartup + (network.IsServer() ? 5f : .25f);
            BoomboxPlugin.Instance?.LogError(reason);
            if (!network.IsServer())
            {
                BoomboxNetworkProtocol.LastReason = reason;
                ZNet.SetExternalError(ZNet.ConnectionStatus.ErrorKicked);
            }
            join.Peer.m_rpc.Invoke("BoomboxReject", BoomboxNetworkProtocol.Rejection(received, protocol));
            if (network.IsServer())
                join.Peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorKicked);
        }
        private void Rejection(ZRpc rpc, ZPackage package)
        {
            if (!joins.TryGetValue(rpc, out var join) || network.IsServer())
                return;
            string reason;
            try
            {
                if (package.Size() > 1024)
                    throw new InvalidOperationException("Invalid Boombox rejection size");
                string required = package.ReadString();
                int requiredProtocol = package.ReadInt();
                string received = package.ReadString();
                int protocol = package.ReadInt();
                reason = BoomboxNetworkProtocol.Reason(received, protocol, required, requiredProtocol);
            }
            catch
            {
                reason = BoomboxNetworkProtocol.Reason("invalid reason", -1);
            }
            BoomboxNetworkProtocol.LastReason = reason;
            ZNet.SetExternalError(ZNet.ConnectionStatus.ErrorKicked);
            join.Rejected = true;
            join.Info = null;
            join.Deadline = Time.realtimeSinceStartup + .25f;
            rpc.Invoke("BoomboxRejectAck");
        }
        private void Acknowledged(ZRpc rpc)
        {
            if (network.IsServer() && joins.TryGetValue(rpc, out var join) && join.Rejected)
                join.Deadline = Time.realtimeSinceStartup + .25f;
        }
        private void Update()
        {
            if (network == null)
                return;
            var peers = network.GetPeers();
            foreach (var entry in joins.ToArray())
            {
                var join = entry.Value;
                if (!peers.Contains(join.Peer))
                {
                    joins.Remove(entry.Key);
                    continue;
                }
                if (join.Rejected)
                {
                    if (Time.realtimeSinceStartup >= join.Deadline)
                    {
                        network.Disconnect(join.Peer);
                        joins.Remove(entry.Key);
                    }
                }
                else if (!join.Validated && Time.realtimeSinceStartup >= join.Deadline)
                    Reject(join, "mod missing", -1);
            }
        }
        private void OnDestroy()
        {
            joins.Clear();
        }
    }
}
