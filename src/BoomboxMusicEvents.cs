using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxMusicEvents
    {
        private sealed class Event
        {
            internal BoomboxMusicHit Hit;
            internal readonly HashSet<ZDOID> Targets = new HashSet<ZDOID>();
        }
        private sealed class Window
        {
            internal string Epoch;
            internal long Floor;
            internal readonly Dictionary<long, Event> Events = new Dictionary<long, Event>();
        }
        private readonly Dictionary<long, Window> windows = new Dictionary<long, Window>();

        internal bool Begin(BoomboxMusicHit hit)
        {
            if (!windows.TryGetValue(hit.Peer, out var window))
                windows[hit.Peer] = window = new Window { Epoch = hit.Epoch };
            if (window.Epoch != hit.Epoch || hit.Sequence <= window.Floor)
                return false;
            if (window.Events.ContainsKey(hit.Sequence))
                return false;
            Prune(window);
            if (hit.Sequence <= window.Floor)
                return false;
            window.Events[hit.Sequence] = new Event { Hit = hit };
            return true;
        }

        internal BoomboxMusicHit Find(long peer, string epoch, long sequence, string source)
        {
            if (!windows.TryGetValue(peer, out var window) || window.Epoch != epoch
            || !window.Events.TryGetValue(sequence, out var value)
            || value.Hit.Source != source
            || Time.realtimeSinceStartup - value.Hit.Created > 10f)
                return null;
            return value.Hit;
        }

        internal bool Target(
            BoomboxMusicHit hit
        ) => windows.TryGetValue(hit.Peer, out var window) && window.Epoch == hit.Epoch
        && window.Events.TryGetValue(hit.Sequence, out var value)
        && value.Hit.Source == hit.Source
        && value.Targets.Add(hit.Target);
        internal bool ContainsTarget(
            BoomboxMusicHit hit
        ) => windows.TryGetValue(hit.Peer, out var window) && window.Epoch == hit.Epoch
        && window.Events.TryGetValue(hit.Sequence, out var value)
        && value.Hit.Source == hit.Source
        && value.Targets.Contains(hit.Target);
        internal void Cancel(long peer, IReadOnlyList<BoomboxAuraSource> sources)
        {
            if (!windows.TryGetValue(peer, out var window))
                return;
            foreach (var value in window.Events.Values)
                if (!sources.Any(source => source.Id == value.Hit.Source && source.Active))
                    value.Hit.Cancelled = true;
        }
        internal void Prune(HashSet<long> peers)
        {
            foreach (long peer in windows.Keys.Where(p => !peers.Contains(p)).ToArray())
                windows.Remove(peer);
            foreach (var window in windows.Values)
                Prune(window);
        }
        private static void Prune(Window window)
        {
            foreach (long sequence in window.Events.Where(p => Time.realtimeSinceStartup - p.Value.Hit.Created > 10f).Select(p => p.Key).ToArray())
            {
                window.Events.Remove(sequence);
                window.Floor = System.Math.Max(window.Floor, sequence);
            }
            while (window.Events.Count >= 4096)
            {
                long sequence = window.Events.Keys.Min();
                window.Events.Remove(sequence);
                window.Floor = System.Math.Max(window.Floor, sequence);
            }
        }
    }
}
