using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingBoombox
{
    internal static class BoomboxPlaylistQueue
    {
        internal static void Append(
            BoomboxPlugin plugin,
            UserPlaylist playlist,
            IEnumerable<string> tracks
        ) => Apply(plugin, playlist, playlist.name, playlist.tracks.Concat(tracks).ToList(), index => index);

        internal static void Rename(
            BoomboxPlugin plugin,
            UserPlaylist playlist,
            string name
        ) => Apply(plugin, playlist, name, playlist.tracks.ToList(), index => index);

        internal static void Remove(BoomboxPlugin plugin, UserPlaylist playlist, int removed)
        {
            var tracks = playlist.tracks.ToList();
            tracks.RemoveAt(removed);
            Apply(
                plugin,
                playlist,
                playlist.name,
                tracks,
                index => index == removed
                    ? -1
                    : index > removed
                        ? index - 1
                        : index
            );
        }

        internal static void Move(BoomboxPlugin plugin, UserPlaylist playlist, int from, int to)
        {
            if (from == to || from < 0 || to < 0 || from >= playlist.tracks.Count || to >= playlist.tracks.Count)
                return;
            var tracks = playlist.tracks.ToList();
            string moved = tracks[from];
            tracks.RemoveAt(from);
            tracks.Insert(to, moved);
            Apply(
                plugin,
                playlist,
                playlist.name,
                tracks,
                index => MovedIndex(index, from, to)
            );
        }

        private static int MovedIndex(int index, int from, int to)
        {
            if (index == from)
                return to;
            if (from < to && index > from && index <= to)
                return index - 1;
            if (to < from && index >= to && index < from)
                return index + 1;
            return index;
        }

        private static void Apply(
            BoomboxPlugin plugin,
            UserPlaylist playlist,
            string name,
            List<string> tracks,
            Func<int, int> map
        )
        {
            var previous = playlist.tracks.ToList();
            plugin.MusicLibrary.Update(playlist, name, tracks);
            foreach (var deck in plugin.Decks.Values)
                if (deck != null && deck.State.playlistId == playlist.id && deck.State.owner == plugin.Store.PlayerId)
                    Sync(deck, previous, playlist.tracks, map);
            plugin.Save();
        }

        private static void Sync(AudioDeck deck, List<string> previous, List<string> tracks, Func<int, int> map)
        {
            var state = deck.State;
            int current = state.tracks.Count == 0
                ? 0
                : UnityEngine.Mathf.Clamp(state.trackIndex, 0, state.tracks.Count - 1);
            string playing = state.TrackId;
            int mapped = state.tracks.SequenceEqual(previous) ? map(current) : tracks.IndexOf(playing);
            deck.Capture();
            state.tracks = tracks.ToList();
            if (tracks.Count == 0)
            {
                state.trackIndex = 0;
                state.seconds = 0f;
                state.playing = false;
                deck.LoadTrack();
                return;
            }
            if (mapped >= 0 && mapped < tracks.Count && tracks[mapped] == playing)
            {
                state.trackIndex = mapped;
                return;
            }
            state.trackIndex = current % tracks.Count;
            state.seconds = 0f;
            deck.LoadTrack();
        }
    }
}
