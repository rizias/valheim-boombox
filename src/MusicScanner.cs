using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace VikingBoombox
{
    public static class MusicScanner
    {
        private static readonly HashSet<string> audio = new HashSet<string>(
            new[] { ".flac", ".mp3", ".wav", ".ogg", ".ape", ".wv", ".m4a", ".aac", ".opus", ".wma" },
            StringComparer.OrdinalIgnoreCase
        );

        public static MusicCatalog Scan(
            IEnumerable<string> folders,
            MusicCatalog previous,
            string ffprobe,
            CancellationToken cancellation
        )
        {
            var catalog = new MusicCatalog { version = 2, generatedAt = DateTime.UtcNow.ToString("O") };
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder));
                    if (catalog.folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                        continue;
                    catalog.folders.Add(path);
                    Walk(path, files, catalog, cancellation);
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    Error(catalog, folder, error);
                }
            }
            var tracks = new List<MusicTrack>();
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files.Values.Where(p => Path.GetExtension(p)
                .Equals(".cue", StringComparison.OrdinalIgnoreCase)))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    foreach (var cue in MusicCue.Parse(File.ReadAllBytes(path)))
                    {
                        string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), cue.File));
                        var track = Track("file://" + path, cue);
                        track.available = File.Exists(target);
                        if (!track.available)
                            track.error = BoomboxText.Get("text.file_missing_or_inaccessible") + target;
                        tracks.Add(track);
                        covered.Add(target);
                    }
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    Error(catalog, path, error);
                }
            }
            foreach (var path in files.Values.Where(
                p => !covered.Contains(p) && !Path.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase)
            ))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        Archive(path, tracks, catalog, ffprobe, cancellation);
                    else
                    {
                        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        string extension = Path.GetExtension(path);
                        if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase))
                            using (var stream = File.OpenRead(path))
                                tags = MusicTags.Flac(stream);
                        else if (extension.Equals(".wv", StringComparison.OrdinalIgnoreCase))
                        {
                            var wavpack = MusicWavPack.Inspect(path);
                            if (wavpack.Cue != null)
                            {
                                tracks.AddRange(MusicCue.Parse(wavpack.Cue).Select(cue => Track(
                                            "file://" + path,
                                            cue
                                        )));
                                continue;
                            }
                            tags = MusicTags.Metadata(MusicProcess.Probe(
                                    path,
                                    null,
                                    wavpack.Embedded
                                    ? wavpack.Input(wavpack.Start)
                                    : null,
                                    ffprobe,
                                    cancellation
                                ));
                        }
                        else
                            tags = MusicTags.Metadata(MusicProcess.Probe(path, ffprobe, cancellation));
                        tracks.AddRange(Tagged("file://" + path, tags));
                    }
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    var track = Track("file://" + path);
                    track.available = false;
                    track.error = error.Message;
                    tracks.Add(track);
                }
            }
            var unique = tracks.GroupBy(t => t.id).Select(g => g.First()).OrderBy(
                t => t.albumArtist ?? t.artist,
                StringComparer.OrdinalIgnoreCase
            )
            .ThenBy(
                t => t.album,
                StringComparer.OrdinalIgnoreCase
            ).ThenBy(
                t => t.AlbumKey,
                StringComparer.OrdinalIgnoreCase
            )
            .ThenBy(
                t => t.discNumber
            ).ThenBy(
                t => t.trackNumber
            ).ThenBy(
                t => t.source,
                StringComparer.OrdinalIgnoreCase
            ).ThenBy(
                t => t.id,
                StringComparer.Ordinal
            ).ToList();
            PreserveIds(unique, previous, catalog);
            catalog.playlists.Add(
                new MusicPlaylist { id = "local-library", name = BoomboxText.Get("text.music_library"), tracks = unique }
            );
            return catalog;
        }

        private static void Walk(
            string folder,
            Dictionary<string, string> files,
            MusicCatalog catalog,
            CancellationToken cancellation
        )
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                foreach (string file in Directory.GetFiles(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string extension = Path.GetExtension(file);
                    if (audio.Contains(extension) || extension.Equals(".cue", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        files[Path.GetFullPath(file)] = Path.GetFullPath(file);
                }
                foreach (string child in Directory.GetDirectories(folder)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        Walk(child, files, catalog, cancellation);
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                Error(catalog, folder, error);
            }
        }

        private static void Archive(
            string path,
            List<MusicTrack> tracks,
            MusicCatalog catalog,
            string ffprobe,
            CancellationToken cancellation
        )
        {
            using (var input = File.OpenRead(path))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            {
                var names = new List<string>();
                var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (entry.Name.Length == 0)
                        continue;
                    try
                    {
                        names.Add(MusicSource.SafeMember(MusicSource.Entry(archive, entry.FullName).FullName));
                    }
                    catch (Exception error)
                    {
                        Error(catalog, path, error);
                    }
                }
                foreach (string name in names.Where(n => Path.GetExtension(n)
                    .Equals(".cue", StringComparison.OrdinalIgnoreCase)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string raw = "unpack://zip|0|file://" + path + "|" + name;
                    try
                    {
                        var cues = MusicCue.Parse(MusicSource.Parse(raw).Read());
                        string parent = name.Contains("/") ? name.Substring(0, name.LastIndexOf('/') + 1) : "";
                        foreach (var cue in cues)
                        {
                            var track = Track(raw, cue);
                            try
                            {
                                string member = MusicSource.SafeMember(parent + MusicSource.SafeMember(cue.File));
                                MusicSource.Entry(archive, member);
                                covered.Add(member);
                            }
                            catch (Exception error)
                            {
                                track.available = false;
                                track.error = error.Message;
                            }
                            tracks.Add(track);
                        }
                    }
                    catch (Exception error) when (!(error is OperationCanceledException))
                    {
                        Error(catalog, path + "|" + name, error);
                    }
                }
                foreach (string name in names.Where(n => audio.Contains(Path.GetExtension(n)) && !covered.Contains(n)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        string raw = "unpack://zip|0|file://" + path + "|" + name;
                        if (Path.GetExtension(name).Equals(".flac", StringComparison.OrdinalIgnoreCase))
                            using (var stream = MusicSource.Entry(archive, name).Open())
                                tags = MusicTags.Flac(stream);
                        else
                            tags = ArchiveMetadata(raw, ffprobe, cancellation);
                        tracks.AddRange(Tagged(raw, tags));
                    }
                    catch (Exception error) when (!(error is OperationCanceledException))
                    {
                        Error(catalog, path + "|" + name, error);
                    }
                }
            }
        }

        private static Dictionary<string, string> ArchiveMetadata(
            string raw,
            string ffprobe,
            CancellationToken cancellation
        )
        {
            string temporary = Path.Combine(Path.GetTempPath(), "ValheimBoombox-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                string file = MusicSource.Parse(raw).Materialize(null, temporary, cancellation, out var cue);
                return MusicTags.Metadata(MusicProcess.Probe(file, ffprobe, cancellation));
            }
            finally
            {
                Directory.Delete(temporary, true);
            }
        }

        private static IEnumerable<MusicTrack> Tagged(string raw, Dictionary<string, string> tags)
        {
            var cues = MusicTags.Embedded(tags);
            if (cues != null)
                return cues.Select(c => Track(raw, c)).ToArray();
            var track = Track(raw);
            if (tags.TryGetValue("title", out var title))
                track.title = title;
            if (tags.TryGetValue("artist", out var artist))
                track.artist = artist;
            if (tags.TryGetValue("album", out var album))
                track.album = album;
            if (tags.TryGetValue("album_artist", out var albumArtist)
                || tags.TryGetValue("albumartist", out albumArtist))
                track.albumArtist = albumArtist;
            if (tags.TryGetValue("track", out var number) || tags.TryGetValue("tracknumber", out number))
                track.trackNumber = Number(number);
            if (tags.TryGetValue("disc", out var disc) || tags.TryGetValue("discnumber", out disc))
                track.discNumber = Number(disc);
            return new[] { track };
        }

        private static int Number(string value) => int.TryParse(value.Split('/')[0], out int number) ? number : 0;

        private static MusicTrack Track(string raw, MusicCue cue = null)
        {
            var source = MusicSource.Parse(raw);
            return new MusicTrack
            {
                id = MusicSource.TrackId(raw, cue?.Number),
                source = raw,
                subsong = cue?.Number,
                title = cue?.Title ?? Path.GetFileNameWithoutExtension(source.Member ?? source.Path),
                artist = cue?.Artist ?? "",
                album = cue?.Album,
                albumArtist = cue?.AlbumArtist,
                trackNumber = cue?.Number ?? 0,
                discNumber = cue?.DiscNumber ?? 0,
                durationSeconds = cue?.End != null ? cue.End.Value - cue.Start.Value : 0
            };
        }

        private static void PreserveIds(List<MusicTrack> tracks, MusicCatalog previous, MusicCatalog next)
        {
            var old = new Dictionary<string, List<MusicTrack>>(StringComparer.Ordinal);
            foreach (var track in previous?.playlists.SelectMany(p => p.tracks) ?? Enumerable.Empty<MusicTrack>())
                try
                {
                    string key = MusicSource.Parse(track.source).Identity(track.subsong);
                    if (!old.TryGetValue(key, out var entries))
                        old[key] = entries = new List<MusicTrack>();
                    entries.Add(track);
                }
                catch (Exception error) when (error is ArgumentException
                    || error is IOException
                    || error is NotSupportedException)
                {
                }
            foreach (var track in tracks)
                if (old.TryGetValue(MusicSource.Parse(track.source).Identity(track.subsong), out var entries))
                {
                    string generated = track.id;
                    track.id = entries[0].id;
                    if (generated != track.id)
                        next.aliases[generated] = track.id;
                    foreach (var entry in entries)
                        if (entry.id != track.id)
                            next.aliases[entry.id] = track.id;
                }
            var ids = new HashSet<string>(tracks.Select(t => t.id));
            foreach (var alias in previous?.aliases ?? new Dictionary<string, string>())
                if (ids.Contains(alias.Value))
                    next.aliases[alias.Key] = alias.Value;
        }

        private static void Error(
            MusicCatalog catalog,
            string path,
            Exception error
        ) => catalog.errors.Add(new MusicCatalogError { path = path, error = error.Message });
    }
}
