using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VikingBoombox
{
    internal sealed class BoomboxLibraryData
    {
        private int revision = -1;
        private string appliedSearch;
        private string appliedFolder;
        private string appliedLanguage;
        private MusicTrack[] all = new MusicTrack[0];
        private Dictionary<string, string> folderOf = new Dictionary<string, string>();
        internal MusicTrack[] Tracks
        {
            get;
            private set;
        } = new MusicTrack[0];
        internal Dictionary<string, MusicTrack[]> Albums
        {
            get;
            private set;
        } = new Dictionary<string, MusicTrack[]>();

        internal bool Refresh(BoomboxPlugin plugin, string search, string folder)
        {
            string language = Localization.instance.GetSelectedLanguage();
            if (revision == plugin.CatalogRevision
                && appliedSearch == search
                && appliedFolder == folder
                && appliedLanguage == language)
                return false;
            if (revision != plugin.CatalogRevision)
            {
                all = plugin.Catalog.playlists.SelectMany(p => p.tracks)
                    .GroupBy(t => t.id)
                    .Select(g => g.First())
                    .ToArray();
                Albums = all.Where(t => !string.IsNullOrEmpty(t.album)).GroupBy(t => t.AlbumKey).ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(t => t.discNumber).ThenBy(t => t.trackNumber).ToArray()
                );
                var folders = plugin.Catalog.folders ?? new List<string>();
                folderOf = all.ToDictionary(t => t.id, t => FolderOf(t.source, folders));
            }
            revision = plugin.CatalogRevision;
            appliedSearch = search;
            appliedFolder = folder;
            appliedLanguage = language;
            var synthetic = folder != null
                ? Enumerable.Empty<MusicTrack>()
                : BoomboxSyntheticTrackId.All.Select(plugin.FindTrack).Where(t => Matches(t.Label, search));
            var library = all.Where(t => (folder == null || folderOf[t.id] == folder)
                && (Matches(t.Label, search) || Matches(t.album, search) || Matches(t.source, search)));
            Tracks = synthetic.Concat(library).ToArray();
            return true;
        }

        private static bool Matches(
            string value,
            string search
        ) => string.IsNullOrWhiteSpace(search)
            || (value ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string FolderOf(string source, List<string> folders)
        {
            string path;
            try
            {
                path = string.IsNullOrEmpty(source) ? null : MusicSource.ParseMetadata(source).Path;
            }
            catch (Exception error) when (error is ArgumentException
                || error is IOException
                || error is InvalidDataException
                || error is NotSupportedException)
            {
                path = null;
            }
            if (path == null)
                return null;
            return folders.Where(
                f => path.StartsWith(
                    f.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            ).OrderByDescending(
                f => f.Length
            ).FirstOrDefault();
        }
    }
}
