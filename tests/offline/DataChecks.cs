using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Reflection;
using System.Threading;
using VikingBoombox;

public static class DataChecks
{
    private static int checks;
    public static int Main(string[] args)
    {
        string game = BoomboxQa.QaEnvironment.ClientRoot;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string dir in new[] { BoomboxQa.QaEnvironment.Managed, BoomboxQa.QaEnvironment.Core })
            {
                string file = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(file))
                    return context.LoadFromAssemblyPath(file);
            }
            return null;
        };
        Run(args[0]);
        Console.WriteLine("PASS: " + checks + " assertions against actual product DLL; no game started");
        return 0;
    }
    private static void Check(bool value, string name)
    {
        checks++;
        if (!value)
            throw new Exception(name);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string root)
    {
        string folder = Path.Combine(
            BoomboxQa.QaEnvironment.QaRoot,
            "current/offline/data-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        var library = UserMusicLibrary.Load(folder);
        Check(library.AddFolder(folder), "folder added");
        Check(!library.AddFolder(folder), "folder deduplicated");
        var list = library.Create("Мой список", new[] { "b", "a", "b" });
        library.Update(list, "Новое имя", new[] { "a", "b", "b" });
        library.Import("old", "Старый", new[] { "old-track" });
        library.Import("old", "Старый", new[] { "old-track" });
        library = UserMusicLibrary.Load(folder);
        Check(library.playlists.Count == 2, "migration once");
        Check(library.playlists[0].name == "Новое имя", "rename persists");
        Check(library.playlists[0].tracks.SequenceEqual(new[] { "a", "b", "b" }), "order and repeats persist");
        library.Delete(library.playlists[1]);
        library = UserMusicLibrary.Load(folder);
        library.Import("old", "Старый", new[] { "old-track" });
        Check(library.playlists.Count == 1, "deleted migration stays deleted");
        library.RemoveFolder(folder);
        Check(UserMusicLibrary.Load(folder).folders.Count == 0, "folder removal persists");
        var slots = BoomboxTrophyStorage.SlotData();
        Check(
            slots.Length == 24 && slots.Count(s => s.Kind == BoomboxDecorationKind.Hanging) == 17
            && slots.Count(s => s.Kind == BoomboxDecorationKind.Figurine) == 7,
            "17 hanging and 7 figurine slots"
        );
        Check(
            slots.Select(s => s.Index).SequenceEqual(Enumerable.Range(0, 24)),
            "stable unique decoration slot indices"
        );
        var catalogLists = typeof(BoomboxTrophyStorage).Assembly.GetType("VikingBoombox.BoomboxDecorationCatalog");
        var standLists = (System.Collections.Generic.HashSet<string>)catalogLists.GetField(
            "standingHeads",
            BindingFlags.Static | BindingFlags.NonPublic
        ).GetValue(null);
        var hangingLists = (System.Collections.Generic.HashSet<string>)catalogLists.GetField(
            "hangingOnly",
            BindingFlags.Static | BindingFlags.NonPublic
        ).GetValue(null);
        var pieceLists = (System.Collections.Generic.HashSet<string>)catalogLists.GetField(
            "hangingPieces",
            BindingFlags.Static | BindingFlags.NonPublic
        ).GetValue(null);
        Check(
            standLists.Count == 34
                && hangingLists.Count == 53
                && pieceLists.Count == 18
                && !standLists.Contains("TrophyGoblin")
                && hangingLists.Contains("TrophyGoblin")
                && !standLists.Contains("TrophyTick")
                && !hangingLists.Contains("TrophyTick")
                && !pieceLists.Contains("piece_sapcollector"),
            "105 pendant kinds without the tick and the sap collector: 34 stand heads, 53 more items with the goblin, 18 pieces and boats"
        );
        Check(
            new[] {
            "TrophyTheElder",
            "TrophyGhost",
            "TrophyWraith",
            "TrophySkeletonPoison",
            "TrophySGolem",
            "Bronze",
            "MoltenCore",
            "Resin",
            "DragonEgg"
            }.All(hangingLists.Contains)
            && !new[] { "Wood", "Carrot", "MoltenCoreOre" }.Any(hangingLists.Contains)
            && new[] { "smelter", "piece_Lavalantern", "piece_brazierceiling01", "Raft" }.All(pieceLists.Contains)
            && !new[] { "Trailership", "piece_cookingstation", "piece_cookingstation_iron" }.Any(pieceLists.Contains),
            "owner catalog groups of 18 and 19 September"
        );
        using (var fitStream = typeof(BoomboxTrophyStorage).Assembly.GetManifestResourceStream(
            "ValheimBoombox.PendantFit.json"
        ))
        {
            var fitText = new StreamReader(fitStream).ReadToEnd();
            var mini = System.Text.RegularExpressions.Regex.Match(
                fitText,
                "\"ValheimBoombox_Item\"[^}]*}[^}]*\"hangPoint\":\\s*\\[(-?[0-9.]+),"
            );
            Check(
                mini.Success
                    && float.Parse(mini.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < -.38f,
                "mini boombox pendant hangs by its top right corner"
            );
            var growth = System.Text.RegularExpressions.Regex.Match(fitText, "\"TrophyGrowth\":\\s*\\{[^}]*}\\s*}");
            Check(growth.Success && !growth.Value.Contains("rotation"), "growth pendant keeps its spikes sideways");
            Check(
                new[] { "Blueberries", "DragonEgg" }.All(
                    n => System.Text.RegularExpressions.Regex.IsMatch(
                        fitText,
                        "\"" + n + "\":\\s*\\{\\s*\"rotation\":\\s*\\[-90, 0, 0\\]"
                    )
                )
                && System.Text.RegularExpressions.Regex.IsMatch(
                    fitText,
                    "\"GreydwarfEye\":\\s*\\{\\s*\"rotation\":\\s*\\[90, 0, 0\\]"
                )
                && !fitText.Contains("piece_sapcollector"),
                "ingots hang long side down, blueberries by the stems, greydwarf eye and dragon egg by the crown"
            );
            Check(
                new[] {
                "Bronze",
                "Copper",
                "Tin",
                "Iron",
                "Silver",
                "BlackMetal",
                "Gold",
                "Flametal",
                "FlametalNew"
                }.All(
                    n => System.Text.RegularExpressions.Regex.IsMatch(
                        fitText,
                        "\"" + n + "\":\\s*\\{\\s*\"rotation\":\\s*\\[-90, 180, 0\\]"
                    )
                ),
                "ingots show their beveled top to the viewer"
            );
            Check(
                new[] { "Raft", "Karve", "VikingShip", "VikingShip_Ashlands" }.All(
                    n => System.Text.RegularExpressions.Regex.IsMatch(
                        fitText,
                        "\"" + n + "\":\\s*\\{[^}]*\"turn\":\\s*-40,"
                    )
                ),
                "boats turn their bow 40 degrees toward the viewer"
            );
            Check(
                System.Text.RegularExpressions.Regex.IsMatch(
                    fitText,
                    "\"piece_birdnest\":\\s*\\{\\s*\"rotation\":\\s*\\[20, 0, 0\\]"
                ),
                "bird nest knot above its middle"
            );
        }
        var decorated = new DeckState();
        Check(decorated.unlockedDecorationSlots.SequenceEqual(new[] { 0 }), "first decoration slot free per device");
        Check(
            BoomboxTrophyStorage.SlotPrice(0, 50, 0, 1) == 0 && BoomboxTrophyStorage.SlotPrice(0, 50, 0, 2) == 50
            && BoomboxTrophyStorage.SlotPrice(0, 50, 0, 3) == 100
            && BoomboxTrophyStorage.SlotPrice(0, 50, 0, 4) == 150,
            "DEC-44 growing slot price by open count, default settings"
        );
        Check(
            BoomboxTrophyStorage.SlotPrice(0, 50, 120, 5) == 120 && BoomboxTrophyStorage.SlotPrice(0, 50, 0, 5) == 200,
            "DEC-44 cap limits price, 0 means no cap"
        );
        decorated.unlockedDecorationPrefabs.Add("TrophyDeer");
        for (int i = 17; i < 24; i++)
            decorated.decorationPlacements.Add(new BoomboxDecorationPlacement { slot = i, prefab = "TrophyDeer" });
        Check(
            decorated.decorationPlacements.Count == 7
                && decorated.decorationPlacements.All(p => p.prefab == "TrophyDeer"),
            "one unlocked design supports seven repeated figurines"
        );
        var trophyStorage = typeof(BoomboxTrophyStorage);
        Check(
            new[] { "UnlockSlot", "UnlockDesign", "Install", "Clear", "SlotData", "Catalog" }.All(
                name => trophyStorage.GetMethod(name, BindingFlags.Static | BindingFlags.Public) != null
            ),
            "decoration runtime probe entrypoints"
        );
        var tier = BoomboxProgression.GetTier(1);
        Check(tier.BluntDamage == 1 && tier.Recipe.StationPrefab == "piece_workbench", "base damage and bench");
        Check(
            tier.Recipe.Ingredients.Select(i => i.Prefab).Order().SequenceEqual(
                new[] { "BoneFragments", "Coins", "LeatherScraps", "Resin", "Wood" }
            ),
            "early materials"
        );
        Check(
            tier.Recipe.Ingredients.Single(i => i.Prefab == "Coins").Amount == 50,
            "approved 50 coins in base recipe"
        );
        string[] bosses = {
            "TrophyEikthyr",
            "TrophyTheElder",
            "TrophyBonemass",
            "TrophyDragonQueen",
            "TrophyGoblinKing",
            "TrophySeekerQueen",
            "TrophyFader"
        };
        for (int level = 2; level <= 8; level++)
            Check(
                BoomboxProgression.GetTier(level).Recipe.Ingredients.Any(
                    i => i.Prefab == bosses[level - 2] && i.Amount == 1
                ),
                "boss recipe " + level
            );
        string cueFolder = Path.Combine(folder, "album");
        Directory.CreateDirectory(cueFolder);
        File.WriteAllBytes(Path.Combine(cueFolder, "disc.flac"), new byte[0]);
        File.WriteAllText(
            Path.Combine(cueFolder, "disc.cue"),
            "PERFORMER \"Artist\"\nTITLE \"Album\"\nREM DISCNUMBER 2\nFILE \"disc.flac\" WAVE\nTRACK 01 AUDIO\nTITLE \"Zulu\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"Alpha\"\nINDEX 01 01:00:00\n"
        );
        var scanned = MusicScanner.Scan(new[] { cueFolder }, null, "ffprobe", CancellationToken.None);
        var album = scanned.playlists.Single().tracks;
        Check(
            album.Select(t => t.title).SequenceEqual(new[] { "Zulu", "Alpha" }),
            "CUE track order overrides title sorting"
        );
        Check(
            album.All(t => t.album == "Album"
                && t.albumArtist == "Artist"
                && t.discNumber == 2)
                && album[1].trackNumber == 2,
            "CUE album metadata"
        );
        album[1].id = "existing-cassette-id";
        var rescanned = MusicScanner.Scan(new[] { cueFolder }, scanned, "ffprobe", CancellationToken.None);
        Check(
            rescanned.playlists.Single().tracks[1].id == "existing-cassette-id",
            "rescan preserves cassette track identity"
        );
        string realAlbum = Environment.GetEnvironmentVariable("VALHEIM_QA_REFERENCE_ALBUM") ?? "";
        if (realAlbum.Length > 0 && (!Path.IsPathRooted(realAlbum) || !Directory.Exists(realAlbum)))
            throw new IOException("VALHEIM_QA_REFERENCE_ALBUM must be an absolute existing directory");
        if (Directory.Exists(realAlbum))
        {
            var real = MusicScanner.Scan(new[] { realAlbum }, null, "ffprobe", CancellationToken.None);
            Check(real.errors.Count == 0, "real album has no scan errors");
            Check(
                real.playlists.Single().tracks.Select(t => t.title).SequenceEqual(new[] {
                    "War Pigs",
                    "Paranoid",
                    "Planet Caravan",
                    "Iron Man",
                    "Electric Funeral",
                    "Hand Of Doom",
                    "Rat Salad",
                    "Fairies Wear Boots"
                    }),
                "real album eight CUE tracks in order"
            );
        }
        else
            Console.WriteLine("SKIP optional real-album checks: VALHEIM_QA_REFERENCE_ALBUM is not configured");
        var storeType = typeof(MusicTrack).Assembly.GetType("VikingBoombox.StateStore");
        var store = Activator.CreateInstance(storeType, new object[] { Path.Combine(folder, "state") });
        storeType.GetMethod("Load").Invoke(store, new object[] { 123L, 456L });
        storeType.GetMethod("Save").Invoke(store, null);
        string statePath = (string)storeType.GetProperty("WorldPath").GetValue(store);
        var previousTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(statePath, previousTime);
        storeType.GetMethod("Save").Invoke(store, null);
        Check(
            File.GetLastWriteTimeUtc(statePath) == previousTime && !File.Exists(statePath + ".bak"),
            "unchanged state does not rewrite disk"
        );
        ((WorldState)storeType.GetField("World").GetValue(store)).migratedDecks.Add("changed");
        storeType.GetMethod("Save").Invoke(store, null);
        Check(
            File.ReadAllText(statePath).Contains("changed") && File.Exists(statePath + ".bak"),
            "changed state persists with backup"
        );
        string cache = Path.Combine(folder, "cache");
        string held = Path.Combine(cache, "native-v1", new string('1', 64)),
            unused = Path.Combine(cache, "native-v1", new string('2', 64));
        Directory.CreateDirectory(held);
        Directory.CreateDirectory(unused);
        File.WriteAllBytes(Path.Combine(held, "audio.wav"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(unused, "audio.wav"), new byte[2048]);
        Check(
            MusicCache.Clean(cache) == 3072 && !Directory.Exists(Path.Combine(cache, "native-v1")),
            "cleanup deletes the whole cache left by earlier versions"
        );
        BoomboxText.LoadTranslations("English");
        Check(
            BoomboxText.Get("text.error") + BoomboxText.Get("text.file_missing_or_inaccessible") + "D:/Музыка/трек.flac"
                == "Error: File missing or inaccessible: D:/Музыка/трек.flac",
            "English error translation preserves Unicode path"
        );
        BoomboxText.LoadTranslations("Russian", Path.Combine(root, "Translations"));
        Check(BoomboxText.ConfigName("BufferSeconds") == "Буфер звука в секундах", "Russian name for new setting");
        Check(
            BoomboxText.Get("text.the_audio_file_contains_no_cuesheet") == "В аудиофайле нет CUESHEET",
            "Russian translation of an English error message"
        );
        BoomboxText.LoadTranslations("French", folder);
        Check(BoomboxText.Get("text.music") == "Music", "missing language falls back to English");
        File.WriteAllText(Path.Combine(folder, "French.json"), "{\"text.music\":\"Musique\"}");
        BoomboxText.LoadTranslations("French", folder);
        Check(
            BoomboxText.Get("text.music") == "Musique" && BoomboxText.Get("text.pause") == "Pause",
            "external partial translation with English fallback"
        );
        Check(
            BoomboxSyntheticTrackId.All.SequenceEqual(new[] {
                "system:computer-audio",
                "system:gamemusic",
                "system:rhythm:80",
                "system:rhythm:100",
                "system:rhythm:120",
                "system:rhythm:140"
                }),
            "reserved synthetic ids and library order including computer audio"
        );
        Check(
            BoomboxSyntheticTrackId.IsSynthetic("system:computer-audio")
                && BoomboxSyntheticTrackId.IsSynthetic("system:rhythm:80")
                && BoomboxSyntheticTrackId.IsSynthetic("system:gamemusic")
                && !BoomboxSyntheticTrackId.IsSynthetic("real-track-id")
                && !BoomboxSyntheticTrackId.IsSynthetic(null),
            "synthetic id detection"
        );
        Check(
            BoomboxSyntheticTrackId.IsGameMusic("system:gamemusic")
                && !BoomboxSyntheticTrackId.IsGameMusic("system:rhythm:80"),
            "game music id detection"
        );
        Check(
            BoomboxSyntheticTrackId.TryRhythmBpm("system:rhythm:80", out int bpm80) && bpm80 == 80
            && !BoomboxSyntheticTrackId.TryRhythmBpm("system:rhythm:81", out _)
            && !BoomboxSyntheticTrackId.TryRhythmBpm("system:rhythm:abc", out _),
            "only the four approved rhythm speeds parse"
        );
        foreach (var pair in new (int bpm, double ms)[] { (80, 750.0), (100, 600.0), (120, 500.0), (140, 60000.0 / 140.0) })
            Check(
                Math.Abs(BoomboxSyntheticTrackId.IntervalSeconds(pair.bpm) * 1000.0 - pair.ms) < 0.01,
                "rhythm interval milliseconds for " + pair.bpm + " bpm"
            );
        BoomboxText.LoadTranslations("English");
        Check(
            BoomboxSyntheticTrackId.For("system:rhythm:80").title == "Rhythm 80"
            && BoomboxSyntheticTrackId.For("system:rhythm:140").title == "Rhythm 140",
            "English rhythm labels"
        );
        Check(BoomboxSyntheticTrackId.For("system:gamemusic").title == "Game music", "English game music label");
        BoomboxText.LoadTranslations("Russian");
        Check(BoomboxSyntheticTrackId.For("system:rhythm:100").title == "Ритм 100", "Russian rhythm label");
        Check(BoomboxSyntheticTrackId.For("system:gamemusic").title == "Музыка игры", "Russian game music label");
        Check(BoomboxSyntheticTrackId.For("not-a-real-id") == null, "unknown id does not resolve to a synthetic track");
        string legacyFile = Path.Combine(BoomboxQa.QaEnvironment.FixturesRoot, "legacy-catalog.json");
        if (File.Exists(legacyFile))
        {
            var legacy = System.Text.Json.JsonSerializer.Deserialize<MusicCatalog>(
                File.ReadAllText(legacyFile),
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true }
            );
            var imported = UserMusicLibrary.Load(Path.Combine(folder, "legacy"));
            imported.ImportCatalog(legacy);
            imported.ImportCatalog(legacy);
            imported = UserMusicLibrary.Load(Path.Combine(folder, "legacy"));
            Check(imported.playlists.Count == legacy.playlists.Count, "real legacy catalog imported once");
            for (int index = 0; index < legacy.playlists.Count; index++)
                Check(
                    imported.playlists[index].name == legacy.playlists[index].name
                    && imported.playlists[index].tracks.SequenceEqual(legacy.playlists[index].tracks.Select(t => t.id)),
                    "real legacy playlist name/order/repeats " + index
                );
        }
        File.WriteAllText(Path.Combine(folder, "result.txt"), "passed " + checks);
    }
}
