using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VikingBoombox
{
    internal sealed class MusicCue
    {
        public int Number;
        public string File;
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public string AlbumArtist = "";
        public int DiscNumber;
        public double? Start;
        public double? End;

        public static List<MusicCue> Parse(byte[] bytes)
        {
            string text;
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254)
                text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255)
                text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            else
            {
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                }
                catch (DecoderFallbackException)
                {
                    text = Windows1251(bytes);
                }
            }
            return Parse(text);
        }

        public static List<MusicCue> Parse(string text)
        {
            var tracks = new List<MusicCue>();
            MusicCue current = null;
            string file = "", album = "", artist = "";
            int discNumber = 0;
            foreach (string line in text.Split('\n'))
            {
                var parts = Regex.Split(line.Trim(), "\\s+", RegexOptions.None).ToArray();
                if (parts.Length < 2)
                    continue;
                string command = parts[0].ToUpperInvariant();
                string value = line.Trim().Substring(parts[0].Length).Trim();
                if (command == "FILE")
                {
                    var match = Regex.Match(value, "^(?:\"([^\"]+)\"|(.+?))\\s+\\S+$");
                    if (!match.Success)
                        throw new InvalidDataException(BoomboxText.Get("text.invalid_file_in_cue") + value);
                    file = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                }
                else if (command == "TRACK")
                {
                    if (parts.Length != 3 || !parts[2].Equals("AUDIO", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(BoomboxText.Get("text.cue_contains_a_non_audio_track"));
                    current = new MusicCue
                    {
                        Number = int.Parse(parts[1], CultureInfo.InvariantCulture),
                        File = file,
                        Artist = artist,
                        Album = album,
                        AlbumArtist = artist,
                        DiscNumber = discNumber
                    };
                    tracks.Add(current);
                }
                else if (command == "TITLE" || command == "PERFORMER")
                {
                    value = value.Trim('"');
                    if (current == null)
                    {
                        if (command == "TITLE")
                            album = value;
                        else
                            artist = value;
                    }
                    else if (command == "TITLE")
                        current.Title = value;
                    else
                        current.Artist = value;
                }
                else if (command == "REM"
                    && parts.Length >= 3
                    && parts[1].Equals("DISCNUMBER", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(parts[2].Trim('"'), out discNumber);
                else if (command == "INDEX" && current != null && parts.Length == 3 && parts[1] == "01")
                    current.Start = Seconds(parts[2]);
            }
            if (tracks.Count == 0)
                throw new InvalidDataException(BoomboxText.Get("text.cue_contains_no_audio_tracks"));
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (string.IsNullOrEmpty(track.File) || track.Start == null)
                    throw new InvalidDataException(BoomboxText.Get("text.cue_track_is_missing_file_or_index_01") + track.Number);
                if (track.Title.Length == 0)
                    track.Title = BoomboxText.Format("text.cue_fallback_title", album.Length > 0
                        ? album
                        : Path.GetFileNameWithoutExtension(track.File), track.Number);
                if (i + 1 < tracks.Count && tracks[i + 1].File == track.File)
                {
                    track.End = tracks[i + 1].Start;
                    if (track.End == null || track.End <= track.Start)
                        throw new InvalidDataException(BoomboxText.Get("text.invalid_boundary_for_cue_track") + track.Number);
                }
            }
            return tracks;
        }

        public static MusicCue Select(
            IEnumerable<MusicCue> tracks,
            int? number
        ) => tracks.FirstOrDefault(t => t.Number == number)
            ?? throw new InvalidDataException(BoomboxText.Get("text.cue_contains_no_track") + number);

        private static string Windows1251(byte[] bytes)
        {
            const string extended =
                "\u0402\u0403\u201a\u0453\u201e\u2026\u2020\u2021\u20ac\u2030\u0409\u2039\u040a\u040c\u040b\u040f\u0452\u2018\u2019\u201c\u201d\u2022\u2013\u2014\u0098\u2122\u0459\u203a\u045a\u045c\u045b\u045f\u00a0\u040e\u045e\u0408\u00a4\u0490\u00a6\u00a7\u0401\u00a9\u0404\u00ab\u00ac\u00ad\u00ae\u0407\u00b0\u00b1\u0406\u0456\u0491\u00b5\u00b6\u00b7\u0451\u2116\u0454\u00bb\u0458\u0405\u0455\u0457";
            var text = new StringBuilder(bytes.Length);
            foreach (byte value in bytes)
            {
                if (value == 0x98)
                    throw new DecoderFallbackException(BoomboxText.Get("text.invalid_windows_1251_character_in_cue"));
                text.Append(value < 128
                    ? (char)value
                    : value >= 192
                        ? (char)(0x410 + value - 192)
                        : extended[value - 128]);
            }
            return text.ToString();
        }

        private static double Seconds(string value)
        {
            var match = Regex.Match(value, "^(\\d+):(\\d{2}):(\\d{2})$");
            if (!match.Success)
                throw new InvalidDataException(BoomboxText.Get("text.invalid_cue_time") + value);
            int minutes = int.Parse(match.Groups[1].Value),
                seconds = int.Parse(match.Groups[2].Value),
                frames = int.Parse(match.Groups[3].Value);
            if (seconds >= 60 || frames >= 75)
                throw new InvalidDataException(BoomboxText.Get("text.invalid_cue_time") + value);
            return minutes * 60d + seconds + frames / 75d;
        }
    }
}
