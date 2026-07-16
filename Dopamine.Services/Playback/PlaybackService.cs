using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Audio;
using Dopamine.Core.Base;
using Dopamine.Core.Extensions;
using Dopamine.Core.Helpers;
using Dopamine.Core.Settings;
using Dopamine.Data;
using Dopamine.Data.Entities;
using Dopamine.Data.Metadata;
using Dopamine.Data.Repositories;
using Dopamine.Services.Blacklist;
using Dopamine.Services.Collection;
using Dopamine.Services.Entities;
using Dopamine.Services.Equalizer;
using Dopamine.Services.Extensions;
using Dopamine.Services.File;
using Dopamine.Services.I18n;
using Dopamine.Services.Playlist;
using Dopamine.Services.Utils;
using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Dopamine.Services.Playback
{
    public class PlaybackService : IPlaybackService
    {
        private class RestoredQueuedTrack
        {
            public QueuedTrack QueuedTrack { get; set; }

            public TrackViewModel TrackViewModel { get; set; }
        }

        private QueueManager queueManager;
        private System.Timers.Timer progressTimer = new System.Timers.Timer();
        private double progressTimeoutSeconds = 0.5;
        private double progress = 0.0;
        private float volume = 0.0f;
        private LoopMode loopMode;
        private bool shuffle;
        private bool durableShuffle;
        private bool mute;
        private bool isPlayingPreviousTrack;
        private IPlayer player;
        private bool hasMediaFoundationSupport = false;

        private bool isLoadingSettings;

        private bool isQueueChanged;
        private bool canGetSavedQueuedTracks = true;

        private II18nService i18nService;
        private IFileService fileService;
        private IEqualizerService equalizerService;
        private IPlaylistService playlistService;
        private IContainerProvider container;
        private EqualizerPreset desiredPreset;
        private EqualizerPreset activePreset;
        private bool isEqualizerEnabled;

        private IQueuedTrackRepository queuedTrackRepository;
        private IBlacklistService blacklistService;
        private System.Timers.Timer saveQueuedTracksTimer = new System.Timers.Timer();
        private int saveQueuedTracksTimeoutSeconds = 5;
        private int saveQueuedTracksQuickTimeoutSeconds = 1;
        private int savePlaybackPositionIntervalSeconds = 10;
        private long lastSavedPlaybackPositionSeconds = -1;
        private string lastSavedPlaybackPositionQueueID = string.Empty;

        private bool isSavingQueuedTracks = false;

        private IPlayerFactory playerFactory;

        private ITrackRepository trackRepository;

        private System.Timers.Timer savePlaybackCountersTimer = new System.Timers.Timer();
        private int savePlaybackCountersTimeoutSeconds = 2;

        private bool isSavingPLaybackCounters = false;
        private Dictionary<string, PlaybackCounter> playbackCounters = new Dictionary<string, PlaybackCounter>();

        private object playbackCountersLock = new object();

        private SynchronizationContext context;
        private bool isLoadingTrack;
        private QueuePersistenceMode queuePersistenceMode = QueuePersistenceMode.Durable;
        private PlaybackQueueContext queueContext = PlaybackQueueContext.Default;
        private IPlaybackSourceResolver playbackSourceResolver;
        private CancellationTokenSource playbackSourceCancellationTokenSource;
        private long playbackSourceGeneration;
        private SemaphoreSlim playbackTransitionGate = new SemaphoreSlim(1, 1);
        private SemaphoreSlim transientQueueGate = new SemaphoreSlim(1, 1);

        private AudioDevice audioDevice;
        private const int PlaybackFadeDurationMilliseconds = 1000;
        private const int PlaybackFadeSteps = 20;
        private int fadeOperationId;
        private double bufferingProgress;
        private bool showBufferingProgress;

        public bool IsSavingQueuedTracks => this.isSavingQueuedTracks;

        public bool IsSavingPlaybackCounters => this.isSavingPLaybackCounters;

        public bool HasMediaFoundationSupport => this.hasMediaFoundationSupport;

        public bool IsStopped
        {
            get
            {
                if (this.player != null)
                {
                    return !this.player.CanStop;
                }
                else
                {
                    return true;
                }
            }
        }

        public bool IsPlaying
        {
            get
            {
                if (this.player != null)
                {
                    return this.player.CanPause;
                }
                else
                {
                    return false;
                }
            }
        }

        public IList<TrackViewModel> Queue => this.queueManager.Queue;

        public PlaybackQueueContext QueueContext => this.queueContext;

        public TrackViewModel CurrentTrack => this.queueManager.CurrentTrack();

        public bool HasQueue => this.queueManager.Queue != null && this.queueManager.Queue.Count > 0;

        public bool HasCurrentTrack => this.queueManager.CurrentTrack() != null;

        public double Progress
        {
            get { return this.progress; }
            set { this.progress = value; }
        }

        public double BufferingProgress => this.bufferingProgress;

        public bool ShowBufferingProgress => this.showBufferingProgress;

        public float Volume
        {
            get { return this.volume; }

            set
            {
                if (value > 1)
                {
                    value = 1;
                }

                if (value < 0)
                {
                    value = 0;
                }

                this.volume = value;

                this.CancelPlaybackFade();
                if (this.player != null && !this.mute) this.player.SetVolume(value);

                SettingsClient.Set<double>("Playback", "Volume", Math.Round(value, 2));
                this.PlaybackVolumeChanged(this, new PlaybackVolumeChangedEventArgs(isLoadingSettings));
            }
        }

        public LoopMode LoopMode
        {
            get { return this.loopMode; }
            set
            {
                this.loopMode = value;
                this.PlaybackLoopChanged(this, new EventArgs());
            }
        }

        public bool Shuffle
        {
            get { return this.shuffle; }
        }

        public bool Mute
        {
            get { return this.mute; }
        }

        public async Task SetShuffleAsync(bool isShuffled)
        {
            if (this.queueContext == PlaybackQueueContext.NeteasePersonalFm)
            {
                isShuffled = false;
            }

            this.shuffle = isShuffled;

            if (isShuffled)
            {
                await this.queueManager.ShuffleAsync();
            }
            else
            {
                await this.queueManager.UnShuffleAsync();

            }

            if (this.queueContext == PlaybackQueueContext.NeteaseDailyRecommendations)
            {
                SettingDefaults.SetSafe<bool>("Netease", "DailyRecommendationsShuffle", this.shuffle);
            }
            else if (this.queueContext == PlaybackQueueContext.NeteaseIntelligenceRecommendations)
            {
                SettingDefaults.SetSafe<bool>("Netease", "IntelligenceRecommendationsShuffle", this.shuffle);
            }
            else
            {
                this.durableShuffle = this.shuffle;
                SettingsClient.Set<bool>("Playback", "Shuffle", this.shuffle);
            }
            this.PlaybackShuffleChanged(this, new EventArgs());
            this.WriteSettings();
            this.QueueChanged(this, new EventArgs());
            await this.SaveQueuedTracksNowAsync();
        }

        public bool UseAllAvailableChannels { get; set; }

        public int Latency { get; set; }

        public bool EventMode { get; set; }

        public bool ExclusiveMode { get; set; }

        public TimeSpan GetCurrentTime
        {
            get
            {
                try
                {
                    // Check if there is a Track playing
                    if (this.player != null && this.player.CanStop)
                    {
                        // This prevents displaying a current time which is larger than the total time
                        if (this.player.GetCurrentTime() <= this.player.GetTotalTime())
                        {
                            return this.player.GetCurrentTime();
                        }
                        else
                        {
                            return this.player.GetTotalTime();
                        }
                    }
                    else
                    {
                        return new TimeSpan(0);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to get current time. Returning 00:00. Exception: {0}", ex.Message);
                    return new TimeSpan(0);
                }

            }
        }

        public TimeSpan GetTotalTime
        {
            get
            {
                try
                {
                    // Check if there is a Track playing
                    if (this.player != null && this.player.CanStop && this.HasCurrentTrack && this.CurrentTrack.Duration != null)
                    {
                        // In some cases, the duration reported by TagLib is 1 second longer than the duration reported by IPlayer.
                        if (this.CurrentTrack.Track.Duration > this.player.GetTotalTime().TotalMilliseconds)
                        {
                            // To show the same duration everywhere, we report the TagLib duration here instead of the IPlayer duration.
                            return new TimeSpan(0, 0, 0, 0, Convert.ToInt32(this.CurrentTrack.Track.Duration));
                        }
                        else
                        {
                            // Unless the TagLib duration is incorrect. In rare cases it is 0, even if 
                            // IPlayer reports a correct duration. In such cases, report the IPlayer duration.
                            return this.player.GetTotalTime();
                        }
                    }
                    else
                    {
                        return new TimeSpan(0);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to get total time. Returning 00:00. Exception: {0}", ex.Message);
                    return new TimeSpan(0);
                }

            }
        }

        public IPlayer Player
        {
            get { return this.player; }
        }

        public PlaybackService(IFileService fileService, II18nService i18nService, ITrackRepository trackRepository, IBlacklistService blacklistService,
            IEqualizerService equalizerService, IQueuedTrackRepository queuedTrackRepository, IContainerProvider container, IPlaylistService playlistService,
            IPlaybackSourceResolver playbackSourceResolver)
        {
            this.fileService = fileService;
            this.i18nService = i18nService;
            this.trackRepository = trackRepository;
            this.queuedTrackRepository = queuedTrackRepository;
            this.blacklistService = blacklistService;
            this.equalizerService = equalizerService;
            this.playlistService = playlistService;
            this.container = container;
            this.playbackSourceResolver = playbackSourceResolver;

            this.context = SynchronizationContext.Current;

            this.queueManager = new QueueManager(this.trackRepository);

            // Event handlers
            this.fileService.ImportingTracks += (_, __) => this.canGetSavedQueuedTracks = false;
            this.fileService.TracksImported += (tracks, track) => this.EnqueueFromFilesAsync(tracks, track);
            this.i18nService.LanguageChanged += (_, __) => this.UpdateQueueLanguageAsync();

            // Set up timers
            this.progressTimer.Interval = TimeSpan.FromSeconds(this.progressTimeoutSeconds).TotalMilliseconds;
            this.progressTimer.Elapsed += new ElapsedEventHandler(this.ProgressTimeoutHandler);

            this.saveQueuedTracksTimer.Interval = TimeSpan.FromSeconds(this.saveQueuedTracksTimeoutSeconds).TotalMilliseconds;
            this.saveQueuedTracksTimer.Elapsed += new ElapsedEventHandler(this.SaveQueuedTracksTimeoutHandler);

            this.savePlaybackCountersTimer.Interval = TimeSpan.FromSeconds(this.savePlaybackCountersTimeoutSeconds).TotalMilliseconds;
            this.savePlaybackCountersTimer.Elapsed += new ElapsedEventHandler(this.SavePlaybackCountersHandler);

            this.Initialize();
        }

        private async void EnqueueFromFilesAsync(IList<TrackViewModel> tracks, TrackViewModel track)
        {
            this.canGetSavedQueuedTracks = false;

            AppLog.Info("Start enqueuing {0} track(s) from files", tracks.Count);
            await this.EnqueueAsync(tracks, track);
            AppLog.Info("Finished enqueuing {0} track(s) from files", tracks.Count);
        }

        public event PlaybackSuccessEventHandler PlaybackSuccess = delegate { };
        public event PlaybackPausedEventHandler PlaybackPaused = delegate { };
        public event PlaybackFailedEventHandler PlaybackFailed = delegate { };
        public event EventHandler PlaybackProgressChanged = delegate { };
        public event EventHandler PlaybackBufferingProgressChanged = delegate { };
        public event EventHandler PlaybackResumed = delegate { };
        public event EventHandler PlaybackStopped = delegate { };
        public event PlaybackVolumeChangedEventhandler PlaybackVolumeChanged = delegate { };
        public event EventHandler PlaybackMuteChanged = delegate { };
        public event EventHandler PlaybackLoopChanged = delegate { };
        public event EventHandler PlaybackShuffleChanged = delegate { };
        public event Action<int> AddedTracksToQueue = delegate { };
        public event PlaybackCountersChangedEventHandler PlaybackCountersChanged = delegate { };
        public event Action<bool> LoadingTrack = delegate { };
        public event EventHandler PlayingTrackChanged = delegate { };
        public event EventHandler QueueChanged = delegate { };
        public event EventHandler PlaybackSkipped = delegate { };

        private AudioDevice CreateDefaultAudioDevice()
        {
            return new AudioDevice(ResourceUtils.GetString("Language_Default_Audio_Device"), string.Empty);
        }

        public async Task<AudioDevice> GetSavedAudioDeviceAsync()
        {
            string savedAudioDeviceId = SettingsClient.Get<string>("Playback", "AudioDevice");

            IList<AudioDevice> audioDevices = await this.GetAllAudioDevicesAsync();
            AudioDevice savedDevice = audioDevices.Where(x => x.DeviceId.Equals(savedAudioDeviceId)).FirstOrDefault();

            if (savedDevice == null)
            {
                AppLog.Warning($"Audio device with deviceId={savedAudioDeviceId} could not be found. Using default device instead.");
                savedDevice = this.CreateDefaultAudioDevice();
            }

            return savedDevice;
        }

        public async Task<IList<AudioDevice>> GetAllAudioDevicesAsync()
        {
            var audioDevices = new List<AudioDevice>();

            await Task.Run(() =>
            {
                if (this.player != null)
                {
                    audioDevices.Add(this.CreateDefaultAudioDevice());
                    audioDevices.AddRange(this.player.GetAllAudioDevices());
                }
            });

            return audioDevices;
        }

        public async Task SwitchAudioDeviceAsync(AudioDevice device)
        {
            this.audioDevice = device;

            await Task.Run(() =>
            {
                if (this.player != null)
                {
                    this.player.SwitchAudioDevice(this.audioDevice);
                }
            });
        }

        public async Task StopIfPlayingAsync(TrackViewModel track)
        {
            if (track.SafePath.Equals(this.CurrentTrack.SafePath))
            {
                if (this.Queue.Count == 1)
                {
                    this.Stop();
                }
                else
                {
                    await this.PlayNextAsync();
                }
            }
        }

        public async Task UpdateQueueOrderAsync(IList<TrackViewModel> tracks)
        {
            if (await this.queueManager.UpdateQueueOrderAsync(tracks, this.shuffle))
            {
                // Required to update other Now Playing screens
                this.QueueChanged(this, new EventArgs());
            }
        }

        public async Task UpdateQueueMetadataAsync(IList<FileMetadata> fileMetadatas)
        {
            UpdateQueueMetadataResult result = await this.queueManager.UpdateMetadataAsync(fileMetadatas);

            // Raise events
            if (result.IsPlayingTrackChanged)
            {
                this.PlayingTrackChanged(this, new EventArgs());
            }

            if (result.IsQueueChanged)
            {
                this.QueueChanged(this, new EventArgs());
            }
        }

        private async void UpdateQueueLanguageAsync()
        {
            await this.queueManager.UpdateQueueLanguageAsync();

            // Raise events
            this.PlayingTrackChanged(this, new EventArgs());
            this.QueueChanged(this, new EventArgs());
        }

        public async Task SetIsEqualizerEnabledAsync(bool isEnabled)
        {
            this.isEqualizerEnabled = isEnabled;

            this.desiredPreset = await this.equalizerService.GetSelectedPresetAsync();
            this.activePreset = isEnabled ? this.desiredPreset : new EqualizerPreset();

            if (this.player != null)
            {
                this.player.ApplyFilter(this.activePreset.Bands);
            }
        }

        public void ApplyPreset(EqualizerPreset preset)
        {
            this.desiredPreset = preset;

            if (this.isEqualizerEnabled)
            {
                this.activePreset = desiredPreset;

                if (this.player != null)
                {
                    this.player.ApplyFilter(this.activePreset.Bands);
                }
            }
        }

        public async Task SaveQueuedTracksAsync()
        {
            if (this.queuePersistenceMode == QueuePersistenceMode.Transient)
            {
                this.saveQueuedTracksTimer.Stop();
                return;
            }

            if (!this.isQueueChanged)
            {
                return;
            }

            this.saveQueuedTracksTimer.Stop();
            this.isSavingQueuedTracks = true;

            try
            {
                IList<TrackViewModel> tracks = this.Queue.ToList();
                var queuedTracks = new List<QueuedTrack>(tracks.Count);
                IDictionary<string, long> shuffleOrderByQueueID = this.queueManager.GetShuffleOrderByQueueID();
                string currentQueueID = this.CurrentTrack?.QueueID;
                long progressSeconds = Convert.ToInt64(this.GetCurrentTime.TotalSeconds);

                int orderID = 0;

                foreach (TrackViewModel track in tracks)
                {
                    if (string.IsNullOrWhiteSpace(track.QueueID))
                    {
                        track.QueueID = Guid.NewGuid().ToString();
                    }

                    var queuedTrack = new QueuedTrack();
                    queuedTrack.Path = track.Path;
                    queuedTrack.SafePath = track.SafePath;
                    queuedTrack.QueueID = track.QueueID;
                    queuedTrack.OrderID = orderID;
                    queuedTrack.ShuffleOrderID = shuffleOrderByQueueID.ContainsKey(track.QueueID) ? shuffleOrderByQueueID[track.QueueID] : orderID;
                    queuedTrack.IsPlaying = 0;
                    queuedTrack.ProgressSeconds = 0;

                    if (!string.IsNullOrEmpty(currentQueueID) && track.QueueID.Equals(currentQueueID))
                    {
                        queuedTrack.IsPlaying = 1;
                        queuedTrack.ProgressSeconds = progressSeconds;
                    }

                    queuedTracks.Add(queuedTrack);

                    orderID++;
                }

                await this.queuedTrackRepository.SaveQueuedTracksAsync(queuedTracks);

                AppLog.Info("Saved {0} queued tracks", queuedTracks.Count.ToString());
                this.RememberSavedPlaybackPosition();
            }
            catch (Exception ex)
            {
                AppLog.Info("Could not save queued tracks. Exception: {0}", ex.Message);
            }

            this.isSavingQueuedTracks = false;
        }

        private async Task SaveQueuedTracksNowAsync()
        {
            if (this.queuePersistenceMode == QueuePersistenceMode.Transient)
            {
                return;
            }

            this.saveQueuedTracksTimer.Stop();
            this.isQueueChanged = true;

            while (this.isSavingQueuedTracks)
            {
                await Task.Delay(50);
            }

            await this.SaveQueuedTracksAsync();
        }

        private void RememberSavedPlaybackPosition()
        {
            this.lastSavedPlaybackPositionQueueID = this.CurrentTrack?.QueueID ?? string.Empty;
            this.lastSavedPlaybackPositionSeconds = Convert.ToInt64(this.GetCurrentTime.TotalSeconds);
        }

        private void WriteSettings()
        {
            try
            {
                SettingsClient.Write();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not write settings. Exception: {0}", ex.Message);
            }
        }

        private void QueuePlaybackPositionSaveIfNeeded(TimeSpan currentTime)
        {
            string currentQueueID = this.CurrentTrack?.QueueID;

            if (string.IsNullOrWhiteSpace(currentQueueID))
            {
                return;
            }

            long currentSeconds = Convert.ToInt64(currentTime.TotalSeconds);

            if (!currentQueueID.Equals(this.lastSavedPlaybackPositionQueueID) ||
                Math.Abs(currentSeconds - this.lastSavedPlaybackPositionSeconds) >= this.savePlaybackPositionIntervalSeconds)
            {
                this.lastSavedPlaybackPositionQueueID = currentQueueID;
                this.lastSavedPlaybackPositionSeconds = currentSeconds;
                this.ResetSaveQueuedTracksTimer(this.saveQueuedTracksQuickTimeoutSeconds);
            }
        }

        public async Task SavePlaybackCountersAsync()
        {
            if (this.playbackCounters.Count == 0 | this.isSavingPLaybackCounters)
            {
                return;
            }

            this.savePlaybackCountersTimer.Stop();

            this.isSavingPLaybackCounters = true;

            IList<PlaybackCounter> localCounters = null;

            await Task.Run(() =>
            {
                lock (this.playbackCountersLock)
                {
                    localCounters = new List<PlaybackCounter>(this.playbackCounters.Values);
                    this.playbackCounters.Clear();
                }
            });

            foreach (PlaybackCounter localCounter in localCounters)
            {
                await this.trackRepository.UpdatePlaybackCountersAsync(localCounter);
            }

            this.PlaybackCountersChanged(localCounters);

            AppLog.Info("Saved track statistics");

            this.isSavingPLaybackCounters = false;

            // If, in the meantime, new playback counters are available, reset the timer.
            if (this.playbackCounters.Count > 0)
            {
                this.ResetSavePlaybackCountersTimer();
            }
        }

        public async Task PlayOrPauseAsync()
        {
            if (!this.IsStopped)
            {
                if (this.IsPlaying)
                {
                    await this.PauseAsync();
                }
                else
                {
                    await this.ResumeAsync();
                }
            }
            else
            {
                if (this.Queue != null && this.Queue.Count > 0)
                {
                    // There are already tracks enqueued. Start playing immediately.
                    await this.PlayFirstAsync();
                }
                else
                {
                    // Enqueue all tracks before playing
                    await this.EnqueueAsync(false, false);
                }
            }
        }

        public void SetMute(bool mute)
        {
            this.mute = mute;
            this.CancelPlaybackFade();

            if (this.player != null)
            {
                this.player.SetVolume(mute ? 0.0f : this.Volume);
            }

            SettingsClient.Set<bool>("Playback", "Mute", this.mute);
            this.PlaybackMuteChanged(this, new EventArgs());
        }

        public void SkipProgress(double progress)
        {
            if (this.player != null && this.player.CanStop)
            {
                this.Progress = progress;
                int newSeconds = Convert.ToInt32(progress * this.player.GetTotalTime().TotalSeconds);
                this.player.Skip(newSeconds);
                this.PlaybackSkipped(this, new EventArgs());
                this.ResetSaveQueuedTracksTimer(this.saveQueuedTracksQuickTimeoutSeconds);
            }
            else
            {
                this.Progress = 0.0;
            }

            this.PlaybackProgressChanged(this, new EventArgs());
        }

        public void SkipSeconds(int seconds)
        {
            if (this.player != null && this.player.CanStop)
            {
                double totalSeconds = this.GetCurrentTime.TotalSeconds;

                if (seconds < 0 && totalSeconds <= Math.Abs(seconds))
                {
                    this.player.Skip(0);
                }
                else
                {
                    this.player.Skip(Convert.ToInt32(this.GetCurrentTime.TotalSeconds + seconds));
                }

                this.PlaybackSkipped(this, new EventArgs());
                this.PlaybackProgressChanged(this, new EventArgs());
                this.ResetSaveQueuedTracksTimer(this.saveQueuedTracksQuickTimeoutSeconds);
            }
        }

        public void Stop()
        {
            this.CancelPlaybackFade();
            this.CancelPlaybackSourceResolution();

            if (this.player != null && this.player.CanStop)
            {
                this.player.Stop();
            }

            this.PlayingTrackChanged(this, new EventArgs());

            this.progressTimer.Stop();
            this.Progress = 0.0;
            this.SetBufferingProgress(false, 0.0);
            this.PlaybackStopped(this, new EventArgs());
        }

        public async Task PlayNextAsync()
        {
            AppLog.Info("Request to play the next track.");

            if (this.HasCurrentTrack && this.CurrentTrack.IsLocalFile)
            {
                try
                {
                    int currentTime = this.GetCurrentTime.Seconds;
                    int totalTime = this.GetTotalTime.Seconds;

                    if (currentTime <= 10)
                    {
                        // Increase SkipCount
                        await this.UpdatePlaybackCountersAsync(this.CurrentTrack.Path, false, true);
                    }
                    else
                    {
                        // Increase PlayCount
                        await this.UpdatePlaybackCountersAsync(this.CurrentTrack.Path, true, false);
                    }

                }
                catch (Exception ex)
                {
                    AppLog.Error("Could not get time information for Track with path='{0}'. Exception: {1}", this.CurrentTrack.Path, ex.Message);
                }
            }

            // We don't want interruptions when trying to play the next Track.
            // If the next Track cannot be played, keep skipping to the 
            // following Track until a working Track is found.
            bool playSuccess = false;
            int numberSkips = 0;

            while (!playSuccess)
            {
                // We skip maximum 3 times. This prevents an infinite 
                // loop if shuffledTracks only contains broken Tracks.
                if (numberSkips < 3)
                {
                    numberSkips += 1;
                    playSuccess = await this.TryPlayNextAsync(true);
                }
                else
                {
                    this.Stop();
                    playSuccess = true; // Otherwise we never get out of this While loop
                }
            }
        }

        public async Task PlayPreviousAsync()
        {
            AppLog.Info("Request to play the previous track.");

            // We don't want interruptions when trying to play the previous Track. 
            // If the previous Track cannot be played, keep skipping to the
            // preceding Track until a working Track is found.
            bool playSuccess = false;
            int numberSkips = 0;

            while (!playSuccess)
            {
                // We skip maximum 3 times. This prevents an infinite 
                // loop if shuffledTracks only contains broken Tracks.
                if (numberSkips < 3)
                {
                    numberSkips += 1;
                    playSuccess = await this.TryPlayPreviousAsync(true);
                }
                else
                {
                    this.Stop();
                    playSuccess = true; // Otherwise we never get out of this While loop
                }
            }
        }

        public async Task EnqueueAsync(IList<TrackViewModel> tracks, bool shuffle, bool unshuffle)
        {
            if (tracks == null)
            {
                return;
            }

            this.EnterDurableQueueMode();

            // Shuffle
            if (shuffle)
            {
                await this.EnqueueAsync(tracks, true);
            }

            // Unshuffle
            if (unshuffle)
            {
                await this.EnqueueAsync(tracks, false);
            }

            // Use the current shuffle mode
            if (!shuffle && !unshuffle)
            {
                await this.EnqueueAsync(tracks, this.shuffle);
            }

            // Start playing
            await this.PlayFirstAsync();
        }

        private async Task EnqueueTrackEntitiesAsync(IList<Track> tracks, bool shuffle, bool unshuffle)
        {
            if (tracks == null)
            {
                return;
            }

            this.EnterDurableQueueMode();

            // Shuffle
            if (shuffle)
            {
                await this.EnqueueTrackEntitiesAsync(tracks, true);
            }

            // Unshuffle
            if (unshuffle)
            {
                await this.EnqueueTrackEntitiesAsync(tracks, false);
            }

            // Use the current shuffle mode
            if (!shuffle && !unshuffle)
            {
                await this.EnqueueTrackEntitiesAsync(tracks, this.shuffle);
            }

            // Start playing
            await this.PlayFirstAsync();
        }

        public async Task EnqueueAsync(bool shuffle, bool unshuffle)
        {
            IList<Track> tracks = await this.trackRepository.GetTracksAsync();
            List<Track> orderedTracks = await EntityUtils.OrderTrackEntitiesAsync(tracks, TrackOrder.ByAlbum);
            await this.EnqueueTrackEntitiesAsync(orderedTracks, shuffle, unshuffle);
        }

        public async Task EnqueueAsync(IList<TrackViewModel> tracks)
        {
            await this.EnqueueAsync(tracks, false, false);
        }

        public async Task EnqueueAsync(IList<TrackViewModel> tracks, TrackViewModel track)
        {
            if (tracks == null || track == null)
            {
                return;
            }

            this.EnterDurableQueueMode();

            await this.EnqueueAsync(tracks, this.shuffle);
            await this.PlaySelectedAsync(track);
        }

        public async Task EnqueueArtistsAsync(IList<string> artists, bool shuffle, bool unshuffle)
        {
            if (artists == null)
            {
                return;
            }

            IList<Track> tracks = await this.trackRepository.GetArtistTracksAsync(artists);
            List<Track> orderedTracks = await EntityUtils.OrderTrackEntitiesAsync(tracks, TrackOrder.ByAlbum);
            await this.EnqueueTrackEntitiesAsync(orderedTracks, shuffle, unshuffle);
        }

        public async Task EnqueueGenresAsync(IList<string> genres, bool shuffle, bool unshuffle)
        {
            if (genres == null)
            {
                return;
            }

            IList<Track> tracks = await this.trackRepository.GetGenreTracksAsync(genres);
            List<Track> orderedTracks = await EntityUtils.OrderTrackEntitiesAsync(tracks, TrackOrder.ByAlbum);
            await this.EnqueueTrackEntitiesAsync(orderedTracks, shuffle, unshuffle);
        }

        public async Task EnqueueAlbumsAsync(IList<AlbumViewModel> albumViewModels, bool shuffle, bool unshuffle)
        {
            if (albumViewModels == null)
            {
                return;
            }

            IList<Track> tracks = await this.trackRepository.GetAlbumTracksAsync(albumViewModels.Select(x => x.AlbumKey).ToList());
            List<Track> orderedTracks = await Utils.EntityUtils.OrderTrackEntitiesAsync(tracks, TrackOrder.ByAlbum);
            await this.EnqueueTrackEntitiesAsync(orderedTracks, shuffle, unshuffle);
        }

        public async Task EnqueuePlaylistsAsync(IList<PlaylistViewModel> playlistViewModels, bool shuffle, bool unshuffle)
        {
            if (playlistViewModels == null || playlistViewModels.Count == 0)
            {
                return;
            }

            IList<TrackViewModel> tracks = await this.playlistService.GetTracksAsync(playlistViewModels.First());
            await this.EnqueueAsync(tracks, shuffle, unshuffle);
        }

        public async Task PlaySelectedAsync(TrackViewModel track)
        {
            if (track != null && track.IsLocalFile)
            {
                this.EnterDurableQueueMode();
            }

            await this.TryPlayAsync(track);
        }

        public async Task<bool> PlaySelectedAsync(IList<TrackViewModel> tracks)
        {
            this.EnterDurableQueueMode();
            var result = await this.queueManager.ClearQueueAsync();
            if (result)
            {
                result = (await this.AddToQueueAsync(tracks)).IsSuccess;
                if (result)
                    await this.PlayNextAsync();
            }

            return result;
        }

        public async Task<bool> PlayTransientQueueAsync(
            IList<TrackViewModel> tracks,
            TrackViewModel startTrack,
            PlaybackQueueContext context)
        {
            if (tracks == null || tracks.Count == 0 || startTrack == null || tracks.Any(x => x == null || x.IsLocalFile))
            {
                return false;
            }

            if (!await this.transientQueueGate.WaitAsync(0))
            {
                return false;
            }

            try
            {
                if (this.queuePersistenceMode == QueuePersistenceMode.Durable && this.HasQueue)
                {
                    await this.SaveQueuedTracksNowAsync();
                }

                this.saveQueuedTracksTimer.Stop();
                this.queuePersistenceMode = QueuePersistenceMode.Transient;
                this.SetQueueContext(context);
                this.isQueueChanged = false;

                if (!await this.queueManager.ClearQueueAsync())
                {
                    this.queuePersistenceMode = QueuePersistenceMode.Durable;
                    this.SetQueueContext(PlaybackQueueContext.Default);
                    return false;
                }

                bool useShuffle = this.shuffle;
                EnqueueResult enqueueResult = await this.queueManager.EnqueueAsync(tracks, useShuffle);

                if (!enqueueResult.IsSuccess)
                {
                    this.queuePersistenceMode = QueuePersistenceMode.Durable;
                    this.SetQueueContext(PlaybackQueueContext.Default);
                    return false;
                }

                this.QueueChanged(this, EventArgs.Empty);
                TrackViewModel queuedStartTrack = this.Queue.FirstOrDefault(x => x.SafePath.Equals(startTrack.SafePath));
                return await this.TryPlayAsync(queuedStartTrack ?? this.Queue.FirstOrDefault());
            }
            finally
            {
                this.transientQueueGate.Release();
            }
        }

        public async Task<DequeueResult> DequeueAsync(IList<TrackViewModel> tracks)
        {
            DequeueResult dequeueResult = await this.queueManager.DequeueAsync(tracks);

            if (dequeueResult.IsSuccess & dequeueResult.IsPlayingTrackDequeued)
            {
                if (dequeueResult.NextAvailableTrack != null)
                {
                    await this.TryPlayAsync(dequeueResult.NextAvailableTrack);
                }
                else
                {
                    this.Stop();
                }
            }

            this.QueueChanged(this, new EventArgs());

            this.ResetSaveQueuedTracksTimer(); // Save queued tracks to the database

            return dequeueResult;
        }

        public async Task<EnqueueResult> AddToQueueAsync(IList<TrackViewModel> tracks)
        {
            if (!this.CanAddTracksToCurrentQueue(tracks))
            {
                return new EnqueueResult { IsSuccess = false };
            }

            EnqueueResult result = await this.queueManager.EnqueueAsync(tracks, this.shuffle);

            this.QueueChanged(this, new EventArgs());

            if (result.EnqueuedTrackCount > 0 && result.IsSuccess)
            {
                this.AddedTracksToQueue(result.EnqueuedTrackCount);
            }

            this.ResetSaveQueuedTracksTimer(); // Save queued tracks to the database

            return result;
        }

        public async Task<EnqueueResult> AddToQueueNextAsync(IList<TrackViewModel> tracks)
        {
            if (!this.CanAddTracksToCurrentQueue(tracks))
            {
                return new EnqueueResult { IsSuccess = false };
            }

            EnqueueResult result = await this.queueManager.EnqueueNextAsync(tracks);

            this.QueueChanged(this, new EventArgs());

            if (result.EnqueuedTrackCount > 0 && result.IsSuccess)
            {
                this.AddedTracksToQueue(result.EnqueuedTrackCount);
            }

            this.ResetSaveQueuedTracksTimer(); // Save queued tracks to the database

            return result;
        }

        private async Task<EnqueueResult> AddTrackEntitiesToQueueAsync(IList<Track> tracks)
        {
            EnqueueResult result = await this.queueManager.EnqueueAsync(tracks, this.shuffle, (track) => this.container.ResolveTrackViewModel(track));

            this.QueueChanged(this, new EventArgs());

            if (result.EnqueuedTrackCount > 0 && result.IsSuccess)
            {
                this.AddedTracksToQueue(result.EnqueuedTrackCount);
            }

            this.ResetSaveQueuedTracksTimer(); // Save queued tracks to the database

            return result;
        }

        public async Task<EnqueueResult> AddArtistsToQueueAsync(IList<string> artists)
        {
            IList<Track> tracks = await this.trackRepository.GetArtistTracksAsync(artists);
            List<Track> orderedTracks = await EntityUtils.OrderTrackEntitiesAsync(tracks, TrackOrder.ByAlbum);
            return await this.AddTrackEntitiesToQueueAsync(orderedTracks);
        }

        public async Task<EnqueueResult> AddGenresToQueueAsync(IList<string> genres)
        {
            IList<Track> tracks = await this.trackRepository.GetGenreTracksAsync(genres);
            List<Track> orderedTracks = await EntityUtils.OrderTrackEntitiesAsync(tracks, TrackOrder.ByAlbum);
            return await this.AddTrackEntitiesToQueueAsync(orderedTracks);
        }

        public async Task<EnqueueResult> AddAlbumsToQueueAsync(IList<AlbumViewModel> albumViewModels)
        {
            IList<Track> tracks = await this.trackRepository.GetAlbumTracksAsync(albumViewModels.Select(x => x.AlbumKey).ToList());
            List<Track> orderedTracks = await EntityUtils.OrderTrackEntitiesAsync(tracks, TrackOrder.ByAlbum);
            return await this.AddTrackEntitiesToQueueAsync(orderedTracks);
        }

        private async void Initialize()
        {
            // Media Foundation
            this.hasMediaFoundationSupport = MediaFoundationHelper.HasMediaFoundationSupport();

            // Settings
            this.SetPlaybackSettings();

            // PlayerFactory
            this.playerFactory = new PlayerFactory();

            // Player (default for now, can be changed later when playing a file)
            this.player = this.playerFactory.Create(this.hasMediaFoundationSupport);

            // Audio device
            await this.SetAudioDeviceAsync();

            // Equalizer
            await this.SetIsEqualizerEnabledAsync(SettingsClient.Get<bool>("Equalizer", "IsEnabled"));

            // Queued tracks
            this.GetSavedQueuedTracks();
        }

        private async void SavePlaybackCountersHandler(object sender, ElapsedEventArgs e)
        {
            await this.SavePlaybackCountersAsync();
        }

        private async Task UpdatePlaybackCountersAsync(string path, bool incrementPlayCount, bool incrementSkipCount)
        {

            if (!this.playbackCounters.ContainsKey(path))
            {
                // Try to find an existing counter
                PlaybackCounter counters = await this.trackRepository.GetPlaybackCountersAsync(path);

                // If no existing counter was found, create a new one.
                if (counters == null)
                {
                    counters = new PlaybackCounter();
                }

                // Add statistic to the dictionary
                lock (this.playbackCountersLock)
                {
                    this.playbackCounters.Add(path, counters);
                }
            }

            await Task.Run(() =>
            {
                lock (this.playbackCountersLock)
                {
                    try
                    {
                        if (incrementPlayCount)
                        {
                            this.playbackCounters[path].PlayCount += 1;
                            this.playbackCounters[path].DateLastPlayed = DateTime.Now.Ticks;
                        }
                        if (incrementSkipCount)
                        {
                            this.playbackCounters[path].SkipCount += 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Could not update track statistics for track with path='{0}'. Exception: {1}", path, ex.Message);
                    }
                }
            });

            this.ResetSavePlaybackCountersTimer();
        }

        private bool ShouldUsePlaybackFade(bool isSilent = false)
        {
            return !isSilent && !this.mute && this.IsPlaybackFadeEnabled();
        }

        private bool IsPlaybackFadeEnabled()
        {
            return PlaybackFadeSettings.IsEnabled();
        }

        private void CancelPlaybackFade()
        {
            Interlocked.Increment(ref this.fadeOperationId);
        }

        private async Task FadeCurrentPlayerVolumeAsync(float targetVolume)
        {
            IPlayer fadePlayer = this.player;

            if (fadePlayer == null)
            {
                return;
            }

            int operationId = Interlocked.Increment(ref this.fadeOperationId);

            targetVolume = Math.Max(0.0f, Math.Min(1.0f, targetVolume));
            float startVolume;

            try
            {
                startVolume = fadePlayer.GetVolume();
            }
            catch (Exception)
            {
                startVolume = this.Volume;
            }

            startVolume = Math.Max(0.0f, Math.Min(1.0f, startVolume));

            if (Math.Abs(startVolume - targetVolume) < 0.001f)
            {
                fadePlayer.SetVolume(targetVolume);
                return;
            }

            for (int step = 1; step <= PlaybackFadeSteps; step++)
            {
                if (operationId != this.fadeOperationId || !object.ReferenceEquals(fadePlayer, this.player))
                {
                    return;
                }

                float nextVolume = startVolume + ((targetVolume - startVolume) * step / PlaybackFadeSteps);
                fadePlayer.SetVolume(nextVolume);
                await Task.Delay(PlaybackFadeDurationMilliseconds / PlaybackFadeSteps);
            }

            if (operationId == this.fadeOperationId && object.ReferenceEquals(fadePlayer, this.player))
            {
                fadePlayer.SetVolume(targetVolume);
            }
        }

        private async Task PauseAsync(bool isSilent = false)
        {
            try
            {
                IPlayer pausePlayer = this.player;

                if (pausePlayer != null)
                {
                    if (this.ShouldUsePlaybackFade(isSilent) && this.IsPlaying)
                    {
                        await this.FadeCurrentPlayerVolumeAsync(0.0f);
                    }
                    else
                    {
                        this.CancelPlaybackFade();
                    }

                    if (!object.ReferenceEquals(pausePlayer, this.player))
                    {
                        return;
                    }

                    await Task.Run(() => pausePlayer.Pause());
                    this.PlaybackPaused(this, new PlaybackPausedEventArgs() { IsSilent = isSilent });
                    this.ResetSaveQueuedTracksTimer(this.saveQueuedTracksQuickTimeoutSeconds);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not pause track with path='{0}'. Exception: {1}", this.CurrentTrack.Path, ex.Message);
            }
        }

        private async Task ResumeAsync()
        {
            try
            {
                IPlayer resumePlayer = this.player;

                if (resumePlayer != null)
                {
                    bool isResumed = false;
                    bool shouldFade = this.ShouldUsePlaybackFade();

                    if (shouldFade)
                    {
                        resumePlayer.SetVolume(0.0f);
                    }
                    else
                    {
                        this.CancelPlaybackFade();

                        if (!this.mute)
                        {
                            resumePlayer.SetVolume(this.Volume);
                        }
                    }

                    await Task.Run(() => isResumed = resumePlayer.Resume());

                    if (!object.ReferenceEquals(resumePlayer, this.player))
                    {
                        return;
                    }

                    if (isResumed)
                    {
                        this.PlaybackResumed(this, new EventArgs());

                        if (shouldFade)
                        {
                            await this.FadeCurrentPlayerVolumeAsync(this.Volume);
                        }
                    }
                    else
                    {
                        this.PlaybackStopped(this, new EventArgs());
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not resume track with path='{0}'. Exception: {1}", this.CurrentTrack.Path, ex.Message);
            }
        }

        private async Task PlayFirstAsync()
        {
            if (this.Queue.Count > 0)
            {
                TrackViewModel firstTrack = this.queueManager.FirstTrack();

                if (firstTrack.IsLocalFile && await this.blacklistService.IsInBlacklistAsync(firstTrack))
                {
                    await this.TryPlayNextAsync(false);
                }
                else { 
                    await this.TryPlayAsync(firstTrack);
                }
            }
        }

        private void StopPlayback()
        {
            this.CancelPlaybackFade();

            if (this.player != null)
            {
                // Remove the previous Stopped handler (not sure this is needed)
                this.player.PlaybackInterrupted -= this.PlaybackInterruptedHandler;
                this.player.PlaybackFinished -= this.PlaybackFinishedHandler;

                this.player.Stop();
                this.player.Dispose();
                this.player = null;
            }
        }

        private async Task StartPlaybackAsync(TrackViewModel track, AudioSource audioSource, bool silent = false)
        {
            // If we start playing a track, we need to make sure that
            // queued tracks are saved when the application is closed.
            if (this.queuePersistenceMode == QueuePersistenceMode.Durable)
            {
                this.isQueueChanged = true;
            }

            // Settings
            this.SetPlaybackSettings();

            // Play the Track from its runtime path (current or temporary)
            this.player = this.playerFactory.Create(this.hasMediaFoundationSupport);

            this.player.SetPlaybackSettings(this.Latency, this.EventMode, this.ExclusiveMode, this.activePreset.Bands, this.isEqualizerEnabled, this.UseAllAvailableChannels);
            this.player.SetVolume(silent || this.Mute || this.ShouldUsePlaybackFade(silent) ? 0.0f : this.Volume);

            // We need to set PlayingTrack before trying to play the Track.
            // So if we go into the Catch when trying to play the Track,
            // at least, the next time TryPlayNext is called, it will know that 
            // we already tried to play this track and it can find the next Track.
            this.queueManager.SetCurrentTrack(track);

            // Play the Track
            await Task.Run(() => this.player.Play(audioSource, this.audioDevice));

            // Start reporting progress
            this.progressTimer.Start();

            // Hook up the Stopped event
            this.player.PlaybackInterrupted += this.PlaybackInterruptedHandler;
            this.player.PlaybackFinished += this.PlaybackFinishedHandler;
        }

        private async Task<bool> TryPlayAsync(TrackViewModel track, bool isSilent = false, bool fadeOutCurrentTrack = true)
        {
            if (track == null)
            {
                return false;
            }

            long generation = Interlocked.Increment(ref this.playbackSourceGeneration);
            var cancellationTokenSource = new CancellationTokenSource();
            CancellationTokenSource previousCancellationTokenSource = Interlocked.Exchange(
                ref this.playbackSourceCancellationTokenSource,
                cancellationTokenSource);
            previousCancellationTokenSource?.Cancel();
            this.OnLoadingTrack(true);

            if (track.IsOnline)
            {
                this.SetBufferingProgress(true, 0.0);
            }
            else
            {
                this.SetBufferingProgress(false, 0.0);
            }

            IProgress<double> bufferingProgress = track.IsOnline
                ? new Progress<double>(value =>
                {
                    if (generation == Interlocked.Read(ref this.playbackSourceGeneration))
                    {
                        this.SetBufferingProgress(true, value);
                    }
                })
                : null;

            bool isPlaybackSuccess = false;
            bool fadeInAfterPlaybackSuccess = false;
            PlaybackFailedEventArgs playbackFailedEventArgs = null;
            bool transitionGateEntered = false;
            bool sourceWasForceRefreshed = false;

            try
            {
                PlaybackSourceResolution sourceResolution = await this.playbackSourceResolver.ResolveAsync(
                    track,
                    new PlaybackSourceRequest { BufferingProgress = bufferingProgress },
                    cancellationTokenSource.Token);

                if (generation != Interlocked.Read(ref this.playbackSourceGeneration) || cancellationTokenSource.IsCancellationRequested)
                {
                    return false;
                }

                if (sourceResolution == null || !sourceResolution.IsSuccess)
                {
                    if (track.IsOnline && sourceResolution != null &&
                        sourceResolution.FailureReason == PlaybackFailureReason.TemporaryDownloadFailed)
                    {
                        this.RestartBufferingProgress();
                        sourceResolution = await this.playbackSourceResolver.ResolveAsync(
                            track,
                            new PlaybackSourceRequest
                            {
                                ForceRefresh = true,
                                BufferingProgress = bufferingProgress
                            },
                            cancellationTokenSource.Token);
                        sourceWasForceRefreshed = true;
                    }

                    if (generation != Interlocked.Read(ref this.playbackSourceGeneration) || cancellationTokenSource.IsCancellationRequested)
                    {
                        return false;
                    }

                    if (sourceResolution != null && sourceResolution.IsSuccess)
                    {
                        // Continue with the single force-refreshed source.
                    }
                    else
                    {
                        playbackFailedEventArgs = this.CreatePlaybackFailure(sourceResolution);
                        return this.ReportPlaybackFailure(track, playbackFailedEventArgs);
                    }
                }

                if (track.IsOnline)
                {
                    this.SetBufferingProgress(true, 1.0);
                }

                await this.playbackTransitionGate.WaitAsync(cancellationTokenSource.Token);
                transitionGateEntered = true;

                if (generation != Interlocked.Read(ref this.playbackSourceGeneration) || cancellationTokenSource.IsCancellationRequested)
                {
                    return false;
                }

                // If a Track was playing, make sure it is now stopped.
                if (fadeOutCurrentTrack && this.ShouldUsePlaybackFade() && this.IsPlaying)
                {
                    await this.FadeCurrentPlayerVolumeAsync(0.0f);
                }
                else
                {
                    this.CancelPlaybackFade();
                }

                this.StopPlayback();

                try
                {
                    await this.StartPlaybackAsync(track, sourceResolution.AudioSource, isSilent);
                }
                catch (Exception firstOpenException)
                {
                    if (!track.IsOnline || cancellationTokenSource.IsCancellationRequested)
                    {
                        throw;
                    }

                    if (sourceWasForceRefreshed)
                    {
                        playbackFailedEventArgs = new PlaybackFailedEventArgs
                        {
                            FailureReason = PlaybackFailureReason.DecoderUnsupported,
                            MessageKey = "Language_Netease_Decoder_Unsupported",
                            Message = firstOpenException.Message,
                            StackTrace = firstOpenException.StackTrace
                        };

                        return this.ReportPlaybackFailure(track, playbackFailedEventArgs);
                    }

                    this.StopPlayback();
                    this.RestartBufferingProgress();
                    PlaybackSourceResolution refreshedResolution = await this.playbackSourceResolver.ResolveAsync(
                        track,
                        new PlaybackSourceRequest
                        {
                            ForceRefresh = true,
                            BufferingProgress = bufferingProgress
                        },
                        cancellationTokenSource.Token);

                    if (refreshedResolution == null || !refreshedResolution.IsSuccess)
                    {
                        playbackFailedEventArgs = this.CreatePlaybackFailure(refreshedResolution);
                        return this.ReportPlaybackFailure(track, playbackFailedEventArgs);
                    }

                    try
                    {
                        await this.StartPlaybackAsync(track, refreshedResolution.AudioSource, isSilent);
                    }
                    catch (Exception finalOpenException)
                    {
                        AppLog.Warning("Netease audio decoder failed after one forced source refresh. ErrorType={0}", finalOpenException.GetType().Name);
                        playbackFailedEventArgs = new PlaybackFailedEventArgs
                        {
                            FailureReason = PlaybackFailureReason.DecoderUnsupported,
                            MessageKey = "Language_Netease_Decoder_Unsupported",
                            Message = finalOpenException.Message,
                            StackTrace = finalOpenException.StackTrace
                        };

                        return this.ReportPlaybackFailure(track, playbackFailedEventArgs);
                    }

                    AppLog.Warning("The first Netease audio source open failed and was refreshed once. ErrorType={0}", firstOpenException.GetType().Name);
                }

                isPlaybackSuccess = true;

                // Playing was successful
                this.PlaybackSuccess(this, new PlaybackSuccessEventArgs()
                {
                    IsPlayingPreviousTrack = this.isPlayingPreviousTrack,
                    IsSilent = isSilent
                });

                // Set this to false again after raising the event. It is important to have a correct slide 
                // direction for cover art when the next Track is a file from double click in Windows.
                this.isPlayingPreviousTrack = false;
                AppLog.Info("Playing the file {0}. EventMode={1}, ExclusiveMode={2}, LoopMode={3}, Shuffle={4}", track.Path, this.EventMode, this.ExclusiveMode, this.LoopMode, this.shuffle);

                if (!isSilent)
                {
                    await this.SaveQueuedTracksNowAsync();
                }

                fadeInAfterPlaybackSuccess = this.ShouldUsePlaybackFade(isSilent);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                playbackFailedEventArgs = new PlaybackFailedEventArgs
                {
                    FailureReason = ex is FileNotFoundException ? PlaybackFailureReason.FileNotFound : PlaybackFailureReason.Unknown,
                    Message = ex.Message,
                    StackTrace = ex.StackTrace
                };

                return this.ReportPlaybackFailure(track, playbackFailedEventArgs);
            }
            finally
            {
                if (transitionGateEntered)
                {
                    this.playbackTransitionGate.Release();
                }

                if (object.ReferenceEquals(
                    Interlocked.CompareExchange(ref this.playbackSourceCancellationTokenSource, null, cancellationTokenSource),
                    cancellationTokenSource))
                {
                    this.OnLoadingTrack(false);
                }

                if (!isPlaybackSuccess && generation == Interlocked.Read(ref this.playbackSourceGeneration))
                {
                    this.SetBufferingProgress(false, 0.0);
                }

                cancellationTokenSource.Dispose();
            }

            if (fadeInAfterPlaybackSuccess)
            {
                await this.FadeCurrentPlayerVolumeAsync(this.Volume);
            }

            return isPlaybackSuccess;
        }

        private PlaybackFailedEventArgs CreatePlaybackFailure(PlaybackSourceResolution resolution)
        {
            if (resolution == null)
            {
                return new PlaybackFailedEventArgs { FailureReason = PlaybackFailureReason.Unknown };
            }

            return new PlaybackFailedEventArgs
            {
                FailureReason = resolution.FailureReason,
                MessageKey = resolution.MessageKey
            };
        }

        private bool ReportPlaybackFailure(TrackViewModel track, PlaybackFailedEventArgs eventArgs)
        {
            try
            {
                this.player?.Stop();
            }
            catch (Exception)
            {
                AppLog.Error("Could not stop the Player");
            }

            if (eventArgs.FailureReason != PlaybackFailureReason.Cancelled)
            {
                AppLog.Error("Could not play track {0}. FailureReason={1}, EventMode={2}, ExclusiveMode={3}, LoopMode={4}, Shuffle={5}, ErrorType={6}",
                    track.Path,
                    eventArgs.FailureReason,
                    this.EventMode,
                    this.ExclusiveMode,
                    this.LoopMode,
                    this.shuffle,
                    string.IsNullOrWhiteSpace(eventArgs.Message) ? "None" : "Decoder");
                this.PlaybackFailed(this, eventArgs);
            }

            return false;
        }

        private void CancelPlaybackSourceResolution()
        {
            Interlocked.Increment(ref this.playbackSourceGeneration);
            CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(
                ref this.playbackSourceCancellationTokenSource,
                null);

            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }

            if (this.isLoadingTrack)
            {
                this.OnLoadingTrack(false);
            }

            this.SetBufferingProgress(false, 0.0);
        }

        private void OnLoadingTrack(bool isLoadingTrack)
        {
            this.isLoadingTrack = isLoadingTrack;
            this.LoadingTrack(isLoadingTrack);
        }

        private async Task<bool> TryPlayPreviousAsync(bool ignoreLoopOne)
        {
            this.isPlayingPreviousTrack = true;

            if (this.GetCurrentTime.Seconds > 3)
            {
                // If we're more than 3 seconds into the Track, try to
                // jump to the beginning of the current Track.
                this.player.Skip(0);
                this.ResetSaveQueuedTracksTimer(this.saveQueuedTracksQuickTimeoutSeconds);
                return true;
            }

            // When "loop one" is enabled and ignoreLoopOne is true, act like "loop all".
            LoopMode loopMode = this.LoopMode == LoopMode.One && ignoreLoopOne ? LoopMode.All : this.LoopMode;

            TrackViewModel traversalAnchor = this.CurrentTrack;
            int maximumAttempts = Math.Min(3, this.Queue.Count);

            for (int attempt = 0; attempt < maximumAttempts; attempt++)
            {
                TrackViewModel previousTrack = await this.queueManager.PreviousTrackAsync(traversalAnchor, loopMode);

                if (previousTrack == null)
                {
                    this.Stop();
                    return true;
                }

                if (await this.TryPlayAsync(previousTrack))
                {
                    return true;
                }

                traversalAnchor = previousTrack;
            }

            this.Stop();
            return true;
        }

        private async Task<bool> TryPlayNextAsync(bool userHasRequestedNextTrack)
        {
            this.isPlayingPreviousTrack = false;

            LoopMode loopMode = this.LoopMode == LoopMode.One && userHasRequestedNextTrack ? LoopMode.All : this.LoopMode;

            // When "loop one" is enabled and userHasRequestedNextTrack is true, act like "loop all".
            bool returnToStart = SettingsClient.Get<bool>("Playback", "LoopWhenShuffle") & this.shuffle;

            TrackViewModel traversalAnchor = this.CurrentTrack;
            int maximumAttempts = Math.Min(3, this.Queue.Count);

            for (int attempt = 0; attempt < maximumAttempts; attempt++)
            {
                TrackViewModel nextTrack = await this.queueManager.NextTrackAsync(
                    traversalAnchor,
                    loopMode,
                    returnToStart,
                    this.shuffle);

                if (nextTrack == null)
                {
                    this.Stop();
                    return true;
                }

                traversalAnchor = nextTrack;

                if (nextTrack.IsLocalFile && await this.blacklistService.IsInBlacklistAsync(nextTrack))
                {
                    continue;
                }

                if (await this.TryPlayAsync(nextTrack, false, userHasRequestedNextTrack))
                {
                    return true;
                }
            }

            this.Stop();
            return true;
        }


        private void ProgressTimeoutHandler(object sender, ElapsedEventArgs e)
        {
            this.HandleProgress();
        }

        private void PlaybackInterruptedHandler(Object sender, PlaybackInterruptedEventArgs e)
        {
            // Playback was interrupted for some reason. Make sure we are in a correct state.
            // Use our context to trigger the work, because this event is fired on the Player's Playback thread.
            this.context.Post(new SendOrPostCallback((state) =>
            {
                AppLog.Info("Track interrupted: {0}", this.CurrentTrack.Path);
                this.Stop();
            }), null);
        }

        private void PlaybackFinishedHandler(Object sender, EventArgs e)
        {
            // Try to play the next Track from the list automatically
            // Use our context to trigger the work, because this event is fired on the Player's Playback thread.
            this.context.Post(new SendOrPostCallback(async (state) =>
            {
                AppLog.Info("Track finished: {0}", this.CurrentTrack.Path);
                if (this.CurrentTrack.IsLocalFile)
                {
                    await this.UpdatePlaybackCountersAsync(this.CurrentTrack.Path, true, false); // Increase PlayCount
                }
                await this.TryPlayNextAsync(false);
            }), null);
        }

        private async void SaveQueuedTracksTimeoutHandler(object sender, ElapsedEventArgs e)
        {
            await this.SaveQueuedTracksAsync();
        }

        private async Task<IList<RestoredQueuedTrack>> ConvertQueuedTracksToTrackViewModels(IList<QueuedTrack> queuedTracks)
        {
            var restoredQueuedTracks = new List<RestoredQueuedTrack>();

            if (queuedTracks == null || queuedTracks.Count == 0)
            {
                return restoredQueuedTracks;
            }

            IList<string> existingQueuedTrackPaths = queuedTracks.Where(x => System.IO.File.Exists(x.Path)).Select(x => x.Path).ToList();
            IList<Track> databaseTracks = existingQueuedTrackPaths.Count > 0 ? await this.trackRepository.GetTracksAsync(existingQueuedTrackPaths) : new List<Track>();

            foreach (QueuedTrack queuedTrack in queuedTracks)
            {
                Track track = databaseTracks.Where(x => x.SafePath.Equals(queuedTrack.SafePath)).FirstOrDefault();

                if (track == null && System.IO.File.Exists(queuedTrack.Path))
                {
                    // Queued track was not found as track in database: get metadata from file.
                    track = await MetadataUtils.Path2TrackAsync(queuedTrack.Path);
                }

                if (track == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(queuedTrack.QueueID))
                {
                    queuedTrack.QueueID = Guid.NewGuid().ToString();
                }

                TrackViewModel trackViewModel = this.container.ResolveTrackViewModel(track);
                trackViewModel.QueueID = queuedTrack.QueueID;

                restoredQueuedTracks.Add(new RestoredQueuedTrack
                {
                    QueuedTrack = queuedTrack,
                    TrackViewModel = trackViewModel
                });
            }

            return restoredQueuedTracks;
        }

        private async void GetSavedQueuedTracks()
        {
            if (!this.canGetSavedQueuedTracks)
            {
                AppLog.Info("Aborting getting of saved queued tracks");
                return;
            }

            try
            {
                AppLog.Info("Getting saved queued tracks");
                IList<QueuedTrack> savedQueuedTracks = await this.queuedTrackRepository.GetSavedQueuedTracksAsync();
                IList<RestoredQueuedTrack> restoredQueuedTracks = await this.ConvertQueuedTracksToTrackViewModels(savedQueuedTracks);
                RestoredQueuedTrack playingRestoredQueuedTrack = restoredQueuedTracks.Where(x => x.QueuedTrack.IsPlaying == 1).FirstOrDefault();
                IList<TrackViewModel> existingTrackViewModels = restoredQueuedTracks.Select(x => x.TrackViewModel).ToList();
                IList<long> shuffleOrderIDs = restoredQueuedTracks.Select(x => x.QueuedTrack.ShuffleOrderID).ToList();

                await this.EnqueueAlwaysAsync(existingTrackViewModels, shuffleOrderIDs);

                if (!SettingsClient.Get<bool>("Startup", "RememberLastPlayedTrack"))
                {
                    return;
                }

                if (!this.canGetSavedQueuedTracks)
                {
                    AppLog.Info("Aborting getting of saved queued tracks");
                    return;
                }

                if (playingRestoredQueuedTrack == null)
                {
                    return;
                }

                TrackViewModel playingTrackViewModel = playingRestoredQueuedTrack.TrackViewModel;

                if (playingTrackViewModel == null)
                {
                    return;
                }

                int progressSeconds = Convert.ToInt32(playingRestoredQueuedTrack.QueuedTrack.ProgressSeconds);

                try
                {
                    AppLog.Info("Starting track {0} paused", playingTrackViewModel.Path);
                    await this.StartTrackPausedAsync(playingTrackViewModel, progressSeconds);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Could not set the playing track. Exception: {0}", ex.Message);
                    this.Stop(); // Should not be required, but just in case.
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not get saved queued tracks. Exception: {0}", ex.Message);
            }
        }

        private async Task StartTrackPausedAsync(TrackViewModel track, int progressSeconds)
        {
            if (await this.TryPlayAsync(track, true))
            {
                await this.PauseAsync(true);
                this.player.Skip(progressSeconds);
                await Task.Delay(200); // Small delay before unmuting

                if (!this.mute)
                {
                    this.player.SetVolume(this.Volume);
                }

                PlaybackProgressChanged(this, new EventArgs());
            }
        }

        private void HandleProgress()
        {
            if (this.player != null && this.player.CanStop)
            {
                TimeSpan totalTime = this.player.GetTotalTime();
                TimeSpan currentTime = this.player.GetCurrentTime();

                this.Progress = currentTime.TotalMilliseconds / totalTime.TotalMilliseconds;
                this.QueuePlaybackPositionSaveIfNeeded(currentTime);
            }
            else
            {
                this.Progress = 0.0;
            }

            PlaybackProgressChanged(this, new EventArgs());
        }

        private async Task EnqueueAlwaysAsync(IList<TrackViewModel> tracks, IList<long> shuffleOrderIDs)
        {
            this.EnterDurableQueueMode();

            if (await this.queueManager.ClearQueueAsync())
            {
                await this.queueManager.EnqueueRestoredAsync(tracks, shuffleOrderIDs, this.shuffle);

                this.QueueChanged(this, new EventArgs());
                this.ResetSaveQueuedTracksTimer(); // Save queued tracks to the database
            }
        }

        private async Task EnqueueAsync(IList<TrackViewModel> tracks, bool shuffle)
        {
            this.EnterDurableQueueMode();

            if (await this.queueManager.ClearQueueAsync())
            {
                await this.queueManager.EnqueueAsync(tracks, shuffle);
                this.durableShuffle = shuffle;

                if (shuffle != this.shuffle)
                {
                    this.shuffle = shuffle;
                    this.PlaybackShuffleChanged(this, new EventArgs());
                }
            }

            this.QueueChanged(this, new EventArgs());
            this.ResetSaveQueuedTracksTimer(); // Save queued tracks to the database
        }

        private async Task EnqueueTrackEntitiesAsync(IList<Track> tracks, bool shuffle)
        {
            this.EnterDurableQueueMode();

            if (await this.queueManager.ClearQueueAsync())
            {
                await this.queueManager.EnqueueAsync(tracks, shuffle, (track) => this.container.ResolveTrackViewModel(track));
                this.durableShuffle = shuffle;

                if (shuffle != this.shuffle)
                {
                    this.shuffle = shuffle;
                    this.PlaybackShuffleChanged(this, new EventArgs());
                }
            }

            this.QueueChanged(this, new EventArgs());
            this.ResetSaveQueuedTracksTimer(); // Save queued tracks to the database
        }

        private void ResetSaveQueuedTracksTimer()
        {
            this.ResetSaveQueuedTracksTimer(this.saveQueuedTracksTimeoutSeconds);
        }

        private void ResetSaveQueuedTracksTimer(double timeoutSeconds)
        {
            this.saveQueuedTracksTimer.Stop();

            if (this.queuePersistenceMode == QueuePersistenceMode.Transient)
            {
                return;
            }

            this.saveQueuedTracksTimer.Interval = TimeSpan.FromSeconds(timeoutSeconds).TotalMilliseconds;
            this.isQueueChanged = true;
            this.saveQueuedTracksTimer.Start();
        }

        private void EnterDurableQueueMode()
        {
            this.queuePersistenceMode = QueuePersistenceMode.Durable;
            this.SetQueueContext(PlaybackQueueContext.Default);
        }

        private bool CanAddTracksToCurrentQueue(IList<TrackViewModel> tracks)
        {
            if (tracks == null || tracks.Count == 0 || tracks.Any(x => x == null))
            {
                return false;
            }

            return this.queuePersistenceMode == QueuePersistenceMode.Transient
                ? tracks.All(x => x.IsOnline) && this.Queue.All(x => x != null && x.IsOnline)
                : tracks.All(x => x.IsLocalFile);
        }

        private void SetQueueContext(PlaybackQueueContext context)
        {
            this.queueContext = context;
            bool contextShuffle = this.GetQueueContextShuffle(context);

            if (this.shuffle != contextShuffle)
            {
                this.shuffle = contextShuffle;
                this.PlaybackShuffleChanged(this, EventArgs.Empty);
            }
        }

        private bool GetQueueContextShuffle(PlaybackQueueContext context)
        {
            switch (context)
            {
                case PlaybackQueueContext.NeteaseDailyRecommendations:
                    return SettingDefaults.GetOrAdd<bool>("Netease", "DailyRecommendationsShuffle", false);
                case PlaybackQueueContext.NeteaseIntelligenceRecommendations:
                    return SettingDefaults.GetOrAdd<bool>("Netease", "IntelligenceRecommendationsShuffle", false);
                case PlaybackQueueContext.NeteasePersonalFm:
                    return false;
                default:
                    return this.durableShuffle;
            }
        }

        private void SetBufferingProgress(bool show, double progressValue)
        {
            double normalized = Math.Max(0.0, Math.Min(1.0, progressValue));

            if (show && this.showBufferingProgress)
            {
                normalized = Math.Max(this.bufferingProgress, normalized);
            }

            if (this.showBufferingProgress == show && Math.Abs(this.bufferingProgress - normalized) < 0.0001)
            {
                return;
            }

            this.showBufferingProgress = show;
            this.bufferingProgress = normalized;
            this.PlaybackBufferingProgressChanged(this, EventArgs.Empty);
        }

        private void RestartBufferingProgress()
        {
            this.showBufferingProgress = true;
            this.bufferingProgress = 0.0;
            this.PlaybackBufferingProgressChanged(this, EventArgs.Empty);
        }

        private void ResetSavePlaybackCountersTimer()
        {
            this.savePlaybackCountersTimer.Stop();
            this.savePlaybackCountersTimer.Start();
        }

        private void SetPlaybackSettings()
        {
            this.isLoadingSettings = true;
            this.UseAllAvailableChannels = SettingsClient.Get<bool>("Playback", "WasapiUseAllAvailableChannels");
            this.LoopMode = (LoopMode)SettingsClient.Get<int>("Playback", "LoopMode");
            this.Latency = SettingsClient.Get<int>("Playback", "AudioLatency");
            this.Volume = SettingsClient.Get<float>("Playback", "Volume");
            this.mute = SettingsClient.Get<bool>("Playback", "Mute");
            this.durableShuffle = SettingsClient.Get<bool>("Playback", "Shuffle");
            this.shuffle = this.GetQueueContextShuffle(this.queueContext);
            this.EventMode = false;
            //this.EventMode = SettingsClient.Get<bool>("Playback", "WasapiEventMode");
            //this.ExclusiveMode = false;
            this.ExclusiveMode = SettingsClient.Get<bool>("Playback", "WasapiExclusiveMode");
            this.isLoadingSettings = false;
        }

        private async Task SetAudioDeviceAsync()
        {
            this.audioDevice = await this.GetSavedAudioDeviceAsync();
        }
    }
}
