using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxRemoteCodec
    {
        internal static ZPackage Write(BoomboxRemoteState state)
        {
            var p = new ZPackage();
            p.Write(state.Id);
            p.Write(state.Epoch);
            p.Write(state.Revision);
            p.Write(state.Peer);
            p.Write(state.Owner);
            p.Write(state.Character);
            p.Write(state.WorldItem);
            p.Write(state.Worn);
            p.Write(state.Active);
            p.Write(state.Position);
            p.Write(state.Rotation);
            p.Write(state.Appearance.upgradeTier);
            p.Write(state.Appearance.insertedCassette?.prefab ?? "");
            foreach (string property in BoomboxAuraSources.Properties)
                p.Write(state.Appearance.insertedCassette?.Rank(property) ?? 0);
            p.Write(state.Appearance.decorationPlacements.Count);
            foreach (var decoration in state.Appearance.decorationPlacements)
            {
                p.Write(decoration.slot);
                p.Write(decoration.prefab);
                p.Write(decoration.effect ?? "Health");
                p.Write(decoration.effectParticles);
                p.Write(decoration.extraParticles ?? "None");
            }
            p.Write(state.Motion);
            p.Write(state.Glow);
            p.Write(state.Level);
            p.Write(state.Radius);
            p.Write(state.Time);
            p.Write(state.Silent);
            p.Write((int)state.Variant);
            p.Write(state.Smooth.Whole);
            p.Write(state.Smooth.Low);
            p.Write(state.Smooth.Mid);
            p.Write(state.Smooth.High);
            p.Write(state.Strikes.Count);
            foreach (var strike in state.Strikes)
            {
                p.Write(strike.Time);
                p.Write(strike.Strength);
                p.Write((int)strike.Loudest);
            }
            return p;
        }

        internal static BoomboxRemoteState Read(ZPackage p)
        {
            if (p.Size() > 64 * 1024)
                throw new InvalidDataException("Remote visual packet too large");
            var s = new BoomboxRemoteState
            {
                Id = Text(p, 64),
                Epoch = Text(p, 64),
                Revision = p.ReadLong(),
                Peer = p.ReadLong(),
                Owner = p.ReadLong(),
                Character = p.ReadZDOID(),
                WorldItem = p.ReadZDOID(),
                Worn = p.ReadBool(),
                Active = p.ReadBool(),
                Position = p.ReadVector3(),
                Rotation = p.ReadQuaternion()
            };
            if (!Guid.TryParseExact(s.Id, "N", out _) || !Guid.TryParseExact(s.Epoch, "N", out _) || s.Revision < 1
            || !Finite(s.Position)
            || !Finite(s.Rotation))
                throw new InvalidDataException("Invalid remote visual identity or pose");
            s.Appearance = new DeckState { id = s.Id, owner = s.Owner, onBack = s.Worn, upgradeTier = p.ReadInt() };
            if (s.Appearance.upgradeTier < 0 || s.Appearance.upgradeTier > BoomboxProgression.MaxTier)
                throw new InvalidDataException("Invalid remote visual tier");
            string cassette = Text(p, 128);
            if (cassette.Length > 0 && !cassette.StartsWith(BoomboxItemData.CassettePrefix, StringComparison.Ordinal))
                throw new InvalidDataException("Invalid visual cassette");
            var properties = new Dictionary<string, int>();
            foreach (string property in BoomboxAuraSources.Properties)
            {
                int rank = p.ReadInt();
                if (rank < 0)
                    throw new InvalidDataException("Invalid visual rank");
                if (rank > 0)
                    properties[property] = rank;
            }
            if (cassette.Length > 0)
                s.Appearance.insertedCassette = new RecordedCassette { prefab = cassette, properties = properties };
            int count = Count(p, BoomboxDecorationCatalog.Slots.Length);
            var slots = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                var d = new BoomboxDecorationPlacement
                {
                    slot = p.ReadInt(),
                    prefab = Text(p, 128),
                    effect = Text(p, 32),
                    effectParticles = p.ReadBool(),
                    extraParticles = Text(p, 32)
                };
                if (!slots.Add(d.slot) || !BoomboxDecorationCatalog.DataEligible(d.prefab, d.slot)
                || !Enum.TryParse(d.effect, true, out BoomboxDecorationBonus _)
                || !Enum.TryParse(d.extraParticles, true, out BoomboxExtraParticles _))
                    throw new InvalidDataException("Invalid remote decoration");
                s.Appearance.decorationPlacements.Add(d);
                if (!s.Appearance.unlockedDecorationSlots.Contains(d.slot))
                    s.Appearance.unlockedDecorationSlots.Add(d.slot);
                if (!s.Appearance.unlockedDecorationPrefabs.Contains(d.prefab))
                    s.Appearance.unlockedDecorationPrefabs.Add(d.prefab);
            }
            s.Motion = Number(p);
            s.Glow = Number(p);
            s.Level = Number(p);
            s.Radius = Number(p);
            s.Time = Real(p);
            s.Silent = p.ReadBool();
            s.Variant = (BoomboxMusicEffect)p.ReadInt();
            if (!Enum.IsDefined(typeof(BoomboxMusicEffect), s.Variant))
                throw new InvalidDataException("Invalid remote effect");
            s.Smooth = new MusicRanges { Whole = Real(p), Low = Real(p), Mid = Real(p), High = Real(p) };
            count = Count(p, 256);
            for (int i = 0; i < count; i++)
            {
                var strike = new MusicStrike
                {
                    Time = Real(p),
                    Strength = Number(p),
                    Loudest = (MusicRange)p.ReadInt()
                };
                if (!Enum.IsDefined(typeof(MusicRange), strike.Loudest))
                    throw new InvalidDataException("Invalid remote range");
                s.Strikes.Add(strike);
            }
            if (p.GetPos() != p.Size())
                throw new InvalidDataException("Unexpected remote visual fields");
            s.Received = Time.realtimeSinceStartup;
            return s;
        }

        private static int Count(ZPackage p, int max)
        {
            int n = p.ReadInt();
            if (n < 0 || n > max)
                throw new InvalidDataException("Invalid visual collection");
            return n;
        }
        private static string Text(ZPackage p, int max)
        {
            string s = p.ReadString();
            if (s.Length > max)
                throw new InvalidDataException("Invalid visual text");
            return s;
        }
        private static float Number(ZPackage p)
        {
            float n = p.ReadSingle();
            if (!BoomboxAuraMath.Finite(n) || n < 0f)
                throw new InvalidDataException("Invalid visual number");
            return n;
        }
        private static double Real(ZPackage p)
        {
            double n = p.ReadDouble();
            if (double.IsNaN(n) || double.IsInfinity(n))
                throw new InvalidDataException("Invalid music time");
            return n;
        }
        private static bool Finite(
            Vector3 v
        ) => BoomboxAuraMath.Finite(v.x) && BoomboxAuraMath.Finite(v.y) && BoomboxAuraMath.Finite(v.z);
        private static bool Finite(
            Quaternion q
        ) => BoomboxAuraMath.Finite(q.x) && BoomboxAuraMath.Finite(q.y) && BoomboxAuraMath.Finite(q.z)
        && BoomboxAuraMath.Finite(q.w)
        && q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > .001f;
    }
}
