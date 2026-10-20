using System;
using UnityEngine;

namespace VikingBoombox
{
    public static class BoomboxNetworkProtocol
    {
        public const string Guid = "local.valheim.boombox";
        public const string Version = "0.2.4";
        public const int Protocol = 5;
        internal static string LastReason;
        internal static ZPackage Hello()
        {
            var package = new ZPackage();
            package.Write(Guid);
            package.Write(Protocol);
            package.Write(Version);
            return package;
        }
        internal static ZPackage Rejection(string received, int protocol)
        {
            var package = new ZPackage();
            package.Write(Version);
            package.Write(Protocol);
            package.Write(received);
            package.Write(protocol);
            return package;
        }
        internal static string Reason(
            string received,
            int protocol,
            string required = Version,
            int requiredProtocol = Protocol
        )
        {
            switch (received)
            {
                case "wrong GUID":
                    received = BoomboxText.Get("network.wrong_guid");
                    break;
                case "invalid handshake":
                    received = BoomboxText.Get("network.invalid_handshake");
                    break;
                case "invalid peer info":
                    received = BoomboxText.Get("network.invalid_peer_info");
                    break;
                case "invalid reason":
                    received = BoomboxText.Get("network.invalid_reason");
                    break;
                case "mod missing":
                    received = BoomboxText.Get("network.mod_missing");
                    break;
            }
            string safe = (received ?? BoomboxText.Get("network.peer_missing")).Replace("<", "")
                .Replace(">", "")
                .Replace("\n", " ")
                .Replace("\r", " ");
            if (safe.Length > 48)
                safe = safe.Substring(0, 48);
            if (!System.Version.TryParse(required, out var parsed))
                required = Version;
            else
                required = parsed.ToString();
            return BoomboxText.Format("network.version_required", required, requiredProtocol, safe, protocol);
        }
    }
}
