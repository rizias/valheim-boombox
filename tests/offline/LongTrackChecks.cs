using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using VikingBoombox;

public static partial class NativeMusicChecks
{
    private const int Rate = MusicLiveStream.Frequency;

    private static object LongTracks(string ffmpeg, string ffprobe, string source, string run)
    {
        string folder = Path.Combine(source, "long");
        var results = new List<object>();
        foreach (var (file, endKnown) in new[] { ("long.flac", false), ("long.wv", true) })
        {
            var before = Written(run);
            results.Add(new
            {
                file,
                result = LongTrack(ffmpeg, ffprobe, Path.Combine(folder, file), endKnown)
            });
            Check(Written(run).SequenceEqual(before), file + " long playback wrote no files");
        }
        return results;
    }

    private static object LongTrack(string ffmpeg, string ffprobe, string file, bool endKnown)
    {
        string name = Path.GetFileName(file);
        const long whole = 11220L * Rate, lap = MusicLiveStream.MaxFrames;
        var live = MusicLiveSource.Locate(
            new MusicTrack { id = "long-" + name, source = "file://" + file, title = name },
            ffmpeg,
            ffprobe,
            CancellationToken.None
        );
        Check(
            Math.Abs(live.Duration - 11220) < .05 && live.End.HasValue == endKnown,
            name + " lasts 3 h 7 min, end known from the file: " + live.Duration + " / " + live.End
        );
        using var stream = new MusicLiveStream(live, 2, 2048);
        Check(
            stream.Looping && stream.Frames == lap && (endKnown ? stream.Length == whole : stream.Length > whole),
            name + " plays through a looping Unity clip of 3 h 6 min 24 s, clip " + stream.Frames + ", length "
                + stream.Length
        );
        long start = lap - Rate;
        stream.Begin(start);
        WaitReady(stream);
        Check(
            stream.Track(stream.Clip(lap - 1000)) == lap - 1000,
            name + " before the wrap the played position is the clip position"
        );
        var wrap = Heard(stream, start, 2 * Rate);
        var (wrapShift, wrapDiff) = Matched(
            wrap,
            Decode(
                ffmpeg,
                file,
                "-ss",
                Stamp(start / (double)Rate - 1),
                "-t",
                "4"
            ),
            Rate
        );
        Check(
            wrapDiff < 1e-6 && Math.Abs(wrapShift) <= 1 && stream.Launches == 1 && stream.Underruns == 0,
            name + " across the end of the Unity clip the sound goes on from the same place without a restart: shift "
                + wrapShift
                + ", max diff "
                + wrapDiff
                + ", launches "
                + stream.Launches
                + ", underruns "
                + stream.Underruns
        );
        long after = stream.Track(500);
        Check(after == lap + 500, name + " after the wrap the played position counts on past 3 h 6 min 24 s: " + after);
        long far = lap + 10L * Rate;
        var (farShift, farDiff) = SeekAndMatch(stream, ffmpeg, file, far);
        Check(
            farDiff < 1e-6 && Math.Abs(farShift) <= 1 && stream.Clip(far) == 10 * Rate,
            name + " seek to 3 h 6 min 34 s plays that place: shift " + farShift + ", max diff " + farDiff
        );
        long back = lap - 5L * Rate;
        var (backShift, backDiff) = SeekAndMatch(stream, ffmpeg, file, back);
        Check(
            backDiff < 1e-6 && Math.Abs(backShift) <= 1 && stream.Clip(back) == back,
            name + " seek back to 3 h 6 min 19 s plays that place: shift " + backShift + ", max diff " + backDiff
        );
        long last = whole - Rate;
        stream.Seek(last);
        stream.SetPosition(stream.Clip(last));
        WaitReady(stream);
        Heard(stream, last, 2 * Rate);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (stream.EofFrame < 0 && watch.ElapsedMilliseconds < 2000)
            Thread.Sleep(2);
        Check(
            stream.EofFrame == whole && stream.Seconds == 11220,
            name + " the track ends at 3 h 7 min, not at 3 h 6 min 24 s: end " + stream.EofFrame + ", length "
                + stream.Seconds
        );
        return new
        {
            live.Duration,
            stream.Frames,
            stream.Length,
            wrapShift,
            wrapDiff,
            farShift,
            farDiff,
            backShift,
            backDiff,
            stream.EofFrame,
            stream.Launches
        };
    }

    private static (int shift, double diff) SeekAndMatch(MusicLiveStream stream, string ffmpeg, string file, long frame)
    {
        stream.Seek(frame);
        stream.SetPosition(stream.Clip(frame));
        WaitReady(stream);
        return Matched(
            Heard(stream, frame, Rate / 2),
            Decode(ffmpeg, file, "-ss", Stamp(frame / (double)Rate - 1), "-t", "2"),
            Rate
        );
    }

    private static float[] Heard(MusicLiveStream stream, long frame, int frames)
    {
        var heard = new float[frames * 2];
        int clip = stream.Clip(frame);
        for (int done = 0; done < frames;)
        {
            int count = Math.Min(2048, Math.Min(frames - done, stream.Frames - clip));
            var part = new float[count * 2];
            stream.Read(part);
            Array.Copy(part, 0, heard, done * 2, part.Length);
            done += count;
            clip += count;
            if (clip == stream.Frames)
            {
                clip = 0;
                stream.SetPosition(0);
            }
        }
        return heard;
    }

    private static (int shift, double diff) Matched(float[] heard, float[] reference, int offset)
    {
        int bestShift = 0;
        double best = double.MaxValue;
        for (int shift = -48; shift <= 48; shift++)
        {
            double worst = 0;
            for (int i = 0; i < heard.Length && worst < best; i++)
                worst = Math.Max(worst, Math.Abs(heard[i] - At(reference, (long)(offset + shift) * 2 + i)));
            if (worst < best)
            {
                best = worst;
                bestShift = shift;
            }
        }
        return (bestShift, best);
    }

    private static string Stamp(double seconds) => seconds.ToString("0.000000000", CultureInfo.InvariantCulture);
}
