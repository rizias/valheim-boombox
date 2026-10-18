using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VikingBoombox
{
    internal static class MusicPreparation
    {
        public static MusicLiveSource Locate(BoomboxPlugin plugin, string trackId, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var track = plugin.FindTrack(trackId);
            if (track == null)
                throw new InvalidOperationException(BoomboxText.Get("text.track_missing_from_catalog") + trackId);
            if (string.IsNullOrEmpty(track.source) && !string.IsNullOrEmpty(track.path))
            {
                string readyPath = Path.IsPathRooted(track.path)
                    ? track.path
                    : Path.Combine(Path.GetDirectoryName(plugin.CatalogPath.Value), track.path);
                track = new MusicTrack
                {
                    id = track.id,
                    title = track.title,
                    artist = track.artist,
                    source = "file://" + Path.GetFullPath(readyPath)
                };
            }
            return MusicLiveSource.Locate(track, plugin.FFmpegPath.Value, plugin.FFprobePath.Value, cancellation);
        }

        public static IEnumerator Refresh(BoomboxPlugin plugin, CancellationToken cancellation)
        {
            plugin.CatalogRefreshing = true;
            try
            {
                plugin.MusicLibrary.Save();
                plugin.Notify("text.scanning_selected_music_folders");
                var folders = plugin.MusicLibrary.folders.ToArray();
                var previous = plugin.Catalog;
                string ffprobe = plugin.FFprobePath.Value;
                var task = Task.Run(() => MusicScanner.Scan(folders, previous, ffprobe, cancellation), cancellation);
                while (!task.IsCompleted)
                    yield return null;
                if (cancellation.IsCancellationRequested || task.IsCanceled)
                    yield break;
                if (task.IsFaulted)
                {
                    plugin.Notify("notice.scan_error", task.Exception.GetBaseException().Message);
                    plugin.LogError(plugin.Notice);
                }
                else
                {
                    StateStore.AtomicWrite(plugin.CatalogPath.Value, task.Result);
                    plugin.ReloadCatalog();
                }
            }
            finally
            {
                plugin.CompleteCatalogRefresh();
            }
        }

    }
}
