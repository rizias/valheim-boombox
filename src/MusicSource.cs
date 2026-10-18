using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace VikingBoombox
{
    internal sealed class MusicSource
    {
        public string Path;
        public string Member;
        public string Identity(int? subsong) => Path.ToUpperInvariant() + "\t" + (Member ?? "") + "\t" + subsong;

        public static string AlbumLocation(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            try
            {
                var source = ParseMetadata(value);
                return source.Member == null
                    ? System.IO.Path.GetDirectoryName(source.Path).ToUpperInvariant()
                    : source.Path.ToUpperInvariant() + "|" + System.IO.Path.GetDirectoryName(source.Member);
            }
            catch (Exception error) when (error is ArgumentException
                || error is IOException
                || error is NotSupportedException)
            {
                return value;
            }
        }

        public static MusicSource Parse(string value)
        {
            var source = SourceParts(value);
            source.Path = LocalPath(source.Path);
            return source;
        }

        internal static MusicSource ParseMetadata(string value)
        {
            var source = SourceParts(value);
            string path = source.Path;
            if (!path.StartsWith("file://", StringComparison.Ordinal))
                throw new InvalidDataException(BoomboxText.Get("text.unsupported_source") + path);
            path = path.Substring(7);
            if (path.Length > 3 && path[0] == '/' && path[2] == ':')
                path = path.Substring(1);
            source.Path = System.IO.Path.GetFullPath(path);
            return source;
        }

        private static MusicSource SourceParts(string value)
        {
            if (value == null)
                throw new InvalidDataException(BoomboxText.Get("text.music_source_is_missing"));
            if (value.StartsWith("unpack://zip|", StringComparison.Ordinal))
            {
                var parts = value.Split(new[] { '|' }, 4);
                if (parts.Length != 4)
                    throw new InvalidDataException(BoomboxText.Get("text.invalid_zip_source"));
                return new MusicSource { Path = parts[2], Member = SafeMember(parts[3]) };
            }
            return new MusicSource { Path = value };
        }

        public static string LocalPath(string value)
        {
            if (!value.StartsWith("file://", StringComparison.Ordinal))
                throw new InvalidDataException(BoomboxText.Get("text.unsupported_source") + value);
            string path = value.Substring(7);
            if (path.Length > 3 && path[0] == '/' && path[2] == ':')
                path = path.Substring(1);
            if (!File.Exists(path))
                path = Uri.UnescapeDataString(path);
            return System.IO.Path.GetFullPath(path);
        }

        public static string SafeMember(string value)
        {
            string name = value.Replace('\\', '/');
            if (name.Length == 0
                || name.StartsWith("/", StringComparison.Ordinal)
                || name.Contains(":")
                || name.Contains("\0")
                || name.Split('/').Contains(".."))
                throw new InvalidDataException(BoomboxText.Get("text.invalid_path_inside_zip") + value);
            return string.Join("/", name.Split('/').Where(p => p.Length > 0 && p != "."));
        }

        public static ZipArchiveEntry Entry(ZipArchive archive, string name)
        {
            name = SafeMember(name);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == name);
            if (entry == null)
                throw new FileNotFoundException(BoomboxText.Get("text.zip_is_missing_the_file") + name);
            SafeMember(entry.FullName);
            if (entry.Name.Length == 0 || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException(BoomboxText.Get("text.zip_entry_is_not_a_regular_file") + name);
            return entry;
        }

        public byte[] Read()
        {
            if (Member == null)
                return File.ReadAllBytes(Path);
            using (var input = File.OpenRead(Path))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            using (var stream = Entry(archive, Member).Open())
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                return bytes.ToArray();
            }
        }

        public MusicLiveSource Locate(int? subsong, out MusicCue cue)
        {
            cue = null;
            if (!File.Exists(Path))
                throw new FileNotFoundException(BoomboxText.Get("text.file_missing_or_inaccessible") + Path);
            if (Member == null)
            {
                if (!System.IO.Path.GetExtension(Path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
                    return new MusicLiveSource { File = Path };
                cue = MusicCue.Select(MusicCue.Parse(Read()), subsong);
                string target = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), cue.File)
                );
                if (!File.Exists(target))
                    throw new FileNotFoundException(BoomboxText.Get("text.file_missing_or_inaccessible") + target);
                return new MusicLiveSource { File = target };
            }
            string member = Member;
            if (System.IO.Path.GetExtension(member).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                cue = MusicCue.Select(MusicCue.Parse(Read()), subsong);
                string parent = member.Contains("/") ? member.Substring(0, member.LastIndexOf('/') + 1) : "";
                member = SafeMember(parent + SafeMember(cue.File));
            }
            return new MusicLiveSource { ZipPath = Path, ZipMember = member };
        }

        public string Materialize(int? subsong, string directory, CancellationToken cancellation, out MusicCue cue)
        {
            cue = null;
            if (!File.Exists(Path))
                throw new FileNotFoundException(BoomboxText.Get("text.file_missing_or_inaccessible") + Path);
            if (Member == null)
            {
                if (!System.IO.Path.GetExtension(Path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
                    return Path;
                cue = MusicCue.Select(MusicCue.Parse(Read()), subsong);
                string target = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), cue.File)
                );
                if (!File.Exists(target))
                    throw new FileNotFoundException(BoomboxText.Get("text.file_missing_or_inaccessible") + target);
                return target;
            }
            string member = Member;
            if (System.IO.Path.GetExtension(member).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                cue = MusicCue.Select(MusicCue.Parse(Read()), subsong);
                string parent = member.Contains("/") ? member.Substring(0, member.LastIndexOf('/') + 1) : "";
                member = SafeMember(parent + SafeMember(cue.File));
            }
            string destination = System.IO.Path.Combine(directory, "source" + System.IO.Path.GetExtension(member));
            using (var input = File.OpenRead(Path))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            using (var stream = Entry(archive, member).Open())
            using (var output = File.Create(destination))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, count);
                }
            }
            return destination;
        }

        public static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", "")
                    .ToLowerInvariant();
        }

        public static string TrackId(string source, int? subsong) => Hash(source + "\t" + subsong).Substring(0, 32);
    }
}
