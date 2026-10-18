using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    public enum BoomboxGameMusicMode
    {
        Normal, Quiet, Off
    }

    public static class BoomboxGameMusic
    {
        private sealed class AppliedVolume
        {
            public float Original;
            public float Applied;
        }
        private static readonly Dictionary<AudioSource, AppliedVolume> sources =
            new Dictionary<AudioSource, AppliedVolume>();
        private static readonly List<AudioSource> deadSources = new List<AudioSource>();
        private static readonly HashSet<AudioSource> tracked = new HashSet<AudioSource>();
        private static ConfigFile configuration;
        private static ConfigEntry<BoomboxGameMusicMode> mode;
        private static float factor = 1f;
        private static int frame = -1;
        public static float Factor => factor;
        public static BoomboxGameMusicMode Mode
        {
            get
            {
                Bind();
                return mode?.Value ?? BoomboxGameMusicMode.Off;
            }
            set
            {
                Bind();
                if (mode != null)
                    mode.Value = value;
            }
        }

        private static void Bind()
        {
            var plugin = BoomboxPlugin.Instance;
            if (plugin == null || configuration == plugin.Config)
                return;
            configuration = plugin.Config;
            mode =
                configuration.Bind(
                "Audio",
                "GameMusic",
                BoomboxGameMusicMode.Off,
                BoomboxText.Description("text.native_game_music_while_your_boombox_plays")
            );
        }

        private static bool Playing()
        {
            var plugin = BoomboxPlugin.Instance;
            var player = Player.m_localPlayer;
            if (plugin == null
                || !plugin.isActiveAndEnabled
                || !plugin.AudioWorldActive
                || player == null
                || player.IsDead()
                || Time.timeScale <= 0f
                || AudioListener.pause)
                return false;
            foreach (var deck in plugin.Decks.Values)
            {
                if (deck == null
                    || !deck.isActiveAndEnabled
                    || deck.State == null
                    || deck.State.owner != player.GetPlayerID()
                    || !deck.State.playing
                    ||
                deck.StoragePaused
                    || deck.State.insertedCassette == null
                    || deck.Source == null
                    || deck.State.volume <= 0f)
                    continue;
                if (Vector3.Distance(player.transform.position, deck.transform.position) > deck.Source.maxDistance)
                    continue;
                if (deck.IsActivePlayback || (deck.Loading && deck.State.TrackId != null))
                    return true;
            }
            return false;
        }

        private static void Advance()
        {
            if (frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            float target = Playing()
                ? (Mode == BoomboxGameMusicMode.Off
                    ? 0f
                    : Mode == BoomboxGameMusicMode.Quiet
                        ? .2f
                        : 1f)
                : 1f;
            factor = Mathf.MoveTowards(factor, target, Mathf.Min(Time.unscaledDeltaTime, .1f) * 2f);
            deadSources.Clear();
            foreach (var entry in sources)
                if (entry.Key == null)
                    deadSources.Add(entry.Key);
            foreach (var source in deadSources)
                sources.Remove(source);
            tracked.RemoveWhere(source => source == null);
        }

        private static AudioSource FirstAudibleSource()
        {
            foreach (var source in tracked)
                if (source != null && source.isPlaying && source.volume > 0f)
                {
                    return source;
                }
            return null;
        }

        public static bool ReadOutput(float[] buffer)
        {
            var source = FirstAudibleSource();
            if (source == null)
                return false;
            source.GetOutputData(buffer, 0);
            return true;
        }

        internal static BoomboxMusicTap Tap()
        {
            var source = FirstAudibleSource();
            if (source == null)
                return null;
            var tap = source.GetComponent<BoomboxMusicTap>();
            return tap != null ? tap : source.gameObject.AddComponent<BoomboxMusicTap>();
        }

        public static void BeforeNativeUpdate(AudioSource source)
        {
            if (source == null || !sources.TryGetValue(source, out var previous))
                return;
            if (Mathf.Approximately(source.volume, previous.Applied))
                source.volume = previous.Original;
            sources.Remove(source);
        }

        public static void AfterNativeUpdate(AudioSource source)
        {
            Advance();
            if (source != null)
                tracked.Add(source);
            if (source == null || factor >= 1f)
                return;
            float original = source.volume;
            float applied = original * factor;
            sources[source] = new AppliedVolume { Original = original, Applied = applied };
            source.volume = applied;
        }

        public static void Release()
        {
            foreach (var entry in sources)
                if (entry.Key != null && Mathf.Approximately(entry.Key.volume, entry.Value.Applied))
                    entry.Key.volume = entry.Value.Original;
            sources.Clear();
            tracked.Clear();
            factor = 1f;
            frame = -1;
        }
    }

    [HarmonyPatch(typeof(MusicMan), "UpdateMusic")]
    internal static class BoomboxNativeMusicVolume
    {
        private static void Prefix(
            AudioSource ___m_musicSource
        ) => BoomboxGameMusic.BeforeNativeUpdate(___m_musicSource);
        private static void Postfix(
            AudioSource ___m_musicSource
        ) => BoomboxGameMusic.AfterNativeUpdate(___m_musicSource);
    }

    [HarmonyPatch(typeof(MusicLocation), "Update")]
    internal static class BoomboxLocationMusicVolume
    {
        private static void Prefix(
            AudioSource ___m_audioSource
        ) => BoomboxGameMusic.BeforeNativeUpdate(___m_audioSource);
        private static void Postfix(
            AudioSource ___m_audioSource
        ) => BoomboxGameMusic.AfterNativeUpdate(___m_audioSource);
    }
}
