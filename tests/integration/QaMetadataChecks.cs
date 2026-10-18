using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaMetadataChecks
    {
        public static void Run(QaLedger ledger)
        {
            var clock = Stopwatch.StartNew();
            var checks = new List<string>();
            void Check(bool value, string name)
            {
                if (!value)
                    throw new IOException(name);
                checks.Add(name);
            }
            string managed = QaEnvironment.Managed;
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                string path = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            string candidate = Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll");
            var product = Assembly.LoadFrom(candidate);
            var source = product.GetType("VikingBoombox.MusicSource")!;
            var track = product.GetType("VikingBoombox.MusicTrack")!;
            var parse = source.GetMethod("ParseMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;
            var resolve = source.GetMethod("LocalPath")!;
            string PathOf(
                string uri
            ) => (string)source.GetField("Path")!.GetValue(parse.Invoke(null, new object[] { uri }))!;
            Check(
                PathOf("file://C:/Music/100% Rock/Band%20Name/song.wav") == @"C:\Music\100% Rock\Band%20Name\song.wav",
                "Metadata preserves literal percent names"
            );
            Check(
                PathOf("file:////unavailable.invalid/share/Artist/song.wav")
                    == @"\\unavailable.invalid\share\Artist\song.wav",
                "UNC metadata stays UNC without accessing server"
            );
            Check(
                PathOf("file:///C:/Music/Artist/../Album/song.wav") == @"C:\Music\Album\song.wav",
                "Pure normalization preserves dot-segment semantics"
            );
            string zipped = "unpack://zip|0|file://C:/Music/a.zip|disc/track.flac";
            Check(
                (string)source.GetField("Member")!.GetValue(parse.Invoke(null, new object[] { zipped }))!
                    == "disc/track.flac",
                "Zip member parsed without opening archive"
            );
            bool rejected = false;
            try
            {
                parse.Invoke(null, new object[] { "unpack://zip|0|file://C:/Music/a.zip|../escape.wav" });
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidDataException)
            {
                rejected = true;
            }
            Check(rejected, "Unsafe zip member still rejected");
            var folderOf = product.GetType("VikingBoombox.BoomboxLibraryData")!.GetMethod(
                "FolderOf",
                BindingFlags.NonPublic | BindingFlags.Static
            )!;
            var folders = new List<string> { @"C:\Music", @"C:\Music\Band%20Name" };
            Check(
                (string)folderOf.Invoke(null, new object[] { "file://C:/Music/Band%20Name/song.wav", folders })!
                    == folders[1],
                "Literal source selects longest literal folder"
            );
            string fixtures = Path.GetFullPath(Path.Combine(ledger.Root, "current/metadata-io"));
            string literal = Path.Combine(fixtures, "literal%25.wav"),
                decoded = Path.Combine(fixtures, "legacy song.wav");
            QaFiles.Write(ledger, literal, new byte[] { 1 });
            QaFiles.Write(ledger, decoded, new byte[] { 2 });
            Check(
                (string)resolve.Invoke(null, new object[] { "file://" + literal })! == literal,
                "Playback still prefers existing literal percent file"
            );
            Check(
                (string)resolve.Invoke(null, new object[] { "file://" + decoded.Replace(" ", "%20") })! == decoded,
                "Playback still resolves existing escaped legacy path"
            );
            object item = Activator.CreateInstance(track)!;
            foreach (var pair in new Dictionary<string, object>
            {
                ["id"] = "stable-id",
                ["title"] = "Песня",
                ["artist"] = "Artist",
                ["album"] = "Album",
                ["albumArtist"] = "Band",
                ["source"] = zipped,
                ["path"] = "old/path.wav",
                ["subsong"] = 2,
                ["duration"] = 4.5d,
                ["durationSeconds"] = 4.5d,
                ["discNumber"] = 2,
                ["trackNumber"] = 3,
                ["available"] = true,
                ["error"] = "test"
            })
                track.GetField(pair.Key)!.SetValue(item, pair.Value);
            var json = Assembly.LoadFrom(Path.Combine(managed, "Newtonsoft.Json.dll"))
                .GetType("Newtonsoft.Json.JsonConvert")!;
            var serialize = json.GetMethod("SerializeObject", new[] { typeof(object) })!;
            var deserialize = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) })!;
            string text = (string)serialize.Invoke(null, new[] { item })!;
            using (var document = JsonDocument.Parse(text))
            {
                foreach (string name in new[] { "Label", "SelectionLabel", "AlbumKey" })
                    Check(!document.RootElement.TryGetProperty(name, out _), "JSON excludes computed " + name);
            }
            string legacy = text.Substring(
                0,
                text.Length - 1
            )
                + ",\"Label\":\"old\",\"SelectionLabel\":\"old\",\"AlbumKey\":\"old\"}";
            object restored = deserialize.Invoke(null, new object[] { legacy, track })!;
            Check(
                track.GetFields(BindingFlags.Public | BindingFlags.Instance).All(
                    f => Equals(f.GetValue(item), f.GetValue(restored))
                ),
                "Legacy JSON roundtrip preserves every persisted field"
            );
            int visited = AssertNoDiskCalls(
                new[] { parse, folderOf, track.GetProperty("AlbumKey")!.GetMethod! },
                product
            );
            checks.Add("Actual DLL metadata call graph contains no filesystem access: " + visited + " methods");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R10/metadata-io.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        candidateSha = QaFiles.Hash(candidate),
                        checks,
                        seconds = clock.Elapsed.TotalSeconds,
                        scope = "Actual product DLL on CLR; reflection call-graph plus path and JSON behavior. No game or decoded audio. Playback filesystem behavior retained."
                    }
                )
            );
            Console.WriteLine(
                "QA_METADATA_PASS " + checks.Count + " checks; " + clock.Elapsed.TotalSeconds.ToString("0.000") + "s"
            );
        }

        private static int AssertNoDiskCalls(IEnumerable<MethodBase> roots, Assembly product)
        {
            var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(
                f => f.FieldType == typeof(OpCode)
            ).Select(
                f => (OpCode)f.GetValue(null)!
            ).ToDictionary(
                o => unchecked((ushort)o.Value)
            );
            var pending = new Stack<MethodBase>(roots);
            var seen = new HashSet<MethodBase>();
            while (pending.Count > 0)
            {
                var method = pending.Pop();
                if (!seen.Add(method))
                    continue;
                var bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes == null)
                    continue;
                for (int i = 0; i < bytes.Length;)
                {
                    ushort key = bytes[i++];
                    if (key == 0xfe)
                        key = (ushort)(0xfe00 | bytes[i++]);
                    var code = codes[key];
                    if (code.OperandType == OperandType.InlineMethod)
                    {
                        var called = method.Module.ResolveMethod(
                            BitConverter.ToInt32(bytes, i),
                            method.DeclaringType?.GetGenericArguments(),
                            method is MethodInfo info && info.IsGenericMethod ? info.GetGenericArguments() : null
                        )!;
                        string owner = called.DeclaringType?.FullName ?? "";
                        if (owner == "System.IO.File" || owner == "System.IO.Directory" || owner == "System.IO.FileInfo"
                        || owner == "System.IO.DirectoryInfo")
                            throw new IOException("Metadata reaches disk: " + called);
                        if (called.DeclaringType?.Assembly == product)
                            pending.Push(called);
                    }
                    i += code.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
                        _ => 4
                    };
                }
            }
            return seen.Count;
        }
    }
}
