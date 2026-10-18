using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx.Configuration;
using VikingBoombox;

internal static class UpgradeChecks
{
    private static int checks;
    private static readonly string[] trophies = {
        "TrophyEikthyr",
        "TrophyTheElder",
        "TrophyBonemass",
        "TrophyDragonQueen",
        "TrophyGoblinKing",
        "TrophySeekerQueen",
        "TrophyFader"
    };
    private static readonly string[] cassetteMaterials = {
        "Bronze:1,FineWood:1",
        "Iron:2,LeatherScraps:1",
        "Silver:2,Crystal:1",
        "BlackMetal:2,LinenThread:1",
        "Eitr:1,BlackMarble:2",
        "FlametalNew:2,AskHide:1",
        "Gold:2,Frostwood:1"
    };
    private static readonly string[] deviceMaterials = {
        "Wood:10,Resin:6,LeatherScraps:2,BoneFragments:2,Coins:50",
        "TrophyEikthyr:1,Bronze:4,FineWood:4",
        "TrophyTheElder:1,Iron:6,LeatherScraps:4",
        "TrophyBonemass:1,Silver:6,Crystal:4",
        "TrophyDragonQueen:1,BlackMetal:6,LinenThread:4",
        "TrophyGoblinKing:1,Eitr:4,BlackMarble:6",
        "TrophySeekerQueen:1,FlametalNew:6,AskHide:2",
        "TrophyFader:1,Gold:6,Frostwood:4"
    };
    public static int Main(string[] args)
    {
        string dll = Path.GetFullPath(args[0]), root = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(root);
        string report = Path.Combine(root, "upgrade-result.json");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name == "ValheimBoombox")
                return context.LoadFromAssemblyPath(dll);
            foreach (string dir in new[] { args[1], args[2] })
            {
                string path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        var clock = Stopwatch.StartNew();
        try
        {
            Run(root);
            var result =
                new
                {
                    passed = true,
                    checks,
                    seconds = clock.Elapsed.TotalSeconds,
                    candidate = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))),
                    scope = "Real product DLL: exact recipes, ingredient/station failures, 35 cassette transitions and state preservation, config isolation. No game or Unity Editor process started. Native inventory consumption/UI are not claimed by these offline checks."
                };
            File.WriteAllText(
                report,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            );
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(
                report,
                JsonSerializer.Serialize(new
                {
                    passed = false,
                    checks,
                    seconds = clock.Elapsed.TotalSeconds,
                    error = error.ToString(),
                    candidate = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))
                })
            );
            Console.Error.WriteLine(error);
            return 1;
        }
    }
    private static void Check(bool condition, string name)
    {
        checks++;
        if (!condition)
            throw new InvalidOperationException(name);
    }
    private static Dictionary<string, int> Inventory(
        string text
    ) => text.Split(',').ToDictionary(x => x.Split(':')[0], x => int.Parse(x.Split(':')[1]));
    private static Dictionary<string, int> Cost(
        UpgradeRecipe recipe
    ) => recipe.Ingredients.ToDictionary(x => x.Prefab, x => x.Amount);
    private static void Recipe(UpgradeRecipe recipe, string station, int level, string materials)
    {
        Check(recipe.StationPrefab == station && recipe.StationLevel == level, "Station differs from specification");
        var expected = Inventory(materials);
        var actual = Cost(recipe);
        Check(
            actual.Count == expected.Count
                && expected.All(x => actual.TryGetValue(x.Key, out int amount)
                && amount == x.Value),
            "Exact recipe differs: " + materials
        );
        var missing = typeof(UpgradeRecipe).GetMethod("FindMissing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var at = typeof(UpgradeRecipe).GetMethod("CheckStation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Check(
            ((System.Collections.IList)missing.Invoke(recipe, new object[] { expected })!).Count == 0,
            "Exact resources must suffice"
        );
        foreach (var item in expected)
        {
            var shortInventory = new Dictionary<string, int>(expected);
            shortInventory[item.Key]--;
            var result = (System.Collections.IList)missing.Invoke(recipe, new object[] { shortInventory })!;
            Check(result.Count == 1, "Missing ingredient not detected: " + item.Key);
        }
        Check(at.Invoke(recipe, new object[] { station, level })!.ToString() == "Ready", "Correct station rejected");
        Check(
            at.Invoke(recipe, new object[] { station, level - 1 })!.ToString() == "StationLevelTooLow",
            "Insufficient station level accepted"
        );
        Check(
            at.Invoke(recipe, new object[] { "wrong_station", 99 })!.ToString() == "WrongStation",
            "Wrong station accepted"
        );
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string root)
    {
        string[] properties = { "Red", "Green", "Yellow", "Blue", "Purple" };
        Check(
            BoomboxItemData.Properties.OrderBy(x => x).SequenceEqual(properties.OrderBy(x => x)),
            "Property set changed"
        );
        Recipe(BoomboxCassetteWorkshop.BaseRecipe, "piece_workbench", 1, "Wood:2,Resin:1,BoneFragments:1");
        for (int rank = 1; rank <= 7; rank++)
        {
            var recipe = BoomboxCassetteProgression.DefaultRecipe(rank);
            Recipe(recipe, "piece_workbench", 1, trophies[rank - 1] + ":1," + cassetteMaterials[rank - 1]);
            if (rank > 1)
            {
                var wrong = Cost(recipe);
                wrong.Remove(trophies[rank - 1]);
                wrong["TrophyEikthyr"] = 10000;
                var missing = (System.Collections.IList)typeof(UpgradeRecipe).GetMethod(
                    "FindMissing",
                    BindingFlags.Instance | BindingFlags.NonPublic
                )!.Invoke(
                    recipe,
                    new object[] { wrong }
                )!;
                Check(missing.Count == 1, "Farming Eikthyr bypassed rank " + rank);
            }
            foreach (string property in properties)
            {
                var cassette = new RecordedCassette
                {
                    id = "same-cassette",
                    name = "kept",
                    prefab = BoomboxItemData.ArchivePrefab,
                    tracks = new List<string> { "track-a", "track-b" }
                };
                foreach (string other in properties)
                    cassette.properties[other] = other == property ? rank - 1 : 3;
                var original = cassette.properties.ToDictionary(x => x.Key, x => x.Value);
                var tracks = cassette.tracks;
                Check(BoomboxCassetteProgression.TryAdvance(cassette, property, 7), "Valid transition rejected");
                Check(cassette.Rank(property) == rank, "Selected property did not advance exactly once");
                Check(
                    properties.Where(x => x != property).All(x => cassette.Rank(x) == original[x]),
                    "Other properties changed"
                );
                Check(
                    cassette.id == "same-cassette"
                        && cassette.name == "kept"
                        && ReferenceEquals(cassette.tracks, tracks)
                        && tracks.SequenceEqual(new[] { "track-a", "track-b" }),
                    "Identity/music changed"
                );
            }
        }
        foreach (int oldRank in new[] { 7, 8, 99 })
        {
            var cassette = new RecordedCassette();
            cassette.properties["Red"] = oldRank;
            Check(
                !BoomboxCassetteProgression.TryAdvance(cassette, "Red", 99) && cassette.Rank("Red") == oldRank,
                "Cap or grandfathered rank changed"
            );
        }
        var capped = new RecordedCassette();
        capped.properties["Red"] = 3;
        Check(!BoomboxCassetteProgression.TryAdvance(capped, "Red", 3), "Configured lower cap ignored");
        Check(
            !BoomboxCassetteProgression.TryAdvance(capped, "invalid", 7) && !capped.properties.ContainsKey("invalid"),
            "Unknown property accepted"
        );
        Check(!BoomboxCassetteProgression.TryAdvance(null, "Red", 7), "Null cassette accepted");
        capped.properties["Red"] = -1;
        Check(!BoomboxCassetteProgression.TryAdvance(capped, "Red", 7), "Negative rank accepted");
        foreach (int rank in new[] { 0, 8, -1 })
        {
            bool rejected = false;
            try
            {
                BoomboxCassetteProgression.DefaultRecipe(rank);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            Check(rejected, "Undefined recipe accepted");
        }
        string[] stations = {
            "piece_workbench",
            "forge",
            "forge",
            "forge",
            "forge",
            "blackforge",
            "blackforge",
            "blackforge"
        };
        int[] levels = { 1, 1, 2, 3, 4, 1, 3, 4 };
        for (int tier = 1; tier <= 8; tier++)
        {
            var recipe = BoomboxProgression.GetTier(tier).Recipe;
            Recipe(recipe, stations[tier - 1], levels[tier - 1], deviceMaterials[tier - 1]);
            var plan = BoomboxProgression.PlanNextUpgrade(
                tier - 1,
                stations[tier - 1],
                levels[tier - 1],
                Inventory(deviceMaterials[tier - 1])
            );
            Check(plan.Allowed && plan.TargetTier.Level == tier, "Device next-level plan wrong");
        }
        Check(
            !BoomboxProgression.PlanNextUpgrade(8, "blackforge", 99, new Dictionary<string, int>()).Allowed,
            "Device exceeded VIII"
        );
        Configuration(root);
    }
    private static void Configuration(string root)
    {
        string folder = Path.Combine(root, "upgrade-config");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "legacy.cfg");
        File.WriteAllText(
            path,
            "[Recipe Cassette upgrade]\nStation = piece_workbench\nStationLevel = 1\nMaterials = TrophyEikthyr:1,Wood:4,Resin:3,BoneFragments:1\n"
        );
        string legacyText = File.ReadAllText(path);
        var config = new ConfigFile(path, false);
        config.SaveOnConfigSet = false;
        var settings = new BoomboxSettings(config);
        for (int rank = 1; rank <= 7; rank++)
            Recipe(
                BoomboxCassetteProgression.Recipe(settings, rank),
                "piece_workbench",
                1,
                trophies[rank - 1] + ":1," + cassetteMaterials[rank - 1]
            );
        Check(File.ReadAllText(path) == legacyText, "Reading old config changed its file");
        string fresh = Path.Combine(folder, "fresh.cfg");
        File.WriteAllText(fresh, "");
        var freshConfig = new ConfigFile(fresh, false);
        freshConfig.SaveOnConfigSet = false;
        var freshSettings = new BoomboxSettings(freshConfig);
        foreach (var key in freshConfig.Keys.Where(
            k => !k.Section.StartsWith("Recipe Cassette rank ", StringComparison.Ordinal)
        )
            .ToArray())
            freshConfig.Remove(key);
        freshConfig.Save();
        var reloadedConfig = new ConfigFile(fresh, false);
        reloadedConfig.SaveOnConfigSet = false;
        var reloadedSettings = new BoomboxSettings(reloadedConfig);
        for (int rank = 1; rank <= 7; rank++)
            Recipe(
                BoomboxCassetteProgression.Recipe(reloadedSettings, rank),
                "piece_workbench",
                1,
                trophies[rank - 1] + ":1," + cassetteMaterials[rank - 1]
            );
        string custom = Path.Combine(folder, "custom.cfg");
        File.WriteAllText(
            custom,
            "[Recipe Cassette rank 2]\nStation = forge\nStationLevel = 2\nMaterials = TrophyTheElder:2,Iron:3,LeatherScraps:1\n"
        );
        var other = new ConfigFile(custom, false);
        other.SaveOnConfigSet = false;
        var customSettings = new BoomboxSettings(other);
        Recipe(
            BoomboxCassetteProgression.Recipe(customSettings, 2),
            "forge",
            2,
            "TrophyTheElder:2,Iron:3,LeatherScraps:1"
        );
        Recipe(
            BoomboxCassetteProgression.Recipe(customSettings, 3),
            "piece_workbench",
            1,
            "TrophyBonemass:1,Silver:2,Crystal:1"
        );
    }
}
