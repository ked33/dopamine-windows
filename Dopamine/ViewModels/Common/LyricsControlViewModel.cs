using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Api.Lyrics;
using Dopamine.Core.Base;
using Dopamine.Core.Enums;
using Dopamine.Core.Helpers;
using Dopamine.Core.Prism;
using Dopamine.Data.Entities;
using Dopamine.Data.Metadata;
using Dopamine.Services.I18n;
using Dopamine.Services.Metadata;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using Dopamine.Services.Shell;
using Dopamine.ViewModels.Common.Base;
using Prism.Commands;
using Prism.Events;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;
using Prism.Ioc;
using Dopamine.Services.Entities;
using System.Windows;

namespace Dopamine.ViewModels.Common
{
    public class LyricsControlViewModel : ContextMenuViewModelBase
    {
        private IContainerProvider container;
        private ILocalizationInfo info;
        private IMetadataService metadataService;
        private IPlaybackService playbackService;
        private IAppVisibilityService appVisibilityService;
        private II18nService i18NService;
        private LyricsViewModel lyricsViewModel;
        private TrackViewModel previousTrack;
        private int contentSlideInFrom;
        private Timer highlightTimer = new Timer();
        private int highlightTimerIntervalMilliseconds = 100;
        private IEventAggregator eventAggregator;
        private Object lockObject = new Object();
        private Timer updateLyricsAfterEditingTimer = new Timer();
        private int updateLyricsAfterEditingTimerIntervalMilliseconds = 100;
        private bool isDownloadingLyrics;
        private bool canHighlight;
        private Timer refreshTimer = new Timer();
        private int refreshTimerIntervalMilliseconds = 500;
        private bool isNowPlayingPageActive;
        private bool isNowPlayingLyricsPageActive;
        private LyricsFactory lyricsFactory;
        private INeteaseMusicService neteaseMusicService;
        private CancellationTokenSource lyricsCancellationTokenSource;
        private int lyricsGeneration;

        public DelegateCommand RefreshLyricsCommand { get; set; }

        public int ContentSlideInFrom
        {
            get { return this.contentSlideInFrom; }
            set { SetProperty<int>(ref this.contentSlideInFrom, value); }
        }

        public LyricsViewModel LyricsViewModel
        {
            get { return this.lyricsViewModel; }
            set { SetProperty<LyricsViewModel>(ref this.lyricsViewModel, value); }
        }

        public bool IsDownloadingLyrics
        {
            get { return this.isDownloadingLyrics; }
            set
            {
                SetProperty<bool>(ref this.isDownloadingLyrics, value);
                this.RefreshLyricsCommand.RaiseCanExecuteChanged();
            }
        }

        public LyricsControlViewModel(IContainerProvider container) : base(container)
        {
            this.container = container;
            this.info = container.Resolve<ILocalizationInfo>();
            this.metadataService = container.Resolve<IMetadataService>();
            this.playbackService = container.Resolve<IPlaybackService>();
            this.appVisibilityService = container.Resolve<IAppVisibilityService>();
            this.eventAggregator = container.Resolve<IEventAggregator>();
            this.i18NService = container.Resolve<II18nService>();
            this.neteaseMusicService = container.Resolve<INeteaseMusicService>();

            this.highlightTimer.Interval = this.highlightTimerIntervalMilliseconds;
            this.highlightTimer.Elapsed += HighlightTimer_Elapsed;

            this.updateLyricsAfterEditingTimer.Interval = this.updateLyricsAfterEditingTimerIntervalMilliseconds;
            this.updateLyricsAfterEditingTimer.Elapsed += UpdateLyricsAfterEditingTimer_Elapsed;

            this.refreshTimer.Interval = this.refreshTimerIntervalMilliseconds;
            this.refreshTimer.Elapsed += RefreshTimer_Elapsed;

            this.playbackService.PlaybackPaused += (_, __) => this.StopHighlighting();
            this.playbackService.PlaybackResumed += (_, __) => this.StartHighlightingIfAllowed();
            this.appVisibilityService.VisibilityChanged += (_, __) => this.HandleVisibilityChanged();

            this.metadataService.MetadataChanged += (_) => this.RestartRefreshTimer();

            I18NService_LanguageChanged(null, null);
            this.i18NService.LanguageChanged += I18NService_LanguageChanged;                  

            SettingsClient.SettingChanged += (_, e) =>
            {
                if (SettingsClient.IsSettingChanged(e, "Lyrics", "DownloadLyrics"))
                {
                    if ((bool)e.Entry.Value)
                    {
                        this.RestartRefreshTimer();
                    }
                }
            };

            this.isNowPlayingPageActive = SettingsClient.Get<bool>("FullPlayer", "IsNowPlayingSelected");
            this.isNowPlayingLyricsPageActive = ((NowPlayingSubPage)SettingsClient.Get<int>("FullPlayer", "SelectedNowPlayingSubPage")) == NowPlayingSubPage.Lyrics;

            this.eventAggregator.GetEvent<IsNowPlayingPageActiveChanged>().Subscribe(isNowPlayingPageActive =>
            {
                this.isNowPlayingPageActive = isNowPlayingPageActive;

                if (!isNowPlayingPageActive)
                {
                    this.CancelLyricsRequest();
                }

                this.RestartRefreshTimer();
            });

            this.eventAggregator.GetEvent<IsNowPlayingSubPageChanged>().Subscribe(tuple =>
            {
                this.isNowPlayingLyricsPageActive = tuple.Item2 == NowPlayingSubPage.Lyrics;

                if (!this.isNowPlayingLyricsPageActive)
                {
                    this.CancelLyricsRequest();
                }

                this.RestartRefreshTimer();
            });

            this.RefreshLyricsCommand = new DelegateCommand(() => this.RestartRefreshTimer(), () => !this.IsDownloadingLyrics);
            ApplicationCommands.RefreshLyricsCommand.RegisterCommand(this.RefreshLyricsCommand);

            this.playbackService.PlaybackSuccess += (_, e) =>
            {
                this.ContentSlideInFrom = e.IsPlayingPreviousTrack ? -30 : 30;
                this.CancelLyricsRequest();
                this.RestartRefreshTimer();
            };

            this.ClearLyrics(null); // Makes sure the loading animation can be shown even at first start

            this.RestartRefreshTimer();
        }

        private void I18NService_LanguageChanged(object sender, EventArgs e)
        {
            this.lyricsFactory = new LyricsFactory(SettingsClient.Get<int>("Lyrics", "TimeoutSeconds"),
                SettingsClient.Get<string>("Lyrics", "Providers"), this.info);
        }

        private void RefreshTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.refreshTimer.Stop();
            this.RefreshLyricsAsync(this.playbackService.CurrentTrack);
        }

        private void UpdateLyricsAfterEditingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.updateLyricsAfterEditingTimer.Stop();
            this.RefreshLyricsAsync(this.playbackService.CurrentTrack);
        }

        private async void HighlightTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.highlightTimer.Stop();

            if (!this.CanHighlightNow)
            {
                this.StopHighlighting();
                return;
            }

            await HighlightLyricsLineAsync();

            if (this.CanHighlightNow)
            {
                this.highlightTimer.Start();
            }
        }

        private void RestartRefreshTimer()
        {
            this.refreshTimer.Stop();
            this.refreshTimer.Start();
        }

        private void StartHighlighting()
        {
            this.highlightTimer.Start();
            this.canHighlight = true;
        }

        private void StartHighlightingIfAllowed()
        {
            if (!this.CanHighlightNow)
            {
                this.StopHighlighting();
                return;
            }

            this.StartHighlighting();
        }

        private void StopHighlighting()
        {
            this.canHighlight = false;
            this.highlightTimer.Stop();
        }

        private bool CanRefreshLyricsNow
        {
            get
            {
                return this.isNowPlayingPageActive &&
                    this.isNowPlayingLyricsPageActive &&
                    !this.appVisibilityService.IsBackgroundPlaybackMode;
            }
        }

        private bool CanHighlightNow
        {
            get
            {
                return this.CanRefreshLyricsNow &&
                    this.playbackService.IsPlaying;
            }
        }

        private async void HandleVisibilityChanged()
        {
            if (!this.CanRefreshLyricsNow)
            {
                this.StopHighlighting();
                return;
            }

            this.RestartRefreshTimer();

            if (this.CanHighlightNow)
            {
                await HighlightLyricsLineAsync();
                this.StartHighlighting();
            }
            else
            {
                this.StopHighlighting();
            }
        }

        private void ClearLyrics(TrackViewModel track)
        {
            this.LyricsViewModel = new LyricsViewModel(this.container, track);
        }

        private async void RefreshLyricsAsync(TrackViewModel track)
        {
            if (!this.isNowPlayingPageActive || !this.isNowPlayingLyricsPageActive) return;
            if (this.appVisibilityService.IsBackgroundPlaybackMode) return;
            if (track == null) return;

            int generation = Interlocked.Increment(ref this.lyricsGeneration);
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            CancellationTokenSource previousCancellationTokenSource = Interlocked.Exchange(
                ref this.lyricsCancellationTokenSource,
                cancellationTokenSource);
            previousCancellationTokenSource?.Cancel();

            this.previousTrack = track;

            this.StopHighlighting();

            if (track.IsOnline)
            {
                await this.RefreshNeteaseLyricsAsync(track, generation, cancellationTokenSource);
                return;
            }

            FileMetadata fmd = await this.metadataService.GetFileMetadataAsync(track.Path);

            if (generation != this.lyricsGeneration)
            {
                cancellationTokenSource.Dispose();
                return;
            }

            await Task.Run(() =>
            {
                // If we're in editing mode, delay changing the lyrics.
                if (this.LyricsViewModel != null && this.LyricsViewModel.IsEditing)
                {
                    this.updateLyricsAfterEditingTimer.Start();
                    return;
                }

                // No FileMetadata available: clear the lyrics.
                if (fmd == null)
                {
                    this.ClearLyrics(track);
                    return;
                }
            });

            try
            {
                Lyrics lyrics = null;
                bool mustDownloadLyrics = false;

                await Task.Run(async () =>
                {
                    // Try to get lyrics from the audio file
                    lyrics = new Lyrics(fmd != null && fmd.Lyrics.Value != null ? fmd.Lyrics.Value : String.Empty, string.Empty);
                    lyrics.SourceType = SourceTypeEnum.Audio;

                    // If the audio file has no lyrics, try to find lyrics in a local lyrics file.
                    if (!lyrics.HasText)
                    {
                        var lrcFile = Path.Combine(Path.GetDirectoryName(fmd.Path), Path.GetFileNameWithoutExtension(fmd.Path) + FileFormats.LRC);

                        if (File.Exists(lrcFile))
                        {
                            using (var fs = new FileStream(lrcFile, FileMode.Open, FileAccess.Read))
                            {
                                using (var sr = new StreamReader(fs, Encoding.Default))
                                {
                                    lyrics = new Lyrics(await sr.ReadToEndAsync(), String.Empty);
                                    if (lyrics.HasText)
                                    {
                                        lyrics.SourceType = SourceTypeEnum.Lrc;
                                        return;
                                    }
                                }
                            }
                        }

                        // If we still don't have lyrics and the user enabled automatic download of lyrics: try to download them online.
                        if (SettingsClient.Get<bool>("Lyrics", "DownloadLyrics"))
                        {
                            string artist = fmd.Artists != null && fmd.Artists.Values != null && fmd.Artists.Values.Length > 0 ? fmd.Artists.Values[0] : string.Empty;
                            string title = fmd.Title != null && fmd.Title.Value != null ? fmd.Title.Value : string.Empty;

                            if (!string.IsNullOrWhiteSpace(artist) & !string.IsNullOrWhiteSpace(title)) mustDownloadLyrics = true;
                        }
                    }
                });

                // No lyrics were found in the file: try to download.
                if (mustDownloadLyrics)
                {
                    this.IsDownloadingLyrics = true;

                    try
                    {
                        lyrics = await this.lyricsFactory.GetLyricsAsync(fmd.Artists.Values[0], fmd.Title.Value);
                        lyrics.SourceType = SourceTypeEnum.Online;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Could not get lyrics online {0}. Exception: {1}", track.Path, ex.Message);
                    }

                    this.IsDownloadingLyrics = false;
                }

                await Task.Run(() =>
                            {
                                if (generation != this.lyricsGeneration)
                                {
                                    return;
                                }

                                this.LyricsViewModel = new LyricsViewModel(container, track);
                                this.LyricsViewModel.SetLyrics(lyrics);
                            });
            }
            catch (Exception ex)
            {
                this.IsDownloadingLyrics = false;
                AppLog.Error("Could not show lyrics for Track {0}. Exception: {1}", track.Path, ex.Message);
                this.ClearLyrics(track);
                this.CompleteLyricsRequest(generation, cancellationTokenSource);
                return;
            }

            this.StartHighlightingIfAllowed();
            this.CompleteLyricsRequest(generation, cancellationTokenSource);
        }

        private async Task RefreshNeteaseLyricsAsync(
            TrackViewModel track,
            int generation,
            CancellationTokenSource cancellationTokenSource)
        {
            this.IsDownloadingLyrics = true;

            try
            {
                NeteaseLyricResult result = await this.neteaseMusicService.GetLyricsAsync(
                    track.SourceInfo.RemoteId,
                    cancellationTokenSource.Token);

                if (generation != this.lyricsGeneration || cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                var lyrics = result.IsSuccess
                    ? new Lyrics(result.Lyric ?? string.Empty, "Netease Cloud Music", SourceTypeEnum.Online)
                    : new Lyrics();

                this.Dispatch(() =>
                {
                    if (generation != this.lyricsGeneration)
                    {
                        return;
                    }

                    this.LyricsViewModel = new LyricsViewModel(this.container, track);
                    this.LyricsViewModel.SetLyrics(lyrics);
                    this.LyricsViewModel.KaraokeLyrics = result.KaraokeLyric ?? string.Empty;
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not show Netease lyrics. SongId={0}, ErrorType={1}",
                    track.SourceInfo.RemoteId,
                    ex.GetType().Name);
                this.Dispatch(() => this.ClearLyrics(track));
            }
            finally
            {
                if (generation == this.lyricsGeneration)
                {
                    this.IsDownloadingLyrics = false;
                    Interlocked.CompareExchange(ref this.lyricsCancellationTokenSource, null, cancellationTokenSource);

                    this.StartHighlightingIfAllowed();
                }

                cancellationTokenSource.Dispose();
            }
        }

        private async Task HighlightLyricsLineAsync()
        {
            if (!this.canHighlight)
            {
                return;
            }

            if (this.LyricsViewModel == null || this.LyricsViewModel.LyricsLines == null)
            {
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < this.LyricsViewModel.LyricsLines.Count; i++)
                    {
                        if (!this.canHighlight)
                        {
                            break;
                        }

                        double progressTime = this.playbackService.GetCurrentTime.TotalMilliseconds;
                        double lyricsLineTime = this.LyricsViewModel.LyricsLines[i].Time.TotalMilliseconds;
                        double nextLyricsLineTime = 0;

                        int j = 1;

                        while (i + j < this.LyricsViewModel.LyricsLines.Count && nextLyricsLineTime <= lyricsLineTime)
                        {
                            if (!this.canHighlight)
                            {
                                break;
                            }

                            nextLyricsLineTime = this.LyricsViewModel.LyricsLines[i + j].Time.TotalMilliseconds;
                            j++;
                        }

                        if (progressTime >= lyricsLineTime & (nextLyricsLineTime >= progressTime | nextLyricsLineTime == 0))
                        {
                            this.LyricsViewModel.LyricsLines[i].IsHighlighted = true;

                            if (this.LyricsViewModel.AutomaticScrolling & this.canHighlight)
                            {
                                this.eventAggregator.GetEvent<ScrollToHighlightedLyricsLine>().Publish(null);
                            }
                        }
                        else
                        {
                            this.LyricsViewModel.LyricsLines[i].IsHighlighted = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Could not highlight the lyrics. Exception: {0}", ex.Message);
                }

            });
        }

        private void CompleteLyricsRequest(int generation, CancellationTokenSource cancellationTokenSource)
        {
            if (generation == this.lyricsGeneration)
            {
                Interlocked.CompareExchange(ref this.lyricsCancellationTokenSource, null, cancellationTokenSource);
            }

            cancellationTokenSource.Dispose();
        }

        private void CancelLyricsRequest()
        {
            Interlocked.Increment(ref this.lyricsGeneration);
            CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(
                ref this.lyricsCancellationTokenSource,
                null);
            cancellationTokenSource?.Cancel();
            this.Dispatch(() => this.IsDownloadingLyrics = false);
        }

        private void Dispatch(Action action)
        {
            if (Application.Current == null || Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action);
            }
        }

        protected override void SearchOnline(string id)
        {
            // No implementation required here
        }
    }
}
