using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

internal static class ReviewRegressionChecks
{
    private static readonly List<string> passed = new List<string>();
    private static void Check(bool value, string name)
    {
        if (!value)
            throw new InvalidOperationException(name);
        passed.Add(name);
    }

    public static int Main(string[] args)
    {
        string dll = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string folder in new[] { args[1], args[2] })
            {
                string path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            return null;
        };
        var product = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        string output = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(output);
        var failures = new List<string>();
        try
        {
            Recovery(product, output);
        }
        catch (Exception error)
        {
            failures.Add("Recovery: " + error.GetBaseException().Message);
        }
        try
        {
            Cancellation(product);
        }
        catch (Exception error)
        {
            failures.Add("Cancellation: " + error.GetBaseException().Message);
        }
        try
        {
            Configuration(product, output);
        }
        catch (Exception error)
        {
            failures.Add("Configuration: " + error);
        }
        try
        {
            BonusPresentation(product);
        }
        catch (Exception error)
        {
            failures.Add("BonusPresentation: " + error.GetBaseException().Message);
        }
        try
        {
            DecorationNumbering(product);
        }
        catch (Exception error)
        {
            failures.Add("DecorationNumbering: " + error.GetBaseException().Message);
        }
        try
        {
            DesktopDisposal(product);
        }
        catch (Exception error)
        {
            failures.Add("DesktopDisposal: " + error.GetBaseException().Message);
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passed = failures.Count == 0,
            checks = passed,
            failures,
            dllSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll)))
        }));
        return failures.Count == 0 ? 0 : 1;
    }

    private static void DecorationNumbering(Assembly product)
    {
        var catalog = product.GetType("VikingBoombox.BoomboxDecorationCatalog", true);
        var slots = (Array)catalog.GetField("Slots", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var slotType = slots.GetValue(0).GetType();
        int Field(object slot, string name) => (int)slotType.GetField(name).GetValue(slot);
        var ordered = slots.Cast<object>().OrderBy(s => Field(s, "Number")).ToArray();
        Check(ordered.Select(s => Field(s, "Number")).SequenceEqual(Enumerable.Range(1, 24)),
            "Decoration numbers cover 1 through 24 exactly once");
        for (int index = 0; index < slots.Length; index++)
            Check(Field(slots.GetValue(index), "Index") == index,
                "Saved decoration slot identity preserved: " + index);
        using var stream = product.GetManifestResourceStream("ValheimBoombox.ContactShapes.json");
        using var layout = JsonDocument.Parse(stream);
        var mounts = new Dictionary<int, float[]>();
        foreach (var name in layout.RootElement.GetProperty("mounts").EnumerateArray())
            foreach (var group in layout.RootElement.GetProperty("mountLayouts").GetProperty(name.GetString()).EnumerateArray())
            {
                var ids = group.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var aims = group.GetProperty("aims").EnumerateArray().ToArray();
                for (int i = 0; i < ids.Length; i++)
                    mounts.Add(ids[i], aims[i].EnumerateArray().Select(v => v.GetSingle()).ToArray());
            }
        float[] Position(int number) => mounts[Field(ordered[number - 1], "Index")];
        Check(Position(1)[0] > 0 && Position(1)[2] < 0
            && Position(2)[0] > 0 && Position(2)[2] == 0
            && Position(3)[0] > 0 && Position(3)[2] > 0,
            "Lower 1, 2, 3 run from viewer-left back through side midpoint to front corner");
        Check(Position(4)[0] > Position(5)[0] && Position(5)[0] > Position(6)[0]
            && Position(6)[0] == 0 && Enumerable.Range(4, 5).All(n => Position(n)[2] > 0)
            && Enumerable.Range(3, 6).All(n => Position(n)[0] > Position(n + 1)[0]),
            "Lower 4 and 5 lie under left speaker, 6 is center, and front order continues right");
        Check(Position(9)[0] < 0 && Position(9)[2] > 0
            && Position(10)[0] < 0 && Position(10)[2] == 0
            && Position(11)[0] < 0 && Position(11)[2] < 0,
            "Lower 9, 10, 11 continue from viewer-right front to back");
        Check(Enumerable.Range(12, 3).All(n => Position(n)[0] > 0)
            && Position(12)[2] < Position(13)[2] && Position(13)[2] < Position(14)[2]
            && Enumerable.Range(15, 3).All(n => Position(n)[0] < 0)
            && Position(15)[2] > Position(16)[2] && Position(16)[2] > Position(17)[2],
            "Upper 12 through 17 continue around left back-to-front and right front-to-back");
        var stands = ordered.Skip(17).ToArray();
        Check(stands.All(s => slotType.GetField("Kind").GetValue(s).ToString() == "Figurine")
            && stands.Select(s => Field(s, "MountIndex")).SequenceEqual(Enumerable.Range(0, 7).Reverse()),
            "Stands 18 through 24 run from viewer left to right without moving mounts");
    }

    private static void DesktopDisposal(Assembly product)
    {
        var type = product.GetType("VikingBoombox.BoomboxDesktopCapture", true);
        object capture = RuntimeHelpers.GetUninitializedObject(type);
        var signal = new ManualResetEvent(false);
        var thread = new Thread(() => signal.WaitOne()) { IsBackground = true };
        var nativeHandle = signal.SafeWaitHandle;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        type.GetField("stop", fields).SetValue(capture, signal);
        type.GetField("worker", fields).SetValue(capture, thread);
        type.GetField("stopGate", fields)?.SetValue(capture, new object());
        thread.Start();
        var dispose = type.GetMethod("Dispose");
        dispose.Invoke(capture, null);
        Check(
            !thread.IsAlive && nativeHandle.IsClosed,
            "Desktop capture stops a waiting worker and releases its stop handle"
        );
        type.GetMethod("Stop", fields).Invoke(capture, null);
        dispose.Invoke(capture, null);
        passed.Add("Desktop capture accepts Stop and Dispose again after completion without throwing");
    }

    private static void Recovery(Assembly product, string output)
    {
        var library = product.GetType("VikingBoombox.CassetteLibrary", true);
        var store = product.GetType("VikingBoombox.StateStore", true);
        var read = store.GetMethod("Read").MakeGenericMethod(library);
        const string valid =
            "{\"cassettes\":[{\"id\":\"preserved-id\",\"name\":\"Saved music\",\"tracks\":[\"local-track\"]}]}";
        var broken = new[] { "", " \r\n\t", "null", "{" };
        for (int index = 0; index < broken.Length; index++)
        {
            string path = Path.Combine(output, "recovery-" + index + ".json");
            File.WriteAllText(path, broken[index]);
            File.WriteAllText(path + ".bak", valid);
            object recovered = read.Invoke(null, new object[] { path });
            Check(recovered != null, "Existing damaged file must recover, not become an empty library: " + index);
            var entries = (System.Collections.IList)library.GetField("cassettes").GetValue(recovered);
            Check(
                entries.Count == 1
                    && (string)entries[0].GetType().GetField("id").GetValue(entries[0]) == "preserved-id",
                "Recover backup: input " + index
            );
            Check(
                File.ReadAllText(path) == broken[index] && File.ReadAllText(path + ".bak") == valid,
                "Read preserves original files: " + index
            );
            string noBackup = Path.Combine(output, "no-backup-" + index + ".json");
            File.WriteAllText(noBackup, broken[index]);
            bool failed = false;
            try
            {
                read.Invoke(null, new object[] { noBackup });
            }
            catch (TargetInvocationException error) when (error.InnerException.GetType()
                .FullName.StartsWith("Newtonsoft.Json."))
            {
                failed = true;
            }
            Check(failed && File.ReadAllText(noBackup) == broken[index], "Reject unrecoverable data: " + index);
        }
        string nullBackup = Path.Combine(output, "null-backup.json");
        File.WriteAllText(nullBackup, "null");
        File.WriteAllText(nullBackup + ".bak", "null");
        bool rejected = false;
        try
        {
            read.Invoke(null, new object[] { nullBackup });
        }
        catch (TargetInvocationException error) when (error.InnerException.GetType()
            .FullName.StartsWith("Newtonsoft.Json."))
        {
            rejected = true;
        }
        Check(rejected, "Reject null backup");
        string missing = Path.Combine(output, "absent.json");
        Check(
            !File.Exists(missing) && read.Invoke(null, new object[] { missing }) == null,
            "Missing file still permits first use"
        );
    }

    private static void Cancellation(Assembly product)
    {
        var type = product.GetType("VikingBoombox.BoomboxPlugin", true);
        object plugin = RuntimeHelpers.GetUninitializedObject(type);
        var busy = type.GetField("CatalogRefreshing");
        var owner = type.GetField("catalogPreparation", BindingFlags.NonPublic | BindingFlags.Instance);
        var complete = type.GetMethod("CompleteCatalogRefresh", BindingFlags.NonPublic | BindingFlags.Instance);
        var cancel = type.GetMethod("CancelCatalogRefresh");
        var start = type.GetMethod("RefreshCatalog");
        for (int round = 0; round < 2; round++)
        {
            var source = new CancellationTokenSource();
            var token = source.Token;
            owner.SetValue(plugin, source);
            busy.SetValue(plugin, true);
            cancel.Invoke(plugin, null);
            Check(
                token.IsCancellationRequested && (bool)busy.GetValue(plugin),
                "Cancellation keeps pending scan busy: " + round
            );
            start.Invoke(plugin, null);
            Check(ReferenceEquals(owner.GetValue(plugin), source), "Restart cannot replace pending scan: " + round);
            cancel.Invoke(plugin, null);
            complete.Invoke(plugin, null);
            Check(
                !(bool)busy.GetValue(plugin) && owner.GetValue(plugin) == null,
                "Completion releases scan ownership: " + round
            );
        }
        cancel.Invoke(plugin, null);
        Check(!(bool)busy.GetValue(plugin), "Cancel when idle remains idle");
    }

    private static void Configuration(Assembly product, string output)
    {
        ConfigureGeometrySerialization();
        string path = Path.Combine(output, "configured-recipes.cfg");
        File.WriteAllText(
            path,
            "[Recipe Cassette rank 1]\nStation = forge\nStationLevel = 3\nMaterials = Wood:17\n\n"
                + "[Recipe Cassette rank 7]\nStation = blackforge\nStationLevel = 4\nMaterials = Iron:29\n\n"
                + "[Speaker effects]\nShowCassetteSymbols = true\nCassetteSymbolIntensity = 0.2\n\n"
                + "[Cassette symbols]\nRedColor = RGBA(1,0,0,1)\n\n"
                + "[Music]\nPythonPath = retired-python\n\n[Unrelated mod setting]\nKeep = user-value\n"
        );
        var configType = Assembly.Load("BepInEx").GetType("BepInEx.Configuration.ConfigFile", true);
        var boundary = product.GetType("VikingBoombox.BoomboxConfiguration", true).GetMethod(
            "Load",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        var progression = product.GetType("VikingBoombox.BoomboxCassetteProgression", true).GetMethod("Recipe");
        for (int round = 0; round < 2; round++)
        {
            object config = Activator.CreateInstance(configType, new object[] { path, false, null });
            configType.GetProperty("SaveOnConfigSet").SetValue(config, true);
            object settings = boundary.Invoke(null, new[] { config });
            bool hasCassetteSymbolControl = false;
            var definitions = (System.Collections.IEnumerable)configType.GetProperty("Keys").GetValue(config);
            foreach (object definition in definitions)
            {
                var type = definition.GetType();
                string section = (string)type.GetProperty("Section").GetValue(definition);
                string key = (string)type.GetProperty("Key").GetValue(definition);
                hasCassetteSymbolControl |= section == "Cassette symbols"
                    || (section == "Speaker effects"
                        && (key == "ShowCassetteSymbols" || key == "CassetteSymbolIntensity"));
            }
            Check(!hasCassetteSymbolControl, "Removed cassette symbol controls are not exposed: round " + round);
            foreach (int rank in new[] { 1, 7 })
            {
                object recipe = progression.Invoke(null, new object[] { settings, rank });
                var type = recipe.GetType();
                Check(
                    (string)type.GetProperty("StationPrefab").GetValue(recipe) == (rank == 1 ? "forge" : "blackforge"),
                    "Startup preserves custom station: rank " + rank + ", round " + round
                );
                Check(
                    (int)type.GetProperty("StationLevel").GetValue(recipe) == (rank == 1 ? 3 : 4),
                    "Startup preserves custom station level: rank " + rank + ", round " + round
                );
                var ingredients = (System.Collections.IEnumerable)type.GetProperty("Ingredients").GetValue(recipe);
                bool expected = false;
                foreach (object ingredient in ingredients)
                {
                    var ingredientType = ingredient.GetType();
                    expected |= (string)ingredientType.GetProperty("Prefab").GetValue(ingredient) == (rank == 1
                        ? "Wood"
                        : "Iron")
                    && (int)ingredientType.GetProperty("Amount").GetValue(ingredient) == (rank == 1 ? 17 : 29);
                }
                Check(expected, "Startup preserves custom material cost: rank " + rank + ", round " + round);
            }
            string text = File.ReadAllText(path);
            Check(
                !text.Contains("retired-python") && !text.Contains("PythonPath"),
                "Startup removes retired decoder setting: round " + round
            );
            Check(text.Contains("user-value"), "Startup preserves unrelated user setting: round " + round);
            Check(
                (bool)configType.GetProperty("SaveOnConfigSet").GetValue(config),
                "Startup restores save policy: round " + round
            );
        }
    }

    private static void ConfigureGeometrySerialization()
    {
        var host = Assembly.Load("BepInEx").GetType("BepInEx.Configuration.TomlTypeConverter", true);
        var engine = Assembly.Load("UnityEngine.CoreModule");
        foreach (string name in new[] { "Color", "Vector2", "Vector3", "Vector4", "Quaternion" })
        {
            var type = engine.GetType("UnityEngine." + name, true);
            object converter = host.GetMethod("GetConverter").Invoke(null, new object[] { type });
            converter.GetType().GetProperty("ConvertToString").SetValue(
            converter,
            (Func<object, Type, string>)((value, valueType) =>
                {
                    var fields = new Dictionary<string, float>();
                    foreach (var field in valueType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        if (field.FieldType == typeof(float))
                            fields[field.Name] = (float)field.GetValue(value);
                    return JsonSerializer.Serialize(fields);
                })
            );
            converter.GetType().GetProperty("ConvertToObject").SetValue(
            converter,
            (Func<string, Type, object>)((text, valueType) =>
                {
                    object value = Activator.CreateInstance(valueType);
                    using var document = JsonDocument.Parse(text);
                    foreach (var field in valueType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        if (field.FieldType == typeof(float) && document.RootElement.TryGetProperty(field.Name, out var number))
                            field.SetValue(value, number.GetSingle());
                    return value;
                })
            );
        }
    }

    private static void BonusPresentation(Assembly product)
    {
        var balanceType = product.GetType("VikingBoombox.BoomboxAuraBalance", true);
        object balance = Activator.CreateInstance(balanceType);
        float[] cassette = (float[])balanceType.GetField("Cassette").GetValue(balance);
        float[] strength = (float[])balanceType.GetField("Strength").GetValue(balance);
        float[] initial = (float[])balanceType.GetField("Initial").GetValue(balance);
        float[] perRank = (float[])balanceType.GetField("PerRank").GetValue(balance);
        float[] period = (float[])balanceType.GetField("Period").GetValue(balance);
        cassette[0] = .1f;
        strength[0] = 2f;
        cassette[1] = 4f;
        strength[1] = .5f;
        initial[0] = 2f;
        perRank[0] = 3f;
        period[0] = 2f;
        cassette[4] = .2f;
        strength[4] = .5f;
        initial[1] = 7f;
        period[1] = 3f;
        var text = product.GetType("VikingBoombox.BoomboxText", true);
        object previous = text.GetProperty("Language").GetValue(null);
        var loadLanguage = text.GetMethod("LoadTranslations");
        loadLanguage.Invoke(null, new object[] { "English", Array.Empty<string>() });
        try
        {
            var format = product.GetType("VikingBoombox.BoomboxCassetteWorkshop", true).GetMethod(
                "EffectFromBalance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Check(
                (string)format.Invoke(null, new[] { (object)"Red", 1, balance }) == "+20%",
                "Cassette preview uses host damage and party multiplier"
            );
            Check(
                (string)format.Invoke(null, new[] { (object)"Green", 2, balance }) == "+10 every 2 s",
                "Cassette preview uses host amount and recovery period"
            );
            Check(
                (string)format.Invoke(null, new[] { (object)"Yellow", 1, balance }) == "+7 every 3 s",
                "Health and stamina use their own host coefficients"
            );
            Check(
                (string)format.Invoke(null, new[] { (object)"Purple", 3, balance }) == "+30%",
                "Cassette preview uses host speed multiplier"
            );
            Check(
                (string)format.Invoke(null, new[] { (object)"Green", 2, null }) == "Waiting for server settings",
                "Preview does not invent local values before host settings arrive"
            );
            object source = Activator.CreateInstance(product.GetType("VikingBoombox.BoomboxAuraSource", true));
            var ranks = (int[])source.GetType().GetField("Ranks").GetValue(source);
            ranks[0] = 1;
            ranks[1] = 2;
            ranks[4] = 3;
            object contribution = product.GetType("VikingBoombox.BoomboxAuraMath", true)
                .GetMethod("Contribution")
                .Invoke(
                null,
                new[] { source, balance }
            );
            var actual = (float[])contribution.GetType().GetField("Values").GetValue(contribution);
            Check(
                Math.Abs(actual[0] - .2f) < .00001f
                    && Math.Abs(actual[1] - 10f) < .00001f
                    && Math.Abs(actual[4] - .3f) < .00001f,
                "Runtime contribution keeps independently specified host values"
            );
        }
        finally
        {
            loadLanguage.Invoke(null, new object[] { previous, Array.Empty<string>() });
        }
    }
}
