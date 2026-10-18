using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VikingBoombox
{
    public static class BoomboxText
    {
        private const string ResourcePrefix = "ValheimBoombox.Translations.";
        private const string ConfigPrefix = "config.prefix.";
        private static readonly Lazy<Catalog> catalog = new Lazy<Catalog>(ReadCatalog);
        private static readonly Lazy<Snapshot> initial = new Lazy<Snapshot>(() => ReadTranslations(null, new string[0]));
        private static readonly Dictionary<ConfigEntryBase, ConfigDescription> originalDescriptions =
            new Dictionary<ConfigEntryBase, ConfigDescription>();
        private static readonly Dictionary<ConfigDescription, string> descriptionKeys =
            new Dictionary<ConfigDescription, string>();
        private static volatile Snapshot loaded;

        private sealed class Catalog
        {
            internal string Fallback;
            internal Dictionary<string, string> Values;
        }

        private sealed class Snapshot
        {
            internal string Language;
            internal CultureInfo Culture;
            internal Dictionary<string, string> Values;
            internal string[] Prefixes;
            internal readonly ConcurrentDictionary<(string Key, int Count), string> SafeFormats =
                new ConcurrentDictionary<(string Key, int Count), string>();
        }

        private sealed class FormatProbe : IFormattable
        {
            internal static readonly FormatProbe Instance = new FormatProbe();
            public string ToString(string format, IFormatProvider provider) => "";
        }

        private static Snapshot Current => loaded ?? initial.Value;
        public static string Language => Current.Language;
        public static CultureInfo Culture => Current.Culture;
        public static IEnumerable<string> Keys => Current.Values.Keys;

        public static string Get(string key)
        {
            if (key == null)
                return null;
            return Current.Values.TryGetValue(key, out var value) ? value : key;
        }

        public static string Format(string key, params object[] values) => Format(Current, key, values ?? Array.Empty<object>());

        private static string Format(Snapshot state, string key, object[] values)
        {
            var cacheKey = (key, values.Length);
            if (!state.SafeFormats.TryGetValue(cacheKey, out var pattern)
                && !state.Values.TryGetValue(key, out pattern))
                pattern = key;
            try
            {
                return string.Format(state.Culture, pattern, values);
            }
            catch (FormatException error)
            {
                if (catalog.Value.Values.TryGetValue(key, out var fallback)
                    && !string.Equals(pattern, fallback, StringComparison.Ordinal))
                {
                    try
                    {
                        string rendered = string.Format(state.Culture, fallback, values);
                        if (!ValidPattern(pattern, values.Length) && state.SafeFormats.TryAdd(cacheKey, fallback))
                            LogFailure(key, error);
                        return rendered;
                    }
                    catch (FormatException)
                    {
                    }
                }
                return pattern;
            }
        }

        private static bool ValidPattern(string pattern, int count)
        {
            var probes = new object[count];
            for (int index = 0; index < count; index++)
                probes[index] = FormatProbe.Instance;
            try
            {
                string.Format(CultureInfo.InvariantCulture, pattern, probes);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static void LoadTranslations(string language, params string[] directories)
        {
            loaded = ReadTranslations(language, directories ?? new string[0]);
        }

        private static Catalog ReadCatalog()
        {
            var settings = ReadEmbedded("catalog");
            if (!settings.TryGetValue("fallback", out var fallback) || !ValidLanguage(fallback))
                throw new InvalidDataException(ResourcePrefix + "catalog.json");
            return new Catalog { Fallback = fallback, Values = ReadEmbedded(fallback) };
        }

        private static Snapshot ReadTranslations(string language, string[] directories)
        {
            var source = catalog.Value;
            string selected = ValidLanguage(language) ? language : source.Fallback;
            var fallback = new Dictionary<string, string>(source.Values, StringComparer.Ordinal);
            var selectedValues = selected == source.Fallback
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ReadEmbedded(selected, true);
            foreach (string directory in directories)
            {
                if (string.IsNullOrEmpty(directory))
                    continue;
                var externalFallback = ReadExternal(directory, source.Fallback);
                Overlay(fallback, externalFallback);
                if (selected != source.Fallback)
                {
                    var externalSelected = ReadExternal(directory, selected);
                    Overlay(selectedValues, externalSelected);
                }
            }
            fallback.TryGetValue("meta.culture", out var fallbackCulture);
            selectedValues.TryGetValue("meta.culture", out var selectedCulture);
            var culture = ReadCulture(selectedCulture) ?? ReadCulture(fallbackCulture) ?? CultureInfo.InvariantCulture;
            var values = new Dictionary<string, string>(fallback, StringComparer.Ordinal);
            Overlay(values, selectedValues);
            return new Snapshot
            {
                Language = selected,
                Culture = culture,
                Values = values,
                Prefixes = values.Keys.Where(key => key.StartsWith(ConfigPrefix, StringComparison.Ordinal))
                    .Select(key => key.Substring(ConfigPrefix.Length))
                    .Where(prefix => prefix.Length > 0)
                    .OrderByDescending(prefix => prefix.Length)
                    .ToArray()
            };
        }

        private static CultureInfo ReadCulture(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            try
            {
                return CultureInfo.GetCultureInfo(name);
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        private static bool ValidLanguage(string language) =>
            !string.IsNullOrEmpty(language)
            && language != "."
            && language != ".."
            && language.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && language.IndexOf('/') < 0
            && language.IndexOf('\\') < 0;

        private static Dictionary<string, string> ReadEmbedded(string language, bool optional = false)
        {
            string name = ResourcePrefix + language + ".json";
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    if (optional)
                        return new Dictionary<string, string>(StringComparer.Ordinal);
                    throw new InvalidDataException(name);
                }
                using (var reader = new StreamReader(stream))
                    return ReadDictionary(reader.ReadToEnd());
            }
        }

        private static Dictionary<string, string> ReadExternal(string directory, string language)
        {
            string file = null;
            try
            {
                file = Path.Combine(directory, language + ".json");
                if (!File.Exists(file))
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                return ReadDictionary(File.ReadAllText(file));
            }
            catch (Exception error) when (error is IOException
                || error is JsonException
                || error is UnauthorizedAccessException
                || error is ArgumentException
                || error is NotSupportedException)
            {
                LogFailure(file ?? directory, error);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static Dictionary<string, string> ReadDictionary(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in JObject.Parse(json).Properties())
            {
                if (property.Value.Type != JTokenType.String)
                    throw new JsonSerializationException(property.Name);
                result.Add(property.Name, (string)property.Value);
            }
            return result;
        }

        private static void Overlay(Dictionary<string, string> target, Dictionary<string, string> values)
        {
            foreach (var pair in values)
                target[pair.Key] = pair.Value;
        }

        private static void LogFailure(string source, Exception error)
        {
            try
            {
                BoomboxPlugin.Instance?.LogError(source + ": " + error.Message);
            }
            catch (Exception)
            {
                // Translation fallback also works before the game initializes its logger.
            }
        }

        public static string ConfigName(string technicalName)
        {
            if (technicalName == null)
                return null;
            return ConfigName(technicalName, Current);
        }

        private static string ConfigName(string technicalName, Snapshot state)
        {
            if (state.Values.TryGetValue("config." + technicalName, out var exact))
                return exact;
            foreach (string prefix in state.Prefixes)
            {
                if (!technicalName.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                string suffix = technicalName.Substring(prefix.Length).TrimStart();
                if (suffix.Length == 0)
                    continue;
                string translatedSuffix = decimal.TryParse(suffix, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                    ? suffix
                    : ConfigName(suffix, state);
                return Format(state, ConfigPrefix + prefix, new object[] { translatedSuffix });
            }
            return "config." + technicalName;
        }

        public static ConfigDescription Description(
            string key,
            AcceptableValueBase acceptableValues = null,
            params object[] tags
        )
        {
            var description = new ConfigDescription(Get(key), acceptableValues, tags);
            descriptionKeys.Add(description, key);
            return description;
        }

        public static void RefreshConfig()
        {
            var plugin = BoomboxPlugin.Instance;
            if (plugin == null)
                return;
            var field = AccessTools.Field(typeof(ConfigEntryBase), "<Description>k__BackingField");
            foreach (var pair in plugin.Config)
            {
                var entry = pair.Value;
                if (!originalDescriptions.TryGetValue(entry, out var original))
                    originalDescriptions.Add(entry, original = entry.Description);
                var tags = original.Tags.Concat(
                    new object[] { new ConfigurationManagerAttributes(entry.Definition.Section, entry.Definition.Key) }
                ).ToArray();
                string description = Get(descriptionKeys.TryGetValue(original, out var key) ? key : original.Description);
                field.SetValue(entry, new ConfigDescription(description, original.AcceptableValues, tags));
            }
        }
    }

    public sealed class ConfigurationManagerAttributes
    {
        public string Category;
        public string DispName;
        public ConfigurationManagerAttributes(string section, string key)
        {
            Category = BoomboxText.ConfigName(section);
            DispName = BoomboxText.ConfigName(key);
        }
    }
}
