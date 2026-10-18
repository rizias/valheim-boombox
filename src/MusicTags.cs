using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace VikingBoombox
{
    internal static class MusicTags
    {
        public static Dictionary<string, string> Metadata(JObject data)
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stream = (data["streams"] as JArray)?.FirstOrDefault(s => (string)s["codec_type"] == "audio");
            foreach (var section in new[] { stream, data["format"] })
                if (section?["tags"] is JObject values)
                    foreach (var item in values)
                        tags[item.Key] = (string)item.Value;
            return tags;
        }

        public static Dictionary<string, string> Flac(Stream stream)
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Encoding.ASCII.GetString(Read(stream, 4)) != "fLaC")
                return tags;
            while (true)
            {
                var header = Read(stream, 4);
                int size = (header[1] << 16) | (header[2] << 8) | header[3];
                if ((header[0] & 127) == 4)
                {
                    using (var block = new BinaryReader(new MemoryStream(Read(stream, size)), Encoding.UTF8))
                    {
                        block.BaseStream.Position = 4L + block.ReadUInt32();
                        uint count = block.ReadUInt32();
                        for (uint i = 0; i < count; i++)
                        {
                            int length = checked((int)block.ReadUInt32());
                            string text = Encoding.UTF8.GetString(Read(block.BaseStream, length));
                            int split = text.IndexOf('=');
                            if (split > 0)
                                tags[text.Substring(0, split)] = text.Substring(split + 1);
                        }
                    }
                }
                else if (stream.CanSeek)
                    stream.Seek(size, SeekOrigin.Current);
                else
                {
                    var buffer = new byte[8192];
                    while (size > 0)
                    {
                        int count = stream.Read(buffer, 0, Math.Min(size, buffer.Length));
                        if (count == 0)
                            throw new EndOfStreamException(BoomboxText.Get("text.flac_header_is_truncated"));
                        size -= count;
                    }
                }
                if ((header[0] & 128) != 0)
                    return tags;
            }
        }

        public static List<MusicCue> Embedded(Dictionary<string, string> tags)
        {
            if (!tags.TryGetValue("cuesheet", out var text) || string.IsNullOrEmpty(text))
                return null;
            var tracks = MusicCue.Parse(text);
            if (tracks.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                throw new InvalidDataException(BoomboxText.Get("text.ambiguous_embedded_cue_with_multiple_file_entries"));
            return tracks;
        }

        private static byte[] Read(Stream stream, int size)
        {
            var bytes = new byte[size];
            int offset = 0;
            while (offset < size)
            {
                int count = stream.Read(bytes, offset, size - offset);
                if (count == 0)
                    throw new EndOfStreamException(BoomboxText.Get("text.flac_header_is_truncated"));
                offset += count;
            }
            return bytes;
        }
    }
}
