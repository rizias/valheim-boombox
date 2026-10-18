using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class AudioDeck : MonoBehaviour
    {
        private const double EndToleranceSeconds = .025;
        private const float GlowGain = 3f;
        private const float GlowShare = .3f;
        public DeckState State;
        public AudioSource Source;
        public GameObject Visual;
        public BoomboxDisplay Display;
        public BoomboxMount Mount;
        public BoomboxAmplifier Amplifier;
        public BoomboxPlatformAnchor PlatformAnchor;
        public BoomboxTrophyVisuals TrophyVisuals;
        public BoomboxVisualPresenter VisualPresenter;
        public ItemDrop.ItemData Item;
        public ItemDrop WorldDrop;
        public bool StoragePaused;
        private BoomboxMessage status = new BoomboxMessage("text.no_track_selected");
        public string Status
        {
            get => status.ToString();
            set => status = BoomboxMessage.Raw(value);
        }
        public void SetStatus(string key, params object[] arguments) => status = new BoomboxMessage(key, arguments);
        public bool Loading;
        public int EmittedWaves;
        public double LastBeatTime
        {
            get;
            private set;
        }
        public float LastBeatStrength
        {
            get;
            private set;
        }
        public string TrackArtist => string.IsNullOrEmpty(preparedArtist)
            ? BoomboxPlugin.Instance.FindTrack(State.TrackId)?.artist
            : preparedArtist;
        public bool IsActivePlayback => State.insertedCassette != null
            && !StoragePaused
            && Source != null
            && Source.isPlaying
            && State.playing
            && !Loading
            && !simulationPaused
            && Time.timeScale > 0f
            && !AudioListener.pause
            && BoomboxPlugin.Instance != null
            && BoomboxPlugin.Instance.AudioWorldActive;
        public bool IsSyntheticActive => BoomboxSyntheticTrackId.IsSynthetic(State.TrackId)
            && State.insertedCassette != null
            && !StoragePaused
            && State.playing
            && !Loading
            && !simulationPaused
            && Time.timeScale > 0f
            && !AudioListener.pause
            && BoomboxPlugin.Instance != null
            && BoomboxPlugin.Instance.AudioWorldActive;
        public string TrackTitle => string.IsNullOrEmpty(preparedTitle)
            ? BoomboxPlugin.Instance.FindTrack(State.TrackId)?.title
            : preparedTitle;
        public string TrackLabel => string.IsNullOrEmpty(preparedTitle)
            ? BoomboxPlugin.Instance.FindTrack(State.TrackId)?.Label
            : string.IsNullOrEmpty(preparedArtist)
                ? preparedTitle
                : preparedArtist + " — " + preparedTitle;
        private int generation;
        private CancellationTokenSource preparation;
        private Task<MusicLiveSource> preparationTask;
        private Coroutine opening;
        private const double MembranePushSeconds = .15;
        private float bass;
        private float membraneLevel;
        private double membranePushUntil = double.NegativeInfinity;
        private float glowLevel;
        private readonly BoomboxMembraneMeter membranes = new BoomboxMembraneMeter();
        private bool simulationPaused;
        private bool started;
        private bool wasPlaying;
        private bool audioChanged;
        private BoomboxDesktopAudio desktopAudio;
        private long playedFrame;
        private double playedDsp;
        private string preparedTitle;
        private string preparedArtist;
        private int seekSettledFrame;
        private MusicLiveStream stream;
        private MusicLiveStream retiringStream;
        private bool buffering;
        private float? pendingSeek;
        private readonly BoomboxSyntheticBeatClock synthetic = new BoomboxSyntheticBeatClock();
        public long AudioBufferBytes => stream?.BufferBytes ?? 0;
        public MusicLiveStream Stream => stream;
        public float VisualStrength => membraneLevel;
        public float GlowStrength => glowLevel;
        public float TrackSeconds => stream != null ? (float)stream.Seconds : 0f;

        public void Initialize(DeckState state)
        {
            State = state;
            Visual = VikingBoomboxVisual.Create(transform);
            Display = gameObject.AddComponent<BoomboxDisplay>();
            Display.Initialize();
            Mount = gameObject.AddComponent<BoomboxMount>();
            Mount.Initialize();
            Source = gameObject.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.spatialBlend = state.onBack ? 0f : 1f;
            Source.reverbZoneMix = 0f;
            Source.bypassReverbZones = true;
            Source.dopplerLevel = 0f;
            Source.rolloffMode = AudioRolloffMode.Linear;
            Source.minDistance = 2f;
            Source.maxDistance = BoomboxPlugin.Instance.SoundDistance.Value;
            Source.spread = 35f;
            Source.priority = 80;
            Source.volume = state.muted ? 0f : state.volume;
            desktopAudio = gameObject.AddComponent<BoomboxDesktopAudio>();
            ConnectMixer();
            Amplifier = gameObject.AddComponent<BoomboxAmplifier>();
            Amplifier.Initialize(this);
            PlatformAnchor = gameObject.AddComponent<BoomboxPlatformAnchor>();
            PlatformAnchor.Initialize(this);
            VisualPresenter = gameObject.AddComponent<BoomboxVisualPresenter>();
            VisualPresenter.Initialize(State, Visual);
            TrophyVisuals = VisualPresenter.Decorations;
            gameObject.AddComponent<BoomboxEnchantment>().Initialize(this);
            gameObject.AddComponent<BoomboxBoatHelp>().Initialize(this);
            effect = gameObject.AddComponent<MusicEffectRunner>();
            effect.Initialize(this);
            AudioSettings.OnAudioConfigurationChanged += OnAudioChanged;
            if (state.TrackId != null)
                LoadTrack();
        }

        public void TogglePlayback()
        {
            if (!State.playing && State.insertedCassette == null)
            {
                SetStatus("text.insert_a_cassette_2");
                BoomboxPlugin.Instance.Notice = Status;
                return;
            }
            Capture();
            State.playing = !State.playing;
            if (Source.clip != null)
            {
                if (State.playing)
                    PlayAtSavedPosition();
                else
                    Source.Pause();
            }
            else if (State.playing && !Loading)
                LoadTrack();
            BoomboxPlugin.Instance.Save();
        }

        public void SelectTrack(int index)
        {
            if (State.tracks.Count == 0)
                return;
            State.trackIndex = (index % State.tracks.Count + State.tracks.Count) % State.tracks.Count;
            State.seconds = 0f;
            LoadTrack();
            BoomboxPlugin.Instance.Save();
        }

        public bool Seek(float seconds)
        {
            if (Source == null
                || Source.clip == null
                || stream == null
                || Loading
                || float.IsNaN(seconds)
                || float.IsInfinity(seconds))
                return false;
            float position = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, TrackSeconds - .02f));
            Source.Pause();
            buffering = true;
            pendingSeek = position;
            stream.Seek(FrameAt(position));
            State.seconds = position;
            seekSettledFrame = Time.frameCount + 2;
            bass = 0f;
            ForgetMembranes();
            feed.Reset();
            LastBeatTime = -1;
            if (!State.playing || StoragePaused || simulationPaused || Time.timeScale <= 0 || AudioListener.pause)
                Source.Pause();
            BoomboxPlugin.Instance.Save();
            return true;
        }

        private void PlayAtSavedPosition()
        {
            if (Loading || Source.clip == null)
                return;
            if (State.insertedCassette == null || StoragePaused || Time.timeScale <= 0f || AudioListener.pause)
            {
                simulationPaused = true;
                return;
            }
            if (!stream.Ready)
            {
                buffering = true;
                Source.Pause();
                return;
            }
            float resumeAt = Resumable(State.seconds);
            if (started)
                Source.UnPause();
            if (!Source.isPlaying)
            {
                Source.timeSamples = stream.Clip(FrameAt(resumeAt));
                Source.Play();
            }
            playedFrame = Played();
            ForgetMembranes();
            feed.Reset();
            State.seconds = resumeAt;
            started = true;
            wasPlaying = true;
            playedDsp = AudioSettings.dspTime;
        }

        private void ForgetMembranes()
        {
            membraneLevel = glowLevel = 0f;
            membranePushUntil = double.NegativeInfinity;
            membranes.Forget();
        }

        public void Capture()
        {
            if (Time.frameCount > seekSettledFrame
                && Source != null
                && Source.clip != null
                && !Loading
                && Source.isPlaying)
                State.seconds = Mathf.Max(State.seconds, Played() / (float)MusicLiveStream.Frequency);
        }

        private void Update()
        {
            if (State == null || BoomboxPlugin.Instance == null)
                return;
            if (!BoomboxPlugin.Instance.AudioWorldActive)
            {
                desktopAudio?.Poll(false);
                membraneLevel = glowLevel = 0f;
                membranes.Rest();
                if (Visual != null)
                    Visual.GetComponent<VikingBoomboxVisualState>().ApplyMembranes(0f);
                return;
            }
            if (audioChanged)
                Revive();
            ConnectMixer();
            Source.volume = State.muted ? 0f : State.volume;
            Source.spatialBlend = State.onBack ? 0f : 1f;
            Source.maxDistance = BoomboxPlugin.Instance.SoundDistance.Value;
            bool computerAudio = State.TrackId == BoomboxSyntheticTrackId.ComputerAudio;
            var desktopTap = desktopAudio.Poll(computerAudio && IsSyntheticActive);
            if (computerAudio)
                Status = desktopAudio.Status;
            bool paused = SynchronizePlaybackPause();
            UpdateStreamReadiness(paused);
            AnalyzePlayback(computerAudio, desktopTap, paused);
            PresentPlayback();
        }

        private bool SynchronizePlaybackPause()
        {
            bool paused = State.insertedCassette == null
                || StoragePaused
                || Time.timeScale <= 0f
                || AudioListener.pause;
            if (paused && !simulationPaused)
            {
                Capture();
                Source.Pause();
                simulationPaused = true;
                wasPlaying = false;
            }
            else if (!paused && simulationPaused)
            {
                simulationPaused = false;
                if (State.playing && Source.clip != null && !Loading)
                    PlayAtSavedPosition();
            }
            return paused;
        }

        private void UpdateStreamReadiness(bool paused)
        {
            if (stream != null && stream.Error != null)
            {
                string error = stream.Error;
                Source.Stop();
                if (Source.clip != null)
                    Destroy(Source.clip);
                Source.clip = null;
                ReleaseStream();
                buffering = false;
                Fail(error);
            }
            if (stream != null && buffering && stream.Ready)
            {
                if (Loading)
                {
                    Loading = false;
                    SetStatus("text.ready");
                }
                if (pendingSeek.HasValue)
                {
                    int frame = stream.Clip(FrameAt(pendingSeek.Value));
                    if (Source.timeSamples == frame)
                        Source.timeSamples = (frame + 1) % stream.Frames;
                    Source.timeSamples = frame;
                    pendingSeek = null;
                }
                buffering = false;
                if (State.playing && !paused)
                    PlayAtSavedPosition();
            }
        }

        private void AnalyzePlayback(bool computerAudio, BoomboxMusicTap desktopTap, bool paused)
        {
            bass = Mathf.MoveTowards(bass, 0f, Time.deltaTime * 3.5f);
            feed.Rest();
            if (IsActivePlayback && Source.clip != null && Time.frameCount > seekSettledFrame)
            {
                Capture();
                wasPlaying = true;
                playedFrame = Played();
                playedDsp = AudioSettings.dspTime;
                membranes.Measure(stream, playedFrame, Source.clip.channels, Source.clip.frequency, stream.Length);
                feed.Track(stream, playedFrame, Threshold);
                membranes.MeasureWave(stream, (long)Math.Floor(feed.Moment.Time * MusicLiveStream.Frequency));
                if (stream.EofFrame >= 0 && playedFrame >= stream.EofFrame)
                    Advance();
                else if (Source.isVirtual
                    && stream.EofFrame < 0
                    && playedFrame >= MusicLiveStream.FramesOf(stream.Seconds))
                    Advance();
            }
            else if (IsSyntheticActive)
            {
                if (computerAudio)
                {
                    if (desktopTap != null)
                    {
                        feed.Tap(desktopTap, Threshold);
                        membranes.MeasureDesktop(desktopTap, Time.unscaledTime, 1f);
                    }
                    else
                    {
                        feed.Rest();
                        membranes.Rest();
                    }
                }
                else
                {
                    synthetic.Advance(feed, State.TrackId, Threshold);
                    if (BoomboxSyntheticTrackId.IsGameMusic(State.TrackId))
                        membranes.MeasureGameMusic(Time.unscaledTime, AudioSettings.outputSampleRate);
                }
            }
            else if (started
                && wasPlaying
                && State.playing
                && !Loading
                && !buffering
                && Source.clip != null
                && !paused
                && !Source.isPlaying)
            {
                wasPlaying = false;
                if (ReachedEnd())
                    Advance();
            }
        }

        private void PresentPlayback()
        {
            bool active = IsActivePlayback || IsSyntheticActive;
            var player = Player.m_localPlayer;
            bool reacting = active && player != null && !player.IsDead() && !player.IsTeleporting();
            PresentMusic(reacting);
            membraneLevel = reacting
                ? feed.Moment.Time < membranePushUntil
                    ? 1f
                    : membranes.Travel
                : 0f;
            glowLevel = reacting
                ? BoomboxAudioEnvelope.Follow(
                glowLevel,
                Mathf.Max(Mathf.Min(1f, membranes.Level * GlowGain) * GlowShare, bass),
                Time.unscaledDeltaTime
            )
                : 0f;
            if (!reacting)
                membranes.Rest();
            VisualPresenter.PresentFrame(reacting, membraneLevel, glowLevel);
            Display.Show(this, glowLevel);
        }

        private bool ReachedEnd()
        {
            double played = playedFrame + Math.Max(0, AudioSettings.dspTime - playedDsp) * Source.clip.frequency;
            long end = stream.EofFrame >= 0 ? stream.EofFrame : stream.Length;
            return played + EndToleranceSeconds * Source.clip.frequency >= end;
        }

        private void Advance()
        {
            State.seconds = 0f;
            if (!State.repeat && State.trackIndex >= State.tracks.Count - 1)
                State.playing = false;
            SelectTrack(State.trackIndex + 1);
        }

        private void ConnectMixer()
        {
            if (Source.outputAudioMixerGroup == null && MusicMan.instance != null)
                Source.outputAudioMixerGroup = MusicMan.instance.m_musicMixer;
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioChanged;
            generation++;
            CancelPreparation();
            if (Source != null)
                Source.Stop();
            ReleaseStream();
            if (Source != null && Source.clip != null)
            {
                Destroy(Source.clip);
                Source.clip = null;
            }
            if (preparationTask == null)
                return;
            try
            {
                if (!preparationTask.Wait(MusicProcess.ShutdownMilliseconds + 250))
                    BoomboxPlugin.Instance?.LogError("Audio track preparation shutdown timed out");
            }
            catch (AggregateException) { }
        }
    }
}
