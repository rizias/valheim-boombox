using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VikingBoombox
{
    internal sealed class MusicWavPack
    {
        private const int Sector = 2048;
        private const int ChunkBytes = 256 * 1024;
        private const int HeaderBytes = 32;
        public long Start;
        public long End;
        public long FirstIndex;
        public long Samples;
        public byte[] Cue;
        public double Rate;

        public bool Embedded => Start > 0;

        public static MusicWavPack Inspect(string path)
        {
            using (var file = File.OpenRead(path))
            {
                long start = 0, end = file.Length;
                byte[] cue = null;
                if (IsIso(file))
                {
                    var chosen = Choose(IsoFiles(file), out cue);
                    start = chosen.Offset;
                    end = chosen.Offset + chosen.Size;
                }
                var first = NextBlock(file, start, end)
                    ?? throw new InvalidDataException(BoomboxText.Get("text.the_file_contains_no_wavpack_audio") + path);
                return new MusicWavPack
                {
                    Start = first.Offset,
                    End = end,
                    FirstIndex = first.Index,
                    Samples = first.Index == 0
                        ? first.Total
                        : -1,
                    Cue = cue
                };
            }
        }

        public string[] Input(
            long offset
        ) => new[] { "-f", "wv", "-skip_initial_bytes", offset.ToString(CultureInfo.InvariantCulture) };

        public Block Seek(string path, double seconds)
        {
            long target = FirstIndex + (long)Math.Round(seconds * Rate);
            using (var file = File.OpenRead(path))
            {
                var best = NextBlock(file, Start, End)
                    ?? throw new InvalidDataException(BoomboxText.Get("text.the_file_contains_no_wavpack_audio") + path);
                long low = best.Offset + best.Size, high = End;
                while (low < high && !(best.Index <= target && target < best.Index + best.Samples))
                {
                    long middle = low + (high - low) / 2;
                    var block = NextBlock(file, middle, End);
                    if (block == null || block.Index > target)
                        high = middle;
                    else
                    {
                        best = block;
                        low = block.Offset + block.Size;
                    }
                }
                best.Seconds = (best.Index - FirstIndex) / Rate;
                return best;
            }
        }

        private static bool IsIso(FileStream file)
        {
            if (file.Length < 17 * Sector)
                return false;
            return Encoding.ASCII.GetString(Read(file, 16 * Sector + 1, 5)) == "CD001";
        }

        private static IsoFile Choose(List<IsoFile> files, out byte[] cue)
        {
            var sounds = files.Where(f => Path.GetExtension(f.Name)
                .Equals(".wv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                f => f.Name,
                StringComparer.OrdinalIgnoreCase
            )
                .ToList();
            if (sounds.Count == 0)
                throw new InvalidDataException(BoomboxText.Get("text.the_disc_image_contains_no_wv_file"));
            var sheets = files.Where(f => Path.GetExtension(f.Name)
                .Equals(".cue", StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                f => f.Name,
                StringComparer.OrdinalIgnoreCase
            )
                .ToList();
            foreach (var sheet in sheets)
            {
                byte[] bytes = sheet.Bytes;
                List<MusicCue> tracks;
                try
                {
                    tracks = MusicCue.Parse(bytes);
                }
                catch (Exception error) when (error is InvalidDataException
                    || error is FormatException
                    || error is OverflowException
                    || error is DecoderFallbackException)
                {
                    continue;
                }
                var target = sounds.FirstOrDefault(s => tracks.Any(t => SameName(t.File, s.Name)));
                if (target != null)
                {
                    cue = bytes;
                    return target;
                }
            }
            var root = sheets.Where(s => !s.Name.Contains("/")).ToList();
            cue = root.Count == 1 ? root[0].Bytes : null;
            return sounds[0];
        }

        private static bool SameName(string reference, string name) =>
        Path.GetFileName((reference ?? "").Replace('\\', '/')).Equals(
            Path.GetFileName(name),
            StringComparison.OrdinalIgnoreCase
        );

        private static List<IsoFile> IsoFiles(FileStream file)
        {
            byte[] root = null;
            bool joliet = false;
            for (long sector = 16; (sector + 1) * Sector <= file.Length; sector++)
            {
                var descriptor = Read(file, sector * Sector, Sector);
                if (Encoding.ASCII.GetString(descriptor, 1, 5) != "CD001" || descriptor[0] == 255)
                    break;
                if (descriptor[0] == 1 && root == null)
                    root = descriptor;
                if (descriptor[0] == 2 && descriptor[88] == 0x25 && descriptor[89] == 0x2F
                && (descriptor[90] == 0x40 || descriptor[90] == 0x43 || descriptor[90] == 0x45))
                {
                    root = descriptor;
                    joliet = true;
                }
            }
            if (root == null)
                throw new InvalidDataException(BoomboxText.Get("text.the_disc_image_has_no_directory"));
            var files = new List<IsoFile>();
            Walk(
                file,
                BitConverter.ToUInt32(root, 156 + 2),
                BitConverter.ToUInt32(root, 156 + 10),
                joliet,
                "",
                files,
                new HashSet<long>()
            );
            return files;
        }

        private static void Walk(
            FileStream file,
            long extent,
            long size,
            bool joliet,
            string prefix,
            List<IsoFile> files,
            HashSet<long> visited
        )
        {
            if (!visited.Add(extent) || extent * Sector + size > file.Length)
                return;
            var data = Read(file, extent * Sector, (int)size);
            for (int i = 0; i < data.Length;)
            {
                int length = data[i];
                if (length == 0)
                {
                    i = (i / Sector + 1) * Sector;
                    continue;
                }
                if (i + length > data.Length || length < 34)
                    break;
                int nameLength = data[i + 32];
                bool directory = (data[i + 25] & 2) != 0;
                long location = BitConverter.ToUInt32(data, i + 2), bytes = BitConverter.ToUInt32(data, i + 10);
                if (!(nameLength == 1 && data[i + 33] <= 1))
                {
                    string name = joliet
                        ? Encoding.BigEndianUnicode.GetString(data, i + 33, nameLength)
                        : Encoding.ASCII.GetString(data, i + 33, nameLength);
                    int version = name.IndexOf(';');
                    if (version >= 0)
                        name = name.Substring(0, version);
                    if (directory)
                        Walk(file, location, bytes, joliet, prefix + name + "/", files, visited);
                    else
                        files.Add(
                            new IsoFile { Name = prefix + name, Offset = location * Sector, Size = bytes, File = file }
                        );
                }
                i += length;
            }
        }

        private static Block NextBlock(FileStream file, long from, long end)
        {
            for (long position = from; position < end;)
            {
                var chunk = Read(file, position, (int)Math.Min(ChunkBytes, end - position));
                for (int i = 0; i + HeaderBytes <= chunk.Length; i++)
                {
                    if (chunk[i] != (byte)'w'
                        || chunk[i + 1] != (byte)'v'
                        || chunk[i + 2] != (byte)'p'
                        || chunk[i + 3] != (byte)'k')
                        continue;
                    long size = BitConverter.ToUInt32(chunk, i + 4) + 8L;
                    int version = BitConverter.ToUInt16(chunk, i + 8);
                    long samples = BitConverter.ToUInt32(chunk, i + 20);
                    uint flags = BitConverter.ToUInt32(chunk, i + 24);
                    if (version < 0x402
                        || version > 0x410
                        || size < HeaderBytes
                        || size > (8 << 20)
                        || (flags & 0x800) == 0
                        || samples == 0)
                        continue;
                    long index = BitConverter.ToUInt32(chunk, i + 16) | ((long)chunk[i + 10] << 32);
                    uint total = BitConverter.ToUInt32(chunk, i + 12);
                    return new Block
                    {
                        Offset = position + i,
                        Size = size,
                        Index = index,
                        Samples = samples,
                        Total = total == uint.MaxValue ? -1 : chunk[i + 11] * (long)uint.MaxValue + total
                    };
                }
                if (chunk.Length < HeaderBytes)
                    break;
                position += chunk.Length - HeaderBytes + 1;
            }
            return null;
        }

        private static byte[] Read(FileStream file, long offset, int count)
        {
            var buffer = new byte[count];
            file.Position = offset;
            int done = 0;
            while (done < count)
            {
                int read = file.Read(buffer, done, count - done);
                if (read <= 0)
                    break;
                done += read;
            }
            if (done < count)
                Array.Resize(ref buffer, done);
            return buffer;
        }

        internal sealed class Block
        {
            public long Offset;
            public long Size;
            public long Index;
            public long Samples;
            public long Total;
            public double Seconds;
        }

        private sealed class IsoFile
        {
            public string Name;
            public long Offset;
            public long Size;
            public FileStream File;
            public byte[] Bytes => Read(File, Offset, (int)Size);
        }
    }
}
