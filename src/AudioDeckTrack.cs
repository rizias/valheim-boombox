using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class AudioDeck
    {
        private void OnAudioChanged(bool deviceWasChanged) => audioChanged = true;

        public void LoadTrack()
        {
            CancelPreparation();
            preparation = new CancellationTokenSource();
            int marker = ++generation;
            Source.Stop();
            bool released = ReleaseStream();
            buffering = false;
            pendingSeek = null;
            if (Source.clip != null)
                Destroy(Source.clip);
            Source.clip = null;
            started = false;
            wasPlaying = false;
            preparedTitle = null;
            preparedArtist = null;
            ForgetMembranes();
            feed.Reset();
            synthetic.Reset();
            if (!released)
            {
                Fail(BoomboxText.Get("error.stream_shutdown_timeout"));
                return;
            }
            if (State.insertedCassette == null)
            {
                State.playing = false;
                SetStatus("text.the_boombox_is_not_ready_for_playback_yet");
                Loading = false;
                return;
            }
            if (State.TrackId == null)
            {
                SetStatus("text.no_track_selected");
                Loading = false;
                return;
            }
            if (!State.playing)
            {
                SetStatus("text.press_play_to_prepare_the_track");
                Loading = false;
                return;
            }
            if (BoomboxSyntheticTrackId.IsSynthetic(State.TrackId))
            {
                SetStatus("text.ready");
                Loading = false;
                return;
            }
            opening = StartCoroutine(Open(marker, State.TrackId, preparation.Token));
        }

        private IEnumerator Open(int marker, string trackId, CancellationToken cancellation)
        {
            Loading = true;
            SetStatus("text.preparing_track");
            var plugin = BoomboxPlugin.Instance;
            var retiring = Stopwatch.StartNew();
            while (preparationTask != null && !preparationTask.IsCompleted)
            {
                if (marker != generation || cancellation.IsCancellationRequested)
                    yield break;
                if (retiring.ElapsedMilliseconds >= MusicProcess.ShutdownMilliseconds)
                {
                    Fail(BoomboxText.Get("error.previous_track_cleanup_pending"));
                    yield break;
                }
                yield return null;
            }
            if (marker != generation || cancellation.IsCancellationRequested)
                yield break;
            var task = Task.Run(() => MusicPreparation.Locate(plugin, trackId, cancellation), cancellation);
            preparationTask = task;
            MusicProcess.Observe(task);
            while (!task.IsCompleted)
                yield return null;
            if (marker != generation)
                yield break;
            if (task.IsCanceled)
            {
                Loading = false;
                yield break;
            }
            if (task.IsFaulted)
            {
                Fail(task.Exception.GetBaseException().Message);
                yield break;
            }
            var located = task.Result;
            preparedTitle = located.Title;
            preparedArtist = located.Artist;
            plugin.UpdateTrackMetadata(trackId, located.Title, located.Artist);
            long resumeFrame;
            try
            {
                AudioSettings.GetDSPBufferSize(out int bufferFrames, out int bufferCount);
                long frames = MusicLiveStream.FramesOf(located.Duration);
                resumeFrame = Math.Min(
                    Math.Max(0, FrameAt(State.seconds)),
                    Math.Max(
                        0,
                        frames - MusicLiveStream.Frequency / 50
                    )
                );
                stream = new MusicLiveStream(
                    located,
                    plugin.Settings.AudioBufferSeconds.Value,
                    checked(bufferFrames * bufferCount)
                );
            }
            catch (Exception error)
            {
                Fail(error.Message);
                yield break;
            }
            yield return Attach(marker, trackId, resumeFrame);
        }

        private IEnumerator Attach(int marker, string trackId, long frame)
        {
            if (marker != generation || stream == null)
                yield break;
            State.seconds = frame / (float)MusicLiveStream.Frequency;
            frame = FrameAt(State.seconds);
            int clipFrame = stream.Clip(frame);
            if (clipFrame == 0)
            {
                stream.Begin(frame);
                while (marker == generation && stream != null && stream.Error == null && !stream.Ready)
                    yield return null;
                if (marker != generation || stream == null || stream.Error != null)
                    yield break;
            }
            try
            {
                Source.clip = AudioClip.Create(
                    trackId,
                    stream.Frames,
                    MusicLiveStream.Channels,
                    MusicLiveStream.Frequency,
                    true,
                    stream.Read,
                    stream.SetPosition
                );
                Source.clip.name = trackId;
                Source.loop = stream.Looping;
                if (clipFrame > 0)
                {
                    stream.Begin(frame);
                    Source.timeSamples = clipFrame;
                }
            }
            catch (Exception error)
            {
                ReleaseStream();
                if (Source.clip != null)
                {
                    Destroy(Source.clip);
                    Source.clip = null;
                }
                Fail(error.Message);
                yield break;
            }
            buffering = true;
        }

        private void Revive()
        {
            audioChanged = false;
            desktopAudio?.ResetDevice();
            if (Source.clip == null)
                return;
            long frame = FrameAt(Resumable(State.seconds));
            string trackId = Source.clip.name;
            Source.Stop();
            Destroy(Source.clip);
            Source.clip = null;
            stream.Hold();
            feed.Reset();
            started = wasPlaying = false;
            if (opening != null)
                StopCoroutine(opening);
            opening = StartCoroutine(Attach(generation, trackId, frame));
        }

        private void CancelPreparation()
        {
            if (opening != null)
            {
                StopCoroutine(opening);
                opening = null;
            }
            var previous = preparation;
            preparation = null;
            if (previous == null)
                return;
            previous.Cancel();
            if (preparationTask == null || preparationTask.IsCompleted)
                previous.Dispose();
            else
                preparationTask.ContinueWith(
                done =>
                    {
                        var ignored = done.Exception;
                        previous.Dispose();
                    },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
                );
        }

        private bool ReleaseStream()
        {
            if (retiringStream != null)
            {
                if (!retiringStream.Stopped)
                    return false;
                retiringStream = null;
            }
            var previous = stream;
            stream = null;
            if (previous == null)
                return true;
            previous.Dispose();
            if (previous.CleanupError != null)
                BoomboxPlugin.Instance?.LogError(previous.CleanupError);
            if (previous.Stopped)
                return true;
            retiringStream = previous;
            return false;
        }

        private float Resumable(float seconds) => Mathf.Max(
            0f,
            stream.EndKnown
                ? Mathf.Min(seconds, TrackSeconds - .02f)
                : seconds
        );

        private static long FrameAt(float seconds) => (long)Math.Round(seconds * (double)MusicLiveStream.Frequency);

        private long Played() => stream.Track(Source.timeSamples);

        private void Fail(string error)
        {
            Loading = false;
            State.playing = false;
            SetStatus("status.playback_error", error);
            BoomboxPlugin.Instance.LogError(Status);
        }
    }
}
