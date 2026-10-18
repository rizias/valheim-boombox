using System;
using System.IO;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxMusicHit
    {
        internal string Source, Epoch;
        internal long Sequence, Peer;
        internal ZDOID Attacker, Target;
        internal Vector3 Center;
        internal float Damage, Push, Strength;
        internal float Created;
        internal bool Cancelled;
        internal bool ValidatedSource;

        internal ZPackage Request(bool begin)
        {
            var p = new ZPackage();
            p.Write(Source);
            p.Write(Epoch);
            p.Write(Sequence);
            if (!begin)
            {
                p.Write(Target);
                p.Write(Strength);
            }
            return p;
        }

        internal ZPackage Applied()
        {
            var p = Request(false);
            p.Write(Peer);
            p.Write(Attacker);
            p.Write(Center);
            p.Write(Damage);
            p.Write(Push);
            return p;
        }
        internal ZPackage Result(int disposition)
        {
            var p = new ZPackage();
            p.Write(Applied().GetArray());
            p.Write(disposition);
            return p;
        }

        internal static BoomboxMusicHit Read(ZPackage p, bool begin, bool applied = false)
        {
            if (p.Size() > 1024)
                throw new InvalidDataException("Music hit packet too large");
            var h = new BoomboxMusicHit
            {
                Source = p.ReadString(),
                Epoch = p.ReadString(),
                Sequence = p.ReadLong(),
                Created = Time.realtimeSinceStartup
            };
            if (!Guid.TryParseExact(h.Source, "N", out _) || !Guid.TryParseExact(h.Epoch, "N", out _) || h.Sequence < 1)
                throw new InvalidDataException("Invalid music event identity");
            if (!begin)
            {
                h.Target = p.ReadZDOID();
                h.Strength = p.ReadSingle();
                if (h.Target.IsNone() || !BoomboxAuraMath.Finite(h.Strength) || h.Strength < 0f || h.Strength > 1f)
                    throw new InvalidDataException("Invalid music hit");
            }
            if (applied)
            {
                h.Peer = p.ReadLong();
                h.Attacker = p.ReadZDOID();
                h.Center = p.ReadVector3();
                h.Damage = p.ReadSingle();
                h.Push = p.ReadSingle();
                if (h.Attacker.IsNone()
                    || !BoomboxAuraMath.Finite(h.Damage)
                    || h.Damage < 0f
                    || !BoomboxAuraMath.Finite(h.Push)
                    || h.Push < 0f)
                    throw new InvalidDataException("Invalid applied music hit");
            }
            if (p.GetPos() != p.Size())
                throw new InvalidDataException("Unexpected music event fields");
            return h;
        }
    }
}
