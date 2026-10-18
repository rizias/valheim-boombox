using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

internal static class LocalizationChecks
{
    private static readonly List<string> passed = new List<string>();
    private static readonly List<string> failures = new List<string>();
    private static Type text;
    private static string root;

    public static int Main(string[] args)
    {
        string dll = Path.GetFullPath(args[0]);
        root = Path.Combine(Path.GetFullPath(args[3]), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { args[1], args[2] })
            {
                string path = Path.GetFullPath(Path.Combine(directory, name.Name + ".dll"));
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        var product = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        text = product.GetType("VikingBoombox.BoomboxText", true);
        Run("Embedded fallback before initialization", EmbeddedFallback);
        Run("English and Russian resources", BuiltInLanguages);
        Run("Extra language and partial fallback", ExtraLanguage);
        Run("Selected language takes priority across directories", DirectoryPriority);
        Run("Missing and invalid translation files", InvalidFiles);
        Run("Switching languages clears overrides", SwitchLanguages);
        Run("Invalid language filenames stay inside lookup directory", InvalidLanguageNames);
        Run("Config display labels follow language changes", () => ConfigDisplayNames(product));
        Run("Stored messages follow language changes", () => StoredMessages(product));
        Run("Configuration files contain translated descriptions", ConfigDescriptions);
        Run("Generated party settings keep identifiers and translated descriptions", () => PartyDescriptions(product));
        Run("Invalid external formatting keeps the UI usable", InvalidFormats);
        var result = new
        {
            passed = failures.Count == 0,
            checks = passed,
            failures,
            fixtureDirectory = root,
            dllSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll)))
        };
        string json = JsonSerializer.Serialize(result);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(root), "localization-result.json"), json);
        Console.WriteLine(json);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Run(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            failures.Add(name + ": " + error.GetBaseException().Message);
        }
    }

    private static void Check(bool value, string name)
    {
        if (!value)
            throw new InvalidOperationException(name);
        passed.Add(name);
    }

    private static string Get(string key) => (string)text.GetMethod("Get").Invoke(null, new object[] { key });
    private static string ConfigName(string name) => (string)text.GetMethod("ConfigName", new[] { typeof(string) })
        .Invoke(null, new object[] { name });
    private static string Format(string key, params object[] values) => (string)text.GetMethod("Format")
        .Invoke(null, new object[] { key, values });
    private static string Language => (string)text.GetProperty("Language").GetValue(null);
    private static CultureInfo Culture => (CultureInfo)text.GetProperty("Culture").GetValue(null);
    private static void Load(string language, params string[] directories) => text.GetMethod("LoadTranslations")
        .Invoke(null, new object[] { language, directories });

    private static string Folder(string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string directory, string language, Dictionary<string, string> values) =>
        File.WriteAllText(Path.Combine(directory, language + ".json"), JsonSerializer.Serialize(values));

    private static void ConfigDescriptions()
    {
        Load("Russian");
        object description = text.GetMethod("Description").Invoke(null, new object[] { "text.add_folder", null, Array.Empty<object>() });
        var type = description.GetType();
        Check(
            (string)type.GetProperty("Description").GetValue(description) == "Добавить папку",
            "A config description is translated before its first save"
        );
        var configType = type.Assembly.GetType("BepInEx.Configuration.ConfigFile", true);
        string file = Path.Combine(Folder("config-description"), "fixture.cfg");
        object config = Activator.CreateInstance(configType, new object[] { file, false, null });
        var bind = configType.GetMethods().Single(m => m.Name == "Bind" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4
&& m.GetParameters()[0].ParameterType == typeof(string)
&& m.GetParameters()[3].ParameterType == type);
        bind.MakeGenericMethod(typeof(bool)).Invoke(config, new object[] { "Fixture", "Enabled", true, description });
        object withoutDescription = bind.MakeGenericMethod(typeof(bool)).Invoke(config, new object[] { "Fixture", "NoDescription", true, null });
        object emptyDescription = withoutDescription.GetType().GetProperty("Description").GetValue(withoutDescription);
        Check(
            emptyDescription != null && type.GetProperty("Tags").GetValue(emptyDescription) != null,
            "Real BepInEx normalizes an omitted description and its tags instead of leaving null"
        );
        configType.GetMethod("Save").Invoke(config, Array.Empty<object>());
        string saved = File.ReadAllText(file);
        Check(
            saved.Contains("Добавить папку") && !saved.Contains("text.add_folder"),
            "The real BepInEx configuration file writes readable comments rather than localization keys"
        );
        Load("English");
        object english = text.GetMethod("Description").Invoke(null, new object[] { "text.add_folder", null, Array.Empty<object>() });
        Check(
            (string)type.GetProperty("Description").GetValue(english) == "Add folder",
            "New descriptions use the selected JSON language"
        );
    }

    private static void PartyDescriptions(Assembly product)
    {
        var partyType = product.GetType("VikingBoombox.BoomboxPartySettings", true);
        var configType = text.GetMethod("Description").ReturnType.Assembly
            .GetType("BepInEx.Configuration.ConfigFile", true);
        var constructor = partyType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { configType },
            null
        );
        foreach (string language in new[] { "English", "Russian" })
        {
            Load(language);
            string file = Path.Combine(Folder("party-" + language), "fixture.cfg");
            object config = Activator.CreateInstance(configType, new object[] { file, false, null });
            object party = constructor.Invoke(new[] { config });
            var entries = (Array)partyType.GetField("Strength").GetValue(party);
            string[] names = { "Damage", "Health", "Stamina", "Eitr", "Speed" };
            for (int index = 0; index < names.Length; index++)
            {
                object entry = entries.GetValue(index);
                object definition = entry.GetType().GetProperty("Definition").GetValue(entry);
                Check(
                    (string)definition.GetType().GetProperty("Section").GetValue(definition) == "Party"
                    && (string)definition.GetType().GetProperty("Key").GetValue(definition) == names[index] + "Multiplier",
                    "Party setting keeps its saved identifier: " + names[index] + " / " + language
                );
                object description = entry.GetType().GetProperty("Description").GetValue(entry);
                string shown = (string)description.GetType().GetProperty("Description").GetValue(description);
                Check(
                    shown == Get("party." + names[index] + "Multiplier")
                    && shown.Contains(language == "Russian" ? "хозяином" : "Host"),
                    "Generated party description uses the selected language: " + names[index] + " / " + language
                );
            }
        }
    }

    private static void InvalidFormats()
    {
        string folder = Folder("invalid-format");
        Write(folder, "FixtureBroken", new Dictionary<string, string>
        {
            ["meta.culture"] = "de-DE",
            ["notice.library_summary"] = "invalid {",
            ["config.prefix.Recipe "] = "invalid {9}",
            ["text.fixture_bad_format"] = "unclosed {"
        });
        Load("FixtureBroken", folder);
        Check(
            Format("notice.library_summary", 7, 2) == "Library tracks: 7. Warnings: 2",
            "Invalid syntax uses the embedded fallback with the original values"
        );
        Check(
            Format("notice.library_summary", 8, 3) == "Library tracks: 8. Warnings: 3",
            "Rejected format caching preserves fresh values on the next display"
        );
        Check(
            ConfigName("Recipe Cassette rank 7") == "Recipe Cassette rank 7",
            "Invalid config prefix slots use the valid fallback"
        );
        Check(
            Format("text.fixture_bad_format") == "unclosed {",
            "A malformed format without a fallback is displayed literally rather than throwing"
        );
        Check(Format("text.add_folder", (object[])null) == "Add folder", "An explicit null argument array is treated as empty");
        Write(folder, "FixtureBroken", new Dictionary<string, string> { ["notice.library_summary"] = "fixed {1}/{0}" });
        Load("FixtureBroken", folder);
        Check(
            Format("notice.library_summary", 7, 2) == "fixed 2/7",
            "Reloading a corrected dictionary removes the rejected-format cache"
        );
        Write(folder, "FixtureCounts", new Dictionary<string, string>
        {
            ["meta.culture"] = "de-DE",
            ["text.add_folder"] = "custom {1}",
            ["tooltip.boombox_level"] = "{{{0,-4:D}}}/{1}"
        });
        Load("FixtureCounts", folder);
        Check(
            Format("text.add_folder", "only") == "Add folder"
                && Format("text.add_folder", "first", "second") == "custom second",
            "A missing argument fallback cannot replace a valid call with a different argument count"
        );
        Check(
            Format("tooltip.boombox_level", 12.5m, 8) == "Boombox level: 12,5/8"
                && Format("tooltip.boombox_level", 3, 8) == "{3   }/8",
            "An unsupported value format cannot poison a later valid type; alignment and escaped braces remain supported"
        );
    }

    private static void StoredMessages(Assembly product)
    {
        var type = product.GetType("VikingBoombox.BoomboxMessage", true);
        object[] arguments = { 7, 2 };
        object message = Activator.CreateInstance(type, new object[] { "notice.library_summary", arguments });
        arguments[0] = 99;
        Load("Russian");
        Check(
            message.ToString() == "Треков в библиотеке: 7. Предупреждений: 2",
            "Stored message keeps values and resolves Russian at display time"
        );
        Load("English");
        Check(
            message.ToString() == "Library tracks: 7. Warnings: 2",
            "The same stored message changes to English without rescanning"
        );
        Load("Russian");
        Check(
            message.ToString() == "Треков в библиотеке: 7. Предупреждений: 2",
            "The same stored message changes back without parsing translated prose"
        );
        object raw = type.GetMethod("Raw").Invoke(null, new object[] { "D:/Музыка/track.flac" });
        Load("English");
        Check(raw.ToString() == "D:/Музыка/track.flac", "User-supplied Unicode data remains literal");
        object nullArguments = Activator.CreateInstance(type, new object[] { "text.add_folder", null });
        Check(nullArguments.ToString() == "Add folder", "Stored message accepts an explicit null params array");
    }

    private static void EmbeddedFallback()
    {
        Check(
            Get("text.add_folder") == "Add folder" && Language == "English",
            "Get lazily loads embedded English without external files"
        );
        Check(
            Get("boombox_name") == "Boombox" && Get("visibility.AllVisuals") == "All world visuals",
            "Item and visibility namespaces resolve from embedded JSON"
        );
        Check(
            Get("text.no_such_translation_fixture") == "text.no_such_translation_fixture",
            "Missing translation returns its visible key"
        );
        Check(
            ((IEnumerable<string>)text.GetProperty("Keys").GetValue(null)).Contains("text.add_folder"),
            "Keys exposes loaded stable keys"
        );
        Load("English", Path.Combine(root, "translations-do-not-exist"));
        Check(
            Get("text.add_folder") == "Add folder" && Culture.Name == "en-US",
            "Missing external directory preserves embedded text and culture"
        );
    }

    private static void BuiltInLanguages()
    {
        Load("Russian");
        Check(
            Get("text.add_folder") == "Добавить папку" && Get("boombox_name") == "Магнитофон",
            "Russian text and item names load from resources"
        );
        Check(
            Get("visibility.AllVisuals") == "Все эффекты в мире" && Culture.Name == "ru-RU",
            "Russian visibility label and culture load from JSON"
        );
        Check(ConfigName("Music") == "Музыка", "Config section name uses stable config key");
        Check(
            ConfigName("Recipe Cassette rank 7") == "Рецепт Кассета ранга 7",
            "Nested config prefixes translate labels and preserve rank"
        );
        Check(Format("tooltip.boombox_level", 3, 8) == "Уровень магнитофона: 3/8", "Dynamic multi-argument text preserves values");
        Load("English");
        Check(
            Get("text.add_folder") == "Add folder" && ConfigName("Recipe Cassette rank 7") == "Recipe Cassette rank 7",
            "Switching back to English restores embedded labels"
        );
    }

    private static void ExtraLanguage()
    {
        string directory = Folder("additional-language");
        Write(directory, "FixtureOrchid", new Dictionary<string, string>
        {
            ["meta.culture"] = "de-DE",
            ["text.add_folder"] = "Ordner wählen",
            ["text.fixture_format"] = "{1}: {0:0.00}",
            ["config.Music"] = "Klang",
            ["config.prefix.Fixture "] = "Probe [{0}]"
        });
        Load("FixtureOrchid", directory);
        Check(
            Language == "FixtureOrchid" && Get("text.add_folder") == "Ordner wählen",
            "An arbitrary language filename works without a built-in language branch"
        );
        Check(
            Get("boombox_name") == "Boombox" && Get("visibility.AllVisuals") == "All world visuals",
            "A partial extra language falls back to English per missing key"
        );
        Check(
            Culture.Name == "de-DE" && Format("text.fixture_format", 12.5m, "value") == "value: 12,50",
            "JSON placeholders reorder arguments and use the selected numeric culture"
        );
        Check(
            ConfigName("Fixture Music") == "Probe [Klang]" && ConfigName("Fixture 12") == "Probe [12]",
            "A JSON-defined config prefix translates its suffix or preserves a number"
        );
        Check(ConfigName("UnregisteredFixture") == "config.UnregisteredFixture", "Missing config name returns its visible key");
    }

    private static void DirectoryPriority()
    {
        string first = Folder("priority-first");
        string second = Folder("priority-second");
        Write(
            first,
            "English",
            new Dictionary<string, string> { ["text.add_folder"] = "fallback first", ["text.fixture_fallback"] = "fallback first" }
        );
        Write(
            first,
            "FixtureCedar",
            new Dictionary<string, string> { ["text.add_folder"] = "selected first", ["text.fixture_selected"] = "selected first" }
        );
        Write(
            second,
            "English",
            new Dictionary<string, string> { ["text.add_folder"] = "fallback later", ["text.fixture_fallback"] = "fallback later" }
        );
        Write(second, "FixtureCedar", new Dictionary<string, string> { ["text.fixture_selected"] = "selected later" });
        Load("FixtureCedar", first, second);
        Check(Get("text.add_folder") == "selected first", "A selected language in an earlier directory beats every fallback layer");
        Check(
            Get("text.fixture_fallback") == "fallback later" && Get("text.fixture_selected") == "selected later",
            "Later directories override earlier directories within the same language layer"
        );
        Write(second, "English", new Dictionary<string, string> { ["text.add_folder"] = "external English override" });
        Load("Russian", second);
        Check(Get("text.add_folder") == "Добавить папку", "Embedded selected language beats external English fallback");
    }

    private static void InvalidFiles()
    {
        string directory = Folder("invalid-json");
        string[] invalid = { "{", "null", "[]", "{\"text.add_folder\":\"must not leak\",\"text.invalid\":42}" };
        for (int index = 0; index < invalid.Length; index++)
        {
            File.WriteAllText(Path.Combine(directory, "English.json"), invalid[index]);
            File.WriteAllText(Path.Combine(directory, "FixtureInvalid.json"), invalid[index]);
            Load("FixtureInvalid", directory);
            Check(
                Get("text.add_folder") == "Add folder" && Culture.Name == "en-US",
                "Invalid JSON preserves valid embedded defaults without partial overlays: " + index
            );
        }
        Load("FixtureMissing", directory);
        Check(
            Language == "FixtureMissing" && Get("boombox_name") == "Boombox",
            "Missing selected language falls back without inventing an override"
        );
        Write(directory, "FixtureCulture", new Dictionary<string, string> { ["meta.culture"] = "invalid/culture" });
        Load("FixtureCulture", directory);
        Check(Culture.Name == "en-US", "Invalid selected culture uses the fallback culture");
    }

    private static void SwitchLanguages()
    {
        string directory = Folder("switches");
        Write(
            directory,
            "FixtureFirst",
            new Dictionary<string, string> { ["text.add_folder"] = "first only", ["text.fixture_stale"] = "old value", ["meta.culture"] = "de-DE" }
        );
        Write(directory, "FixtureSecond", new Dictionary<string, string> { ["text.add_folder"] = "second only" });
        Load("FixtureFirst", directory);
        Check(Get("text.fixture_stale") == "old value", "First language override is observable before switching");
        Load("FixtureSecond", directory);
        Check(
            Get("text.add_folder") == "second only" && Get("text.fixture_stale") == "text.fixture_stale" && Culture.Name == "en-US",
            "A language switch removes stale text and culture overrides"
        );
        Load("FixtureFirst");
        Check(
            Get("text.add_folder") == "Add folder" && Get("text.fixture_stale") == "text.fixture_stale",
            "Reload without external directories removes previous external overrides"
        );
    }

    private static void InvalidLanguageNames()
    {
        string directory = Folder("confined");
        Write(root, "escaped", new Dictionary<string, string> { ["text.add_folder"] = "escaped lookup" });
        foreach (string name in new[] { "../escaped", "..\\escaped", Path.Combine(root, "escaped"), ".", "..", "bad:name", "bad\0name" })
        {
            Load(name, directory);
            Check(
                Language == "English" && Get("text.add_folder") == "Add folder",
                "Invalid filename cannot select text outside the directory: " + name.Replace("\0", "\\0")
            );
        }
    }

    private static void ConfigDisplayNames(Assembly product)
    {
        var attributes = product.GetType("VikingBoombox.ConfigurationManagerAttributes", true);
        var expected = new Dictionary<string, (string Category, string Name)>
        {
            ["Russian"] = ("Музыка", "Рецепт Кассета ранга 7"),
            ["English"] = ("Music", "Recipe Cassette rank 7")
        };
        var labels = new Dictionary<string, (string English, string Russian)>
        {
            ["BuffAnchor"] = ("Bonus panel anchor", "Привязка панели бонусов"),
            ["ShowBuffPanel"] = ("Show bonus panel", "Показывать панель бонусов"),
            ["ComputerAudioEnabled"] = ("Computer audio reaction", "Реакция на звук компьютера"),
            ["DamageMultiplier"] = ("Shared damage multiplier", "Множитель общего урона")
        };
        foreach (string language in new[] { "Russian", "English", "Russian" })
        {
            Load(language);
            object display = Activator.CreateInstance(attributes, new object[] { "Music", "Recipe Cassette rank 7" });
            Check(
                (string)attributes.GetField("Category").GetValue(display) == expected[language].Category,
                "Existing config category follows current language: " + language
            );
            Check(
                (string)attributes.GetField("DispName").GetValue(display) == expected[language].Name,
                "Existing config display name follows current language: " + language
            );
            foreach (var label in labels)
            {
                object translated = Activator.CreateInstance(attributes, new object[] { "Party", label.Key });
                Check(
                    (string)attributes.GetField("DispName").GetValue(translated)
                        == (language == "Russian" ? label.Value.Russian : label.Value.English),
                    "Readable setting label follows language changes: " + label.Key + " / " + language
                );
            }
        }
    }
}
