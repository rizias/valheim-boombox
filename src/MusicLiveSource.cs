using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace VikingBoombox
{
    public sealed class MusicLiveSource
    {
        private const double PreRollSeconds = .5;
        public string FFmpeg;
        public string File;
        public string ZipPath;
        public string ZipMember;
        public double Start;
        public double? End;
        public bool SeekFromStart;
        internal MusicWavPack WavPack;
        public double Duration;
        public string Title;
        public string Artist;

        public bool Zipped => ZipPath != null;

        public void CopyTo(Stream destination)
        {
            using (var input = System.IO.File.OpenRead(ZipPath))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            using (var stream = MusicSource.Entry(archive, ZipMember).Open())
                stream.CopyTo(destination, 81920);
        }

        public static MusicLiveSource Locate(
            MusicTrack track,
            string ffmpeg,
            string ffprobe,
            CancellationToken cancellation
        )
        {
            cancellation.ThrowIfCancellationRequested();
            var live = MusicSource.Parse(track.source).Locate(track.subsong, out var cue);
            live.FFmpeg = ffmpeg;
            if (!live.Zipped && Path.GetExtension(live.File).Equals(".wv", StringComparison.OrdinalIgnoreCase))
                live.WavPack = MusicWavPack.Inspect(live.File);
            string[] options = live.WavPack != null && live.WavPack.Embedded
                ? live.WavPack.Input(live.WavPack.Start)
                : null;
            var probe = live.Zipped
                ? MusicProcess.Probe(live.ZipMember, live.CopyTo, null, ffprobe, cancellation)
                : MusicProcess.Probe(live.File, null, options, ffprobe, cancellation);
            live.SeekFromStart = live.Zipped || (string)probe["format"]?["format_name"] == "asf";
            if (live.WavPack != null)
                live.WavPack.Rate = SampleRate(probe);
            var tags = MusicTags.Metadata(probe);
            if (cue == null && track.subsong.GetValueOrDefault() != 0)
                cue = MusicCue.Select(
                    (live.WavPack?.Cue != null
                        ? MusicCue.Parse(live.WavPack.Cue)
                        : MusicTags.Embedded(tags)) ?? throw new InvalidDataException(BoomboxText.Get("text.the_audio_file_contains_no_cuesheet")),
                    track.subsong
                );
            live.Start = cue?.Start ?? 0;
            live.End = cue?.End;
            if (!live.End.HasValue && live.WavPack != null && live.WavPack.Samples >= 0)
                live.End = live.WavPack.Samples / live.WavPack.Rate;
            double whole = live.End.HasValue ? 0 : Whole(probe);
            if (!live.End.HasValue && !(whole > 0))
                whole = live.Zipped
                    ? MusicProcess.PacketSeconds(live.ZipMember, live.CopyTo, null, ffprobe, cancellation)
                    : MusicProcess.PacketSeconds(live.File, null, options, ffprobe, cancellation);
            live.Duration = live.End.HasValue ? live.End.Value - live.Start : whole - live.Start;
            if (double.IsNaN(live.Duration) || double.IsInfinity(live.Duration) || live.Duration <= 0)
                throw new InvalidDataException(BoomboxText.Get("text.could_not_determine_the_track_duration"));
            live.Title = cue?.Title ?? (tags.TryGetValue("title", out var title) ? title : track.title);
            live.Artist = cue?.Artist ?? (tags.TryGetValue("artist", out var artist) ? artist : track.artist);
            return live;
        }

        public string[] Decoding(double at, out double from, out Action<Stream> input)
        {
            var arguments = new List<string> { "-hide_banner", "-loglevel", "error" };
            input = Zipped ? new Action<Stream>(CopyTo) : null;
            if (!Zipped)
                arguments.Add("-nostdin");
            if (WavPack != null)
            {
                var block = WavPack.Seek(File, Math.Max(0, at - PreRollSeconds));
                from = block.Seconds;
                arguments.AddRange(WavPack.Input(block.Offset));
                arguments.AddRange(new[] { "-i", File });
            }
            else
            {
                from = SeekFromStart ? 0 : Math.Max(0, at - PreRollSeconds);
                if (from > 0)
                {
                    arguments.Add("-ss");
                    arguments.Add(Stamp(from));
                }
                arguments.AddRange(new[] { "-i", Zipped ? "pipe:0" : File });
                if (End.HasValue)
                {
                    arguments.Add("-t");
                    arguments.Add(Stamp(Math.Max(.001, End.Value - from)));
                }
            }
            arguments.AddRange(new[] {
                "-map",
                "0:a:0",
                "-vn",
                "-sn",
                "-dn",
                "-ac",
                "2",
                "-ar",
                MusicLiveStream.Frequency.ToString(CultureInfo.InvariantCulture),
                "-f",
                "f32le",
                "pipe:1"
                });
            return arguments.ToArray();
        }

        private static string Stamp(double value) => value.ToString("0.000000000", CultureInfo.InvariantCulture);

        private static double SampleRate(JObject probe)
        {
            var stream = probe["streams"].First(s => (string)s["codec_type"] == "audio");
            if (!double.TryParse(
                (string)stream["sample_rate"],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double rate
            )
            || !(rate > 0))
                throw new InvalidDataException(BoomboxText.Get("text.could_not_determine_the_wavpack_sample_rate"));
            return rate;
        }

        private static double Whole(JObject probe)
        {
            var stream = probe["streams"].First(s => (string)s["codec_type"] == "audio");
            string text = (string)stream["duration"];
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                double.TryParse(
                    (string)probe["format"]?["duration"],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out duration
                );
            return duration;
        }
    }
}
