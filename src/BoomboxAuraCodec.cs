using System;
using System.Collections.Generic;
using System.IO;

namespace VikingBoombox
{
    internal static class BoomboxAuraCodec
    {
        internal static void Header(ZPackage package, string session, long revision)
        {
            package.Write(session);
            package.Write(revision);
        }

        internal static string ReadHeader(ZPackage package, out long revision)
        {
            if (package.Size() > 1024 * 1024)
                throw new InvalidDataException("Boombox state packet too large");
            string session = Text(package, 64);
            if (!System.Guid.TryParseExact(session, "N", out _))
                throw new InvalidDataException("Invalid Boombox session");
            revision = package.ReadLong();
            if (revision < 1)
                throw new InvalidDataException("Invalid Boombox revision");
            return session;
        }

        internal static void Sources(ZPackage package, ICollection<BoomboxAuraSource> sources)
        {
            package.Write(sources.Count);
            foreach (var source in sources)
            {
                package.Write(source.Id);
                package.Write(source.Owner);
                package.Write(source.OwnerName ?? "");
                package.Write(source.Peer);
                package.Write(source.WorldItem);
                package.Write(source.Worn);
                package.Write(source.Active);
                package.Write(source.Position);
                foreach (int rank in source.Ranks)
                    package.Write(rank);
                package.Write(source.Decorations.Count);
                foreach (var decoration in source.Decorations)
                {
                    package.Write(decoration.Slot);
                    package.Write(decoration.Prefab);
                    package.Write(decoration.Effect);
                }
            }
        }

        internal static List<BoomboxAuraSource> ReadSources(ZPackage package)
        {
            int count = Count(package, package.Size() / 8);
            var result = new List<BoomboxAuraSource>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                var source = new BoomboxAuraSource
                {
                    Id = Text(package, 64),
                    Owner = package.ReadLong(),
                    OwnerName = Text(package, 128),
                    Peer = package.ReadLong(),
                    WorldItem = package.ReadZDOID(),
                    Worn = package.ReadBool(),
                    Active = package.ReadBool(),
                    Position = package.ReadVector3()
                };
                if (!System.Guid.TryParseExact(source.Id, "N", out _)
                    || !ids.Add(source.Id)
                    || !BoomboxAuraMath.Finite(source.Position.x)
                    || !BoomboxAuraMath.Finite(source.Position.y)
                    || !BoomboxAuraMath.Finite(source.Position.z))
                    throw new InvalidDataException("Invalid Boombox identity or position");
                for (int rank = 0; rank < 5; rank++)
                {
                    source.Ranks[rank] = package.ReadInt();
                    if (source.Ranks[rank] < 0)
                        throw new InvalidDataException("Invalid cassette rank");
                }
                int decorations = Count(package, BoomboxDecorationCatalog.Slots.Length);
                var slots = new HashSet<int>();
                for (int item = 0; item < decorations; item++)
                {
                    var decoration = new BoomboxAuraDecoration
                    {
                        Slot = package.ReadInt(),
                        Prefab = Text(package, 128),
                        Effect = package.ReadInt()
                    };
                    if (!slots.Add(decoration.Slot)
                        || decoration.Slot < 0
                        || decoration.Slot >= BoomboxDecorationCatalog.Slots.Length
                        || decoration.Effect < 0
                        || decoration.Effect >= 5)
                        throw new InvalidDataException("Invalid Boombox decoration");
                    source.Decorations.Add(decoration);
                }
                result.Add(source);
            }
            return result;
        }

        internal static void Balance(ZPackage package, BoomboxAuraBalance balance)
        {
            package.Write(balance.Radius);
            package.Write(balance.MusicOnly);
            package.Write(balance.Fallback ?? "Health");
            foreach (var values in new[] {
                balance.Strength,
                balance.Cassette,
                balance.Decoration,
                balance.Initial,
                balance.PerRank,
                balance.Period
            })
                foreach (float value in values)
                    package.Write(value);
            foreach (float value in balance.WaveRadius)
                package.Write(value);
        }

        internal static BoomboxAuraBalance ReadBalance(ZPackage package)
        {
            var result = new BoomboxAuraBalance
            {
                Radius = Number(package),
                MusicOnly = package.ReadBool(),
                Fallback = Text(package, 32)
            };
            foreach (var values in new[] {
                result.Strength,
                result.Cassette,
                result.Decoration,
                result.Initial,
                result.PerRank,
                result.Period
            })
                for (int index = 0; index < values.Length; index++)
                    values[index] = Number(package, values == result.Initial || values == result.PerRank);
            foreach (float period in result.Period)
                if (period <= 0f)
                    throw new InvalidDataException("Invalid recovery period");
            for (int tier = 0; tier < result.WaveRadius.Length; tier++)
                result.WaveRadius[tier] = Number(package);
            return result;
        }

        private static float Number(ZPackage package, bool signed = false)
        {
            float value = package.ReadSingle();
            if (!BoomboxAuraMath.Finite(value) || !signed && value < 0f)
                throw new InvalidDataException("Invalid shared balance");
            return value;
        }

        private static int Count(ZPackage package, int maximum)
        {
            int value = package.ReadInt();
            if (value < 0 || value > maximum)
                throw new InvalidDataException("Invalid Boombox collection");
            return value;
        }
        private static string Text(ZPackage package, int maximum)
        {
            string value = package.ReadString();
            if (value.Length > maximum)
                throw new InvalidDataException("Invalid Boombox text");
            return value;
        }
    }
}
