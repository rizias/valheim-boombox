using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class MusicCatalog
    {
        public int version = 1;
        public string generatedAt;
        public List<string> folders = new List<string>();
        public List<MusicPlaylist> playlists = new List<MusicPlaylist>();
        public Dictionary<string, string> aliases = new Dictionary<string, string>();
        public List<MusicCatalogError> errors = new List<MusicCatalogError>();
    }

    public sealed class MusicCatalogError
    {
        public string path;
        public string error;
    }

    public sealed class MusicPlaylist
    {
        public string id;
        public string name;
        public List<MusicTrack> tracks = new List<MusicTrack>();
    }

    public sealed class MusicTrack
    {
        public string id;
        public string title;
        public string artist;
        public string album;
        public string albumArtist;
        public int discNumber;
        public int trackNumber;
        public string path;
        public string source;
        public int? subsong;
        public double duration;
        public double durationSeconds;
        public bool available = true;
        public string error;
        [JsonIgnore] public double KnownDuration => durationSeconds > 0 ? durationSeconds : duration;

        public MusicTrack Snapshot() => (MusicTrack)MemberwiseClone();

        public bool SameMetadata(MusicTrack other)
        {
            return other != null
                && title == other.title
                && artist == other.artist
                && duration == other.duration
                && durationSeconds == other.durationSeconds
                && source == other.source
                && path == other.path
                && subsong == other.subsong
                && album == other.album
                && albumArtist == other.albumArtist
                && discNumber == other.discNumber
                && trackNumber == other.trackNumber
                && available == other.available
                && error == other.error;
        }

        [JsonIgnore] public string Label => string.IsNullOrEmpty(artist) ? title ?? id : artist + " — " + (title ?? id);
        [JsonIgnore]
        public string SelectionLabel => Label + (available ? "" : BoomboxText.Get("text.source_unavailable"));
        [JsonIgnore]
        public string AlbumKey => string.IsNullOrEmpty(album)
            ? ""
            : (albumArtist ?? artist ?? "") + "\n" + album + "\n" + MusicSource.AlbumLocation(source);
    }

    public sealed class Cassette
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name;
        public List<string> tracks = new List<string>();
    }

    public sealed class CassetteLibrary
    {
        public List<Cassette> cassettes = new List<Cassette>();
    }

    public sealed class DeckState
    {
        public string id = Guid.NewGuid().ToString("N");
        public long owner;
        public bool onBack = true;
        public float x;
        public float y;
        public float z;
        public float yaw;
        public string cassetteId;
        public string playlistId;
        public RecordedCassette insertedCassette;
        public List<string> tracks = new List<string>();
        public int trackIndex;
        public float seconds;
        public bool playing;
        public float volume = 0.65f;
        public bool repeat = true;
        public bool muted;
        public int upgradeTier;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> unlockedDecorationSlots = new List<int> { 0 };
        public List<string> unlockedDecorationPrefabs = new List<string>();
        public List<BoomboxDecorationPlacement> decorationPlacements = new List<BoomboxDecorationPlacement>();
        public string platformGuid;
        public string platformPrefab;
        public long platformUserId;
        public uint platformObjectId;
        public float platformX;
        public float platformY;
        public float platformZ;
        public float platformQx;
        public float platformQy;
        public float platformQz;
        public float platformQw = 1f;
        public float platformScaleX = 1f;
        public float platformScaleY = 1f;
        public float platformScaleZ = 1f;
        [JsonIgnore]
        public Vector3 Position
        {
            get => new Vector3(x, y, z);
            set
            {
                x = value.x;
                y = value.y;
                z = value.z;
            }
        }
        [JsonIgnore]
        public string TrackId => tracks.Count == 0
            ? null
            : tracks[Mathf.Clamp(trackIndex, 0, tracks.Count - 1)];
    }

    public sealed class WorldState
    {
        public int version = 1;
        public long worldId;
        public List<DeckState> decks = new List<DeckState>();
        public List<string> migratedDecks = new List<string>();
    }

    internal sealed class StateStore
    {
        public readonly string Root;
        public WorldState World;
        public CassetteLibrary Library;
        public long PlayerId;
        private string savedWorld;
        private string savedLibrary;
        public string WorldPath => Path.Combine(Root, "world-" + World.worldId + ".json");
        public string LibraryPath => Path.Combine(Root, "cassettes-" + PlayerId + ".json");

        public StateStore(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
        }

        public void Load(long worldId, long playerId)
        {
            var loadedWorld = Read<WorldState>(Path.Combine(
                    Root,
                    "world-" + worldId + ".json"
                ))
                ?? new WorldState { worldId = worldId };
            if (loadedWorld.worldId != worldId)
                throw new InvalidDataException(BoomboxText.Get("text.the_registry_belongs_to_another_world"));
            if (loadedWorld.decks == null || loadedWorld.decks.GroupBy(d => d.id).Any(g => g.Count() > 1)
            || loadedWorld.decks.GroupBy(d => d.owner).Any(g => g.Count() > 1))
                throw new InvalidDataException(BoomboxText.Get("text.the_registry_contains_duplicate_instances"));
            var loadedLibrary = Read<CassetteLibrary>(Path.Combine(
                    Root,
                    "cassettes-" + playerId + ".json"
                ))
                ?? new CassetteLibrary();
            PlayerId = playerId;
            World = loadedWorld;
            Library = loadedLibrary;
            savedWorld = File.Exists(WorldPath) ? File.ReadAllText(WorldPath) : null;
            savedLibrary = File.Exists(LibraryPath) ? File.ReadAllText(LibraryPath) : null;
        }

        public DeckState OwnDeck()
        {
            return BoomboxPlugin.Instance?.Items?.Own?.State
                ?? World.decks.FirstOrDefault(d => d.owner == PlayerId
                && !World.migratedDecks.Contains(d.id));
        }

        public void Save()
        {
            if (World == null)
                return;
            string world = JsonConvert.SerializeObject(World, Formatting.Indented);
            string library = JsonConvert.SerializeObject(Library, Formatting.Indented);
            if (world != savedWorld)
            {
                AtomicWriteText(WorldPath, world);
                savedWorld = world;
            }
            if (library != savedLibrary)
            {
                AtomicWriteText(LibraryPath, library);
                savedLibrary = library;
            }
        }

        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path))
                return null;
            try
            {
                return ReadRequired<T>(path);
            }
            catch (JsonException)
            {
                if (!File.Exists(path + ".bak"))
                    throw;
                return ReadRequired<T>(path + ".bak");
            }
        }

        private static T ReadRequired<T>(string path) where T : class
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path))
            ?? throw new JsonSerializationException(BoomboxText.Format("error.saved_data_empty", path));
        }

        public static void AtomicWrite(string path, object value)
        {
            AtomicWriteText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        }

        private static void AtomicWriteText(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, value);
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);
        }
    }
}
