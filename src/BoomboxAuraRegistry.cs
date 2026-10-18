using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingBoombox
{
    internal sealed class BoomboxAuraRegistry
    {
        private sealed class Batch
        {
            internal string Session;
            internal long Revision;
        }
        private sealed class Entry
        {
            internal BoomboxAuraSource Source;
            internal float Heard;
        }
        private readonly Dictionary<long, Batch> batches = new Dictionary<long, Batch>();
        private readonly Dictionary<string, Entry> sources = new Dictionary<string, Entry>(StringComparer.Ordinal);
        internal IReadOnlyList<BoomboxAuraSource> Sources => sources.Values.Select(e => e.Source).ToArray();

        internal bool Replace(long peer, string session, long revision, List<BoomboxAuraSource> incoming, float now)
        {
            if (batches.TryGetValue(peer, out var batch) && (batch.Session != session || revision <= batch.Revision))
                return false;
            foreach (var source in incoming)
            {
                if (!sources.TryGetValue(source.Id, out var current) || current.Source.Peer == peer)
                    continue;
                var item = source.WorldItem.IsNone() ? null : ZDOMan.instance?.GetZDO(source.WorldItem);
                bool transferred = item != null
                    && item.GetOwner() == peer
                    && current.Source.WorldItem == source.WorldItem;
                bool pickedUp = source.Worn
                    && !current.Source.WorldItem.IsNone()
                    && ZDOMan.instance?.GetZDO(current.Source.WorldItem) == null;
                if (!transferred && !pickedUp)
                    return false;
            }
            var ids = new HashSet<string>(incoming.Select(s => s.Id), StringComparer.Ordinal);
            foreach (var id in sources.Where(p => p.Value.Source.Peer == peer
                && !ids.Contains(p.Key)).Select(p => p.Key).ToArray())
                sources.Remove(id);
            foreach (var source in incoming)
                sources[source.Id] = new Entry { Source = source, Heard = now };
            batches[peer] = new Batch { Session = session, Revision = revision };
            return true;
        }

        internal void Prune(HashSet<long> connected, float now)
        {
            foreach (var id in sources.Where(p => !connected.Contains(p.Value.Source.Peer)
                || now - p.Value.Heard > 1.25f).Select(
                p => p.Key
            ).ToArray())
                sources.Remove(id);
            foreach (long peer in batches.Keys.Where(p => !connected.Contains(p)).ToArray())
                batches.Remove(peer);
        }
    }
}
