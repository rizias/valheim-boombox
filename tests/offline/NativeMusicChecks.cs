using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using VikingBoombox;

public static partial class NativeMusicChecks
{
    private static int checks;
    private static string audioTools;
    private static readonly JsonSerializerOptions json = new() { IncludeFields = true, WriteIndented = true };
    public static int Main(string[] args)
    {
        audioTools = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
        string game = BoomboxQa.QaEnvironment.ClientRoot;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string dir in new[] { BoomboxQa.QaEnvironment.Managed, BoomboxQa.QaEnvironment.Core })
            {
                string path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            Run(args[0]);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
            throw new Exception(message);
    }
    internal static string[] Written(string run, string tempRoot = null)
    {
        var entries = new List<string>();
        var traversal = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        };
        var roots = Directory.GetFileSystemEntries(tempRoot ?? Path.GetTempPath(), "ValheimBoombox*").Append(run);
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var paths = Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0
                ? Directory.EnumerateFileSystemEntries(root, "*", traversal).Prepend(root)
                : new[] { root };
            foreach (string path in paths)
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    entries.Add("D:" + Path.GetFullPath(path));
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    entries.Add("L:" + Path.GetFullPath(path));
                    continue;
                }
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                entries.Add(JsonSerializer.Serialize(new
                {
                    path = Path.GetFullPath(path),
                    length = file.Length,
                    lastWriteUtcTicks = File.GetLastWriteTimeUtc(path).Ticks,
                    hash = Convert.ToHexString(SHA256.HashData(file))
                }));
            }
        }
        return entries.OrderBy(entry => entry, StringComparer.Ordinal).ToArray();
    }
    private static long WaitReady(MusicLiveStream stream)
    {
        var watch = Stopwatch.StartNew();
        while (!stream.Ready && stream.Error == null && watch.ElapsedMilliseconds < 5000)
            Thread.Sleep(2);
        return watch.ElapsedMilliseconds;
    }
    private static float[] Decode(string ffmpeg, string file, params string[] range)
    {
        var info = new ProcessStartInfo(
            ffmpeg
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin" }.Concat(range).Concat(
            new[] {
            "-i",
            file,
            "-map",
            "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-ac",
            "2",
            "-ar",
            MusicLiveStream.Frequency.ToString(),
            "-f",
            "f32le",
            "pipe:1"
                }
        ))
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info);
        var error = process.StandardError.ReadToEndAsync();
        using var memory = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(memory);
        process.WaitForExit();
        Check(process.ExitCode == 0 && memory.Length > 0, "reference decode of " + file + ": " + error.Result);
        var samples = new float[memory.Length / sizeof(float)];
        Buffer.BlockCopy(memory.GetBuffer(), 0, samples, 0, samples.Length * sizeof(float));
        return samples;
    }
    private static float[] Slice(float[] whole, double start, double? end)
    {
        int from = (int)Math.Round(start * MusicLiveStream.Frequency) * 2;
        int to = end.HasValue
            ? Math.Min(whole.Length, (int)Math.Round(end.Value * MusicLiveStream.Frequency) * 2)
            : whole.Length;
        return whole[from..to];
    }
    private static float At(float[] reference, long index) => index >= 0 && index < reference.Length
        ? reference[index]
        : 0;
    private static double Compare(MusicLiveStream stream, float[] reference, long frame, long frames)
    {
        var a = new float[4096];
        double worst = 0;
        long offset = (long)frame * 2;
        for (long done = 0; done < frames; done += a.Length / 2, offset += a.Length)
        {
            stream.Read(a);
            for (int i = 0; i < a.Length; i++)
                worst = Math.Max(worst, Math.Abs(a[i] - At(reference, offset + i)));
        }
        return worst;
    }
    private static (int shift, double diff) Aligned(
        MusicLiveStream stream,
        float[] reference,
        long frame,
        int frames,
        int reach
    )
    {
        var a = new float[frames * 2];
        for (int done = 0; done < a.Length; done += 4096)
        {
            var part = new float[Math.Min(4096, a.Length - done)];
            stream.Read(part);
            Array.Copy(part, 0, a, done, part.Length);
        }
        int bestShift = 0;
        double best = double.MaxValue;
        foreach (int shift in Enumerable.Range(0, reach * 2 + 1).Select(k => k % 2 == 0 ? k / 2 : -(k + 1) / 2))
        {
            long offset = (long)(frame + shift) * 2;
            if (offset < 0)
                continue;
            double worst = 0;
            for (int i = 0; i < a.Length && worst < best; i++)
                worst = Math.Max(worst, Math.Abs(a[i] - At(reference, offset + i)));
            if (worst < best)
            {
                best = worst;
                bestShift = shift;
            }
        }
        return (bestShift, best);
    }
    private static (long ready, int shift, double diff, int launches) StartAt(
        MusicLiveSource live,
        float[] reference,
        double seconds,
        bool unityPrelude
    )
    {
        long frame = MusicLiveStream.FramesOf(seconds);
        using var stream = new MusicLiveStream(live, 2, 2048);
        if (unityPrelude)
        {
            stream.SetPosition(0);
            stream.Read(new float[4096]);
        }
        stream.Begin(frame);
        long ready = WaitReady(stream);
        var result = Aligned(stream, reference, frame, MusicLiveStream.Frequency / 4, MusicLiveStream.Frequency / 20);
        return (ready, result.shift, result.diff, stream.Launches);
    }
    private static object Live(
        MusicTrack track,
        float[] reference,
        string ending,
        double duration,
        string title,
        string artist,
        string ffmpeg,
        string ffprobe,
        string run
    )
    {
        var before = Written(run);
        double seconds = reference.Length / 2 / (double)MusicLiveStream.Frequency;
        var live = MusicLiveSource.Locate(track, ffmpeg, ffprobe, CancellationToken.None);
        Check(
            live.Duration > duration - .06 && live.Duration < duration + .25,
            ending + " live duration " + live.Duration
        );
        if (title != null)
            Check(
                live.Title == title && live.Artist == artist,
                ending + " live metadata " + live.Title + " / " + live.Artist
            );
        long readyWhole;
        double wholeDiff, rewindDiff;
        using (var stream = new MusicLiveStream(live, 2, 2048))
        {
            stream.Begin(0);
            readyWhole = WaitReady(stream);
            Check(stream.Ready, ending + " live ready " + stream.Error);
            wholeDiff = Compare(stream, reference, 0, MusicLiveStream.FramesOf(seconds + 1));
            Check(
                wholeDiff < .002,
                ending + " live samples match the reference decode of the whole file, max diff " + wholeDiff
            );
            Check(
                stream.Launches == 1 && stream.Underruns == 0,
                ending + " whole track from one decoder start without underruns, launches " + stream.Launches
                    + ", underruns "
                    + stream.Underruns
            );
            var eofWatch = Stopwatch.StartNew();
            while (stream.EofFrame < 0 && eofWatch.ElapsedMilliseconds < 2000)
                Thread.Sleep(2);
            Check(
                stream.EofFrame > 0
                    && Math.Abs(stream.EofFrame - seconds * MusicLiveStream.Frequency) < MusicLiveStream.Frequency / 20,
                ending + " end of audio found at " + stream.EofFrame + ", reference " + seconds
            );
            Check(
                stream.Frames >= stream.EofFrame,
                ending + " clip keeps the whole decoded audio, clip " + stream.Frames + ", audio " + stream.EofFrame
            );
            double length = stream.Seconds;
            stream.SetPosition(stream.Frames - 10);
            WaitReady(stream);
            Check(stream.Ready, ending + " ready after moving past the end of the audio");
            Check(
                stream.Seconds == length,
                ending + " moving past the end of the audio keeps the found length, " + length + " / " + stream.Seconds
            );
        }
        var resume = StartAt(live, reference, 1.235, true);
        Check(
            resume.diff < .002 && Math.Abs(resume.shift) <= MusicLiveStream.Frequency / 1000,
            ending + " resume in the middle matches the reference within 1 ms, shift " + resume.shift + ", max diff "
                + resume.diff
        );
        Check(resume.launches == 1, ending + " resume sets the position once, launches " + resume.launches);
        var far = StartAt(live, reference, duration * .75 + .0123, false);
        Check(
            far.diff < .002 && Math.Abs(far.shift) <= MusicLiveStream.Frequency / 1000,
            ending + " start at three quarters of the track matches the reference within 1 ms, shift " + far.shift
                + ", max diff "
                + far.diff
        );
        using (var stream = new MusicLiveStream(live, .05f, 1))
        {
            stream.Begin(0);
            WaitReady(stream);
            Compare(stream, reference, 0, MusicLiveStream.FramesOf(seconds + 1));
            Check(
                stream.Underruns == 0,
                ending
                    + " 0.05 s buffer read in back-to-back 2048-frame calls, the size of Unity's reads, has no underruns, underruns "
                    + stream.Underruns
            );
            var foundWatch = Stopwatch.StartNew();
            while (stream.EofFrame < 0 && foundWatch.ElapsedMilliseconds < 2000)
                Thread.Sleep(2);
            double found = stream.Seconds;
            Check(
                stream.EofFrame > 0 && found == stream.EofFrame / (double)MusicLiveStream.Frequency,
                ending + " length is the found end of audio, " + found + " / " + stream.EofFrame
            );
            stream.SetPosition(0);
            WaitReady(stream);
            Check(
                stream.Seconds == found && Math.Abs(found - seconds) < .05,
                ending + " length found at the end of audio stays after ffmpeg restarts, " + found + " / "
                    + stream.Seconds
            );
            rewindDiff = Compare(stream, reference, 0, MusicLiveStream.Frequency / 4);
            int launches = duration > 2.8 ? 2 : 1;
            Check(
                rewindDiff < .002 && stream.Launches == launches,
                ending
                    + " rewind to the start: beyond the kept 2.8 s (2 s heard plus Unity's 0.8 s read) ffmpeg restarts, inside it the buffer is reused; launches "
                    + stream.Launches
                    + ", expected "
                    + launches
                    + ", max diff "
                    + rewindDiff
            );
        }
        using (var stream = new MusicLiveStream(live, .01f, 1))
        {
            stream.Begin(0);
            WaitReady(stream);
            Compare(stream, reference, 0, MusicLiveStream.FramesOf(seconds + 1));
            Check(
                stream.Underruns == 0,
                ending
                    + " 480-frame buffer read in 2048-frame calls, each call larger than the buffer, has no underruns, underruns "
                    + stream.Underruns
            );
        }
        Check(Written(run).SequenceEqual(before), ending + " live playback wrote no files");
        return new
        {
            live.Duration,
            live.Title,
            live.Artist,
            readyWhole,
            readyResume = resume.ready,
            readyFar = far.ready,
            wholeDiff,
            resumeDiff = resume.diff,
            resumeShift = resume.shift,
            farDiff = far.diff,
            farShift = far.shift,
            rewindDiff
        };
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string root)
    {
        string source = Path.Combine(BoomboxQa.QaEnvironment.FixturesRoot, "audio");
        string music = Path.Combine(source, "Музыка с пробелами");
        string run = Path.Combine(
            BoomboxQa.QaEnvironment.QaRoot,
            "current/offline/native-audio-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
        );
        Directory.CreateDirectory(run);
        string extra = Path.Combine(run, "Музыка ещё");
        Directory.CreateDirectory(extra);
        File.Copy(Path.Combine(music, "album.flac"), Path.Combine(extra, "plain.flac"));
        string ffmpeg = BoomboxQa.QaEnvironment.Encoder, ffprobe = BoomboxQa.QaEnvironment.MediaProbe;
        if (audioTools != null)
        {
            ffmpeg = Path.Combine(audioTools, "ffmpeg.exe");
            ffprobe = Path.Combine(audioTools, "ffprobe.exe");
        }
        var hashes = Directory.GetFiles(music).ToDictionary(p => p, Hash);
        var previous = JsonSerializer.Deserialize<MusicCatalog>(
            File.ReadAllText(Path.Combine(source, "catalog.json")),
            json
        );
        var catalog = MusicScanner.Scan(new[] { music, music, extra }, previous, ffprobe, CancellationToken.None);
        var tracks = catalog.playlists.SelectMany(p => p.tracks).ToArray();
        Check(catalog.folders.Count == 2, "root deduplication");
        Check(
            tracks.Length == 23 && tracks.Select(t => t.id).Distinct().Count() == 23,
            "23 unique tracks including plain FLAC, WavPack blocks, 5.1 and 176.4 kHz WavPack, three disc images, a disc image with a .cue beside it and a file with an unpacker, found "
                + tracks.Length
        );
        Check(
            tracks.All(t => t.available),
            "every track is available: "
                + string.Join(
                "; ",
                tracks.Where(t => !t.available).Select(t => t.source + " " + t.error)
            )
        );
        Check(
            catalog.errors.Count == 1 && catalog.errors[0].error.Contains("escape.wav"),
            "unsafe ZIP member rejected without losing music"
        );
        foreach (var old in previous.playlists.SelectMany(p => p.tracks))
            Check(
                tracks.Any(t => t.id == old.id && t.source == old.source && t.subsong == old.subsong),
                "legacy ID: " + old.id
            );
        foreach (var (image, listed) in new[] {
            ("образ со ссылкой.iso.wv", "1 Первая / Исполнитель; 2 Вторая / Исполнитель"),
            ("образ без ссылки.iso.wv", "1 Первая / Исполнитель; 2 Вторая / Исполнитель"),
            ("образ с нечитаемым cue.iso.wv", "1 Первая / Исполнитель; 2 Вторая / Исполнитель"),
            ("образ с внешним cue.cue", "1 Внешняя первая / Внешний исполнитель; 2 Внешняя вторая / Внешний исполнитель")
        })
        {
            string found = string.Join(
                "; ",
                tracks.Where(t => t.source.EndsWith(image)).OrderBy(t => t.subsong).Select(
                    t => t.subsong + " " + t.title + " / " + t.artist
                )
            );
            Check(found == listed, image + " tracks come from the fitting .cue: " + found);
        }
        Check(
            !tracks.Any(t => t.source.EndsWith("образ с внешним cue.iso.wv")),
            "a disc image with a .cue beside it is listed only through that .cue"
        );
        File.WriteAllText(Path.Combine(run, "catalog.json"), JsonSerializer.Serialize(catalog, json));
        var decoded = new Dictionary<string, float[]>();
        float[] Whole(string file) => decoded.TryGetValue(file, out var samples)
            ? samples
            : decoded[file] = Decode(ffmpeg, Path.Combine(music, file));
        var results = new List<object>();
        var cases = new (
            string ending,
            int? subsong,
            string file,
            double start,
            double? end,
            double duration,
            string title,
            string artist
        )[]
        {
            ("source.wav", null, "source.wav", 0, null, 4, null, null),
            ("song.mp3", null, "song.mp3", 0, null, 4, null, null),
            ("song.ogg", null, "song.ogg", 0, null, 4, null, null),
            ("song.wv", null, "song.wv", 0, null, 4, null, null),
            ("plain.flac", null, "album.flac", 0, null, 4, null, null),
            ("\\album.cue", 2, "album.flac", 2, null, 2, "Вторая", "Исполнитель"),
            ("embedded.flac", 2, "embedded.flac", 2, null, 2, "Вторая", "Исполнитель"),
            ("|inside/album.cue", 2, "album.flac", 2, null, 2, "Вторая", "Исполнитель"),
            ("\\album.cue", 1, "album.flac", 0, 2, 2, "Первая", "Исполнитель"),
            ("embedded.flac", 1, "embedded.flac", 0, 2, 2, "Первая", "Исполнитель"),
            ("|inside/album.cue", 1, "album.flac", 0, 2, 2, "Первая", "Исполнитель"),
            ("блоки.wv", null, "блоки.wv", 0, null, 20, null, null),
            ("с распаковщиком.wv", null, "блоки.wv", 0, null, 20, null, null),
            ("объёмный 5.1.wv", null, "объёмный 5.1.wv", 0, null, 6, null, null),
            ("176 кГц.wv", null, "176 кГц.wv", 0, null, 6, null, null),
            ("образ со ссылкой.iso.wv", 1, "блоки.wv", 0, 10, 10, "Первая", "Исполнитель"),
            ("образ со ссылкой.iso.wv", 2, "блоки.wv", 10, null, 10, "Вторая", "Исполнитель"),
            ("образ без ссылки.iso.wv", 1, "блоки.wv", 0, 10, 10, "Первая", "Исполнитель"),
            ("образ без ссылки.iso.wv", 2, "блоки.wv", 10, null, 10, "Вторая", "Исполнитель"),
            ("образ с внешним cue.cue", 1, "блоки.wv", 0, 5, 5, "Внешняя первая", "Внешний исполнитель"),
            ("образ с внешним cue.cue", 2, "блоки.wv", 5, null, 15, "Внешняя вторая", "Внешний исполнитель"),
        };
        foreach (var item in cases)
        {
            var track = tracks.Single(t => t.source.EndsWith(item.ending) && t.subsong == item.subsong);
            var reference = Slice(Whole(item.file), item.start, item.end);
            double seconds = reference.Length / 2 / (double)MusicLiveStream.Frequency;
            Check(
                Math.Abs(seconds - item.duration) < .06,
                item.ending + " reference decode lasts " + seconds + " s, expected " + item.duration
            );
            results.Add(new
            {
                track.source,
                track.subsong,
                live = Live(
                    track,
                    reference,
                    item.ending + (item.subsong == null ? "" : " track " + item.subsong),
                    item.duration,
                    item.title,
                    item.artist,
                    ffmpeg,
                    ffprobe,
                    run
                )
            });
        }
        foreach (var (ending, subsong) in new[] {
            ("блоки.wv", (int?)null),
            ("с распаковщиком.wv", null),
            ("объёмный 5.1.wv", null),
            ("176 кГц.wv", null),
            ("образ со ссылкой.iso.wv", 2),
            ("образ без ссылки.iso.wv", 2),
            ("образ с внешним cue.cue", 2)
        })
        {
            var live = MusicLiveSource.Locate(
                tracks.Single(t => t.source.EndsWith(ending) && t.subsong == subsong),
                ffmpeg,
                ffprobe,
                CancellationToken.None
            );
            double at = live.Start + 3.37;
            var arguments = live.Decoding(at, out double from, out var input);
            Check(
                input == null && arguments.Contains("-skip_initial_bytes") && from <= at - .5 && from > at - .6,
                ending
                    + " WavPack start in the middle reads the file from the block holding the point 0.5 s earlier, not from the start of the file: from "
                    + from
                    + " for "
                    + at
                    + ", arguments "
                    + string.Join(" ", arguments)
            );
        }
        results.Add(new
        {
            membranes = Membranes(ffmpeg, ffprobe, run)
        });
        results.Add(new
        {
            longTracks = LongTracks(ffmpeg, ffprobe, source, run)
        });
        string zipped = Path.Combine(run, "mp3.zip");
        using (var archive = ZipFile.Open(zipped, ZipArchiveMode.Create))
            archive.CreateEntryFromFile(Path.Combine(music, "song.mp3"), "song.mp3", CompressionLevel.NoCompression);
        var member = new MusicTrack { id = "zip-mp3", source = "unpack://zip|0|file://" + zipped + "|song.mp3", title = "song" };
        results.Add(new
        {
            member.source,
            live = Live(member, Whole("song.mp3"), "zip|song.mp3", 4, null, null, ffmpeg, ffprobe, run)
        });
        string lockedMessage = null, reported = null;
        using (var hold = new FileStream(zipped, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                File.OpenRead(zipped).Dispose();
            }
            catch (IOException error)
            {
                lockedMessage = error.Message;
            }
            try
            {
                MusicLiveSource.Locate(member, ffmpeg, ffprobe, CancellationToken.None);
            }
            catch (Exception error)
            {
                reported = error.Message;
            }
        }
        Check(
            lockedMessage != null && reported == lockedMessage,
            "archive read failure is reported as it is, not hidden behind an ffprobe error: " + reported
        );
        string oldRoot = Path.Combine(run, "old-cache"),
            oldEntry = Path.Combine(oldRoot, "native-v1", new string('a', 64));
        Directory.CreateDirectory(oldEntry);
        File.WriteAllBytes(Path.Combine(oldEntry, "audio.wav"), new byte[1000]);
        File.WriteAllText(Path.Combine(oldEntry, "entry.json"), "{}");
        File.WriteAllBytes(Path.Combine(oldEntry, ".lock"), new byte[0]);
        File.WriteAllBytes(Path.Combine(oldRoot, "native-v1", "x.p"), new byte[5]);
        File.WriteAllText(Path.Combine(oldRoot, "keep.txt"), "keep");
        long cleared = MusicCache.Clean(oldRoot);
        Check(
            cleared == 1007 && !Directory.Exists(Path.Combine(oldRoot, "native-v1"))
            && File.Exists(Path.Combine(oldRoot, "keep.txt")),
            "old cache cleanup removes the whole old cache and nothing beside it, removed " + cleared
        );
        string extended = Path.Combine(BoomboxQa.QaEnvironment.FixturesRoot, "codec-samples");
        int extendedFormats = 0;
        if (audioTools != null && Directory.Exists(extended))
        {
            foreach (string file in Directory.GetFiles(extended).Where(
                p => new[] { ".m4a", ".aac", ".opus", ".wma" }.Contains(Path.GetExtension(p))
            ))
            {
                var track = new MusicTrack
                {
                    id = "extra-" + Path.GetFileName(file),
                    source = "file://" + file,
                    title = Path.GetFileName(file)
                };
                var reference = Decode(ffmpeg, file);
                double expectedDuration = Path.GetExtension(file) == ".aac"
                    ? 4.224
                    : 4, seconds = reference.Length / 2 / (double)MusicLiveStream.Frequency;
                Check(
                    Math.Abs(seconds - expectedDuration) < .15,
                    Path.GetExtension(file) + " reference decode lasts " + seconds + " s"
                );
                results.Add(
                    new
                    {
                        format = Path.GetExtension(file),
                        seconds,
                        live = Live(
                        track,
                        reference,
                        Path.GetExtension(file),
                        expectedDuration,
                        null,
                        null,
                        ffmpeg,
                        ffprobe,
                        run
                    )
                    }
                );
                extendedFormats++;
            }
        }
        Check(hashes.All(h => Hash(h.Key) == h.Value), "original music unchanged");
        File.WriteAllText(
            Path.Combine(run, "results.json"),
            JsonSerializer.Serialize(
                new
                {
                    passed = true,
                    assertions = checks,
                    ffmpeg,
                    extendedFormats,
                    results
                },
                json
            )
        );
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passed = true,
            assertions = checks,
            ffmpeg,
            extendedFormats,
            report = Path.Combine(run, "results.json")
        }));
    }
}
