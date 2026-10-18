using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace VikingBoombox
{
    public sealed class UserPlaylist
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name;
        public List<string> tracks = new List<string>();
    }

    public sealed class UserMusicLibrary
    {
        public int version = 2;
        public List<string> folders = new List<string>();
        public List<UserPlaylist> playlists = new List<UserPlaylist>();
        public Dictionary<string, MusicTrack> rememberedTracks = new Dictionary<string, MusicTrack>();
        public List<string> importedLegacy = new List<string>();
        [JsonIgnore] public string FilePath;
        private bool dirty;

        public static UserMusicLibrary Load(string root)
        {
            string path = Path.Combine(root, "library.json");
            var library = StateStore.Read<UserMusicLibrary>(path) ?? new UserMusicLibrary();
            if (library.version != 2
                || library.folders == null
                || library.playlists == null
                || library.rememberedTracks == null
                || library.importedLegacy == null)
                throw new InvalidDataException(
                    BoomboxText.Get("text.unknown_music_library_format_the_file_remains_unchanged")
                );
            library.FilePath = path;
            library.dirty = !File.Exists(path);
            library.Save();
            return library;
        }

        public bool AddFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(BoomboxText.Get("text.folder_missing_or_inaccessible") + path);
            if (path.Length > Path.GetPathRoot(path).Length)
                path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                return false;
            folders.Add(path);
            dirty = true;
            Save();
            return true;
        }

        public void RemoveFolder(string value)
        {
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
            if (path.Length > Path.GetPathRoot(path).Length)
                path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (folders.RemoveAll(folder => string.Equals(folder, path, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                dirty = true;
                Save();
            }
        }

        public UserPlaylist Create(string name, IEnumerable<string> tracks)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(BoomboxText.Get("text.enter_a_playlist_name"));
            var playlist = new UserPlaylist
            {
                name = name.Trim(),
                tracks = tracks.Where(id => !string.IsNullOrEmpty(id))
                .ToList()
            };
            playlists.Add(playlist);
            dirty = true;
            Save();
            return playlist;
        }

        public void Update(UserPlaylist playlist, string name, IEnumerable<string> tracks)
        {
            if (!playlists.Contains(playlist) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(BoomboxText.Get("text.select_a_playlist_and_enter_its_name"));
            playlist.name = name.Trim();
            playlist.tracks = tracks.Where(id => !string.IsNullOrEmpty(id)).ToList();
            dirty = true;
            Save();
        }

        public void Delete(UserPlaylist playlist)
        {
            if (playlists.Remove(playlist))
            {
                dirty = true;
                Save();
            }
        }

        public void Import(string key, string name, IEnumerable<string> tracks)
        {
            ImportCore(key, name, tracks, false);
            Save();
        }

        public void ImportCatalog(MusicCatalog catalog)
        {
            if (catalog == null || catalog.version > 1)
                return;
            for (int index = 0; index < catalog.playlists.Count; index++)
            {
                var playlist = catalog.playlists[index];
                ImportCore(
                    "catalog:" + (playlist.id ?? MusicSource.Hash(playlist.name + "\n" + index)),
                    playlist.name,
                    playlist.tracks.Select(t => t.id),
                    true
                );
            }
            Save();
        }

        private void ImportCore(string key, string name, IEnumerable<string> tracks, bool preserveEmpty)
        {
            if (importedLegacy.Contains(key))
                return;
            var ids = tracks?.Where(id => !string.IsNullOrEmpty(id)).ToList() ?? new List<string>();
            importedLegacy.Add(key);
            if (ids.Count > 0 || preserveEmpty)
                playlists.Add(new UserPlaylist
                {
                    name = string.IsNullOrWhiteSpace(name)
                        ? BoomboxText.Get("text.previous_playlist")
                        : name,
                    tracks = ids
                });
            dirty = true;
        }

        public void Remember(MusicTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.id))
                return;
            if (rememberedTracks.TryGetValue(track.id, out var old) && old.SameMetadata(track))
                return;
            rememberedTracks[track.id] = track.Snapshot();
            dirty = true;
        }

        public void Save()
        {
            if (!dirty || string.IsNullOrEmpty(FilePath))
                return;
            StateStore.AtomicWrite(FilePath, this);
            dirty = false;
        }
    }
}
