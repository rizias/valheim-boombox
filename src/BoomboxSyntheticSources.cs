using System;
using UnityEngine;

namespace VikingBoombox
{
    public static class BoomboxSyntheticTrackId
    {
        public const string GameMusic = "system:gamemusic";
        public const string ComputerAudio = "system:computer-audio";
        private const string RhythmPrefix = "system:rhythm:";
        private static readonly int[] RhythmBpm = { 80, 100, 120, 140 };
        public static readonly string[] All = {
            ComputerAudio,
            GameMusic,
            Rhythm(80),
            Rhythm(100),
            Rhythm(120),
            Rhythm(140)
        };

        public static string Rhythm(int bpm) => RhythmPrefix + bpm;

        public static bool TryRhythmBpm(string id, out int bpm)
        {
            bpm = 0;
            if (id == null || !id.StartsWith(RhythmPrefix, StringComparison.Ordinal))
                return false;
            return int.TryParse(id.Substring(RhythmPrefix.Length), out bpm) && Array.IndexOf(RhythmBpm, bpm) >= 0;
        }

        public static bool IsGameMusic(string id) => id == GameMusic;
        public static bool IsSynthetic(string id) => id == ComputerAudio || IsGameMusic(id) || TryRhythmBpm(id, out _);
        public static double IntervalSeconds(int bpm) => 60.0 / bpm;

        public static MusicTrack For(string id)
        {
            if (id == ComputerAudio)
                return new MusicTrack
                {
                    id = id,
                    title = BoomboxText.Get("text.computer_audio"),
                    source = id,
                    available = true
                };
            if (TryRhythmBpm(id, out int bpm))
                return new MusicTrack
                {
                    id = id,
                    title = BoomboxText.Get("text.rhythm") + bpm,
                    source = id,
                    available = true
                };
            if (IsGameMusic(id))
                return new MusicTrack
                {
                    id = id,
                    title = BoomboxText.Get("text.game_music"),
                    source = id,
                    available = true
                };
            return null;
        }
    }

    internal sealed class BoomboxSyntheticBeatClock
    {
        private double nextBeatAt = -1;

        public void Reset() => nextBeatAt = -1;

        public void Advance(MusicEffectFeed feed, string trackId, double threshold)
        {
            if (BoomboxSyntheticTrackId.TryRhythmBpm(trackId, out int bpm))
                AdvanceRhythm(feed, bpm);
            else if (BoomboxSyntheticTrackId.IsGameMusic(trackId))
            {
                var tap = BoomboxGameMusic.Tap();
                if (tap != null)
                    feed.Tap(tap, threshold);
            }
        }

        private void AdvanceRhythm(MusicEffectFeed feed, int bpm)
        {
            double interval = BoomboxSyntheticTrackId.IntervalSeconds(bpm);
            double now = Time.unscaledTimeAsDouble;
            feed.Steady(now);
            if (nextBeatAt < 0)
            {
                nextBeatAt = now + interval;
                return;
            }
            if (now < nextBeatAt)
                return;
            feed.Beat(now, 1f);
            nextBeatAt += interval;
            if (nextBeatAt < now)
                nextBeatAt = now + interval;
        }
    }
}
