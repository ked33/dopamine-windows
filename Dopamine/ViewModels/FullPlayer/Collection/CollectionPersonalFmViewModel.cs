using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Base;
using Dopamine.Core.Logging;
using Dopamine.Core.Utils;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public sealed class CollectionPersonalFmViewModel : BindableBase
    {
        private readonly INeteasePersonalFmService personalFmService;
        private readonly INeteaseSessionService sessionService;
        private readonly INeteaseMusicService musicService;
        private readonly IPlaybackService playbackService;
        private bool isLoaded;
        private bool currentTrackRefreshPending;
        private CancellationTokenSource operationCancellationTokenSource;
        private CancellationTokenSource likeStatusCancellationTokenSource;
        private CancellationTokenSource lyricsCancellationTokenSource;
        private string likeStatusSongId;
        private string lyricsSongId;
        private bool isLikeStatusLoading;
        private bool isLyricsLoading;
        private bool currentSongIsLiked;
        private string karaokeLyrics;
        private string fallbackLyrics;
        private string karaokeTranslationLyrics;
        private string fallbackTranslationLyrics;
        private bool isLikeOperationRunning;
        private string actionErrorMessage;

        public CollectionPersonalFmViewModel(
            INeteasePersonalFmService personalFmService,
            INeteaseSessionService sessionService,
            INeteaseMusicService musicService,
            IPlaybackService playbackService)
        {
            this.personalFmService = personalFmService;
            this.sessionService = sessionService;
            this.musicService = musicService;
            this.playbackService = playbackService;

            this.LoadedCommand = new DelegateCommand(this.Loaded);
            this.UnloadedCommand = new DelegateCommand(this.Unloaded);
            this.StartCommand = new DelegateCommand(
                () => this.StartAsync(),
                () => this.IsLoggedIn && !this.IsBusy && !this.IsActive);
            this.SkipCommand = new DelegateCommand(
                () => this.SkipAsync(),
                () => this.IsActive && !this.IsBusy);
            this.DislikeCommand = new DelegateCommand(
                () => this.DislikeAsync(),
                () => this.IsActive && !this.IsBusy && this.personalFmService.CurrentTrack != null);
            this.ToggleLikeCommand = new DelegateCommand(
                () => this.ToggleLikeAsync(),
                () => this.CanToggleLike());
        }

        public DelegateCommand LoadedCommand { get; private set; }

        public DelegateCommand UnloadedCommand { get; private set; }

        public DelegateCommand StartCommand { get; private set; }

        public DelegateCommand SkipCommand { get; private set; }

        public DelegateCommand DislikeCommand { get; private set; }

        public DelegateCommand ToggleLikeCommand { get; private set; }

        public bool IsLoggedIn => this.sessionService.State == NeteaseSessionState.SignedIn;

        public bool IsActive => this.personalFmService.IsActive;

        public bool IsBusy => this.personalFmService.IsBusy;

        public bool IsLoggedOutVisible => !this.IsLoggedIn;

        public bool IsIdleVisible => this.IsLoggedIn && !this.IsActive;

        public bool IsActiveVisible => this.IsLoggedIn && this.IsActive;

        public string CurrentTrackTitle => this.personalFmService.CurrentTrack?.TrackTitle ??
            ResourceUtils.GetString("Language_Netease_Personal_Fm_Loading");

        public string CurrentTrackSubtitle
        {
            get
            {
                string artist = this.personalFmService.CurrentTrack?.ArtistName ?? string.Empty;
                string album = this.personalFmService.CurrentTrack?.AlbumTitle ?? string.Empty;
                return string.Join(" · ", new[] { artist, album }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
        }

        public string BufferedText => string.Format(
            ResourceUtils.GetString("Language_Netease_Personal_Fm_Buffered"),
            this.personalFmService.BufferedTrackCount);

        public string LikeButtonText => ResourceUtils.GetString(this.IsLikeStatusLoading
            ? "Language_Netease_Like_Status_Loading"
            : this.CurrentSongIsLiked
                ? "Language_Netease_Unlike"
                : "Language_Netease_Like");

        public bool IsLikeStatusLoading
        {
            get { return this.isLikeStatusLoading; }
            private set
            {
                if (SetProperty<bool>(ref this.isLikeStatusLoading, value))
                {
                    RaisePropertyChanged(nameof(this.LikeButtonText));
                    this.ToggleLikeCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CurrentSongIsLiked
        {
            get { return this.currentSongIsLiked; }
            private set
            {
                if (SetProperty<bool>(ref this.currentSongIsLiked, value))
                {
                    RaisePropertyChanged(nameof(this.LikeButtonText));
                }
            }
        }

        public bool IsLyricsLoading
        {
            get { return this.isLyricsLoading; }
            private set { SetProperty<bool>(ref this.isLyricsLoading, value); }
        }

        public string KaraokeLyrics
        {
            get { return this.karaokeLyrics; }
            private set { SetProperty<string>(ref this.karaokeLyrics, value); }
        }

        public string FallbackLyrics
        {
            get { return this.fallbackLyrics; }
            private set
            {
                if (SetProperty<string>(ref this.fallbackLyrics, value))
                {
                    RaisePropertyChanged(nameof(this.HasLyrics));
                    RaisePropertyChanged(nameof(this.IsLyricsEmpty));
                }
            }
        }

        public bool HasLyrics => !string.IsNullOrWhiteSpace(this.KaraokeLyrics) ||
            !string.IsNullOrWhiteSpace(this.FallbackLyrics);

        public string KaraokeTranslationLyrics
        {
            get { return this.karaokeTranslationLyrics; }
            private set { SetProperty<string>(ref this.karaokeTranslationLyrics, value); }
        }

        public string FallbackTranslationLyrics
        {
            get { return this.fallbackTranslationLyrics; }
            private set { SetProperty<string>(ref this.fallbackTranslationLyrics, value); }
        }

        public bool IsLyricsEmpty => !this.IsLyricsLoading && !this.HasLyrics;

        public string ErrorMessage => !string.IsNullOrWhiteSpace(this.actionErrorMessage)
            ? this.actionErrorMessage
            : this.personalFmService.Error == null
                ? string.Empty
                : ResourceUtils.GetString(this.personalFmService.Error.MessageKey);

        public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

        private void Loaded()
        {
            if (this.isLoaded)
            {
                return;
            }

            this.isLoaded = true;
            this.personalFmService.StateChanged += this.PersonalFmService_StateChanged;
            this.sessionService.SessionChanged += this.SessionService_SessionChanged;
            this.playbackService.PlayingTrackChanged += this.PlaybackService_PlayingTrackChanged;
            this.playbackService.PlaybackSuccess += this.PlaybackService_PlaybackSuccess;
            this.RefreshCurrentTrackData();
        }

        private void Unloaded()
        {
            if (!this.isLoaded)
            {
                return;
            }

            this.isLoaded = false;
            this.personalFmService.StateChanged -= this.PersonalFmService_StateChanged;
            this.sessionService.SessionChanged -= this.SessionService_SessionChanged;
            this.playbackService.PlayingTrackChanged -= this.PlaybackService_PlayingTrackChanged;
            this.playbackService.PlaybackSuccess -= this.PlaybackService_PlaybackSuccess;
            this.currentTrackRefreshPending = false;
            this.CancelOperation();
            this.CancelLikeStatusLookup();
            this.CancelLyricsLookup();
        }

        private async void StartAsync()
        {
            CancellationToken token = this.BeginOperation();
            await this.personalFmService.StartAsync(token);
            this.RefreshState();
        }

        private async void SkipAsync()
        {
            CancellationToken token = this.BeginOperation();
            await this.personalFmService.SkipAsync(token);
            this.RefreshState();
        }

        private async void DislikeAsync()
        {
            CancellationToken token = this.BeginOperation();
            await this.personalFmService.DislikeCurrentAsync(token);
            this.RefreshState();
        }

        private CancellationToken BeginOperation()
        {
            this.CancelOperation();
            this.operationCancellationTokenSource = new CancellationTokenSource();
            return this.operationCancellationTokenSource.Token;
        }

        private void CancelOperation()
        {
            CancellationTokenSource source = Interlocked.Exchange(
                ref this.operationCancellationTokenSource,
                null);
            source?.Cancel();
            source?.Dispose();
        }

        private void PersonalFmService_StateChanged(object sender, EventArgs e)
        {
            this.ScheduleCurrentTrackRefresh();
        }

        private void PlaybackService_PlayingTrackChanged(object sender, EventArgs e)
        {
            this.ScheduleCurrentTrackRefresh();
        }

        private void PlaybackService_PlaybackSuccess(object sender, PlaybackSuccessEventArgs e)
        {
            this.ScheduleCurrentTrackRefresh();
        }

        private void ScheduleCurrentTrackRefresh()
        {
            if (!this.isLoaded || this.currentTrackRefreshPending)
            {
                return;
            }

            this.currentTrackRefreshPending = true;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                this.currentTrackRefreshPending = false;
                this.RefreshCurrentTrackData();
                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                this.currentTrackRefreshPending = false;
                if (this.isLoaded)
                {
                    this.RefreshCurrentTrackData();
                }
            }));
        }

        private void RefreshCurrentTrackData()
        {
            this.RefreshState();
            this.RefreshLikeStatusAsync();
            this.RefreshLyricsAsync();
        }

        private void SessionService_SessionChanged(object sender, EventArgs e)
        {
            this.Dispatch(() =>
            {
                if (!this.IsLoggedIn)
                {
                    this.CancelLikeStatusLookup();
                    this.likeStatusSongId = null;
                    this.CurrentSongIsLiked = false;
                    this.CancelLyricsLookup();
                    this.ClearLyrics();
                }

                this.RefreshState();
            });
        }

        private async void RefreshLikeStatusAsync()
        {
            string songId = this.personalFmService.CurrentTrack?.SourceInfo?.RemoteId;

            if (!this.IsLoggedIn || !this.IsActive || string.IsNullOrWhiteSpace(songId))
            {
                this.CancelLikeStatusLookup();
                this.likeStatusSongId = null;
                this.CurrentSongIsLiked = false;
                return;
            }

            if (string.Equals(this.likeStatusSongId, songId, StringComparison.Ordinal))
            {
                return;
            }

            this.CancelLikeStatusLookup();
            var source = new CancellationTokenSource();
            this.likeStatusCancellationTokenSource = source;
            this.likeStatusSongId = songId;
            this.CurrentSongIsLiked = false;
            this.actionErrorMessage = null;
            this.IsLikeStatusLoading = true;

            try
            {
                NeteaseResult<bool> result = await this.musicService.IsSongLikedAsync(songId, source.Token);

                if (!source.IsCancellationRequested &&
                    string.Equals(
                        this.personalFmService.CurrentTrack?.SourceInfo?.RemoteId,
                        songId,
                        StringComparison.Ordinal))
                {
                    if (result.IsSuccess)
                    {
                        this.CurrentSongIsLiked = result.Value;
                        this.actionErrorMessage = null;
                    }
                    else if (result.Error?.Code != NeteaseErrorCode.Cancelled)
                    {
                        this.actionErrorMessage = ResourceUtils.GetString(
                            result.Error?.MessageKey ?? "Language_Netease_Service_Unavailable");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not query the personal FM liked-song status. ErrorType={0}",
                    ex.GetType().Name);
                this.actionErrorMessage = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
            }
            finally
            {
                if (object.ReferenceEquals(
                    Interlocked.CompareExchange(ref this.likeStatusCancellationTokenSource, null, source),
                    source))
                {
                    source.Dispose();
                    this.IsLikeStatusLoading = false;
                    this.RefreshState();
                }
            }
        }

        private bool CanToggleLike()
        {
            return this.IsLoggedIn && this.IsActive && !this.IsBusy &&
                !this.IsLikeStatusLoading && !this.isLikeOperationRunning &&
                !string.IsNullOrWhiteSpace(this.personalFmService.CurrentTrack?.SourceInfo?.RemoteId) &&
                string.Equals(
                    this.likeStatusSongId,
                    this.personalFmService.CurrentTrack.SourceInfo.RemoteId,
                    StringComparison.Ordinal);
        }

        private async void ToggleLikeAsync()
        {
            string songId = this.personalFmService.CurrentTrack?.SourceInfo?.RemoteId;

            if (!this.CanToggleLike() || string.IsNullOrWhiteSpace(songId))
            {
                return;
            }

            bool desiredState = !this.CurrentSongIsLiked;
            this.isLikeOperationRunning = true;
            this.ToggleLikeCommand.RaiseCanExecuteChanged();

            try
            {
                NeteaseResult<bool> result = await this.musicService.SetSongLikedAsync(
                    songId,
                    desiredState,
                    CancellationToken.None);

                if (result.IsSuccess && string.Equals(
                    this.personalFmService.CurrentTrack?.SourceInfo?.RemoteId,
                    songId,
                    StringComparison.Ordinal))
                {
                    this.CurrentSongIsLiked = desiredState;
                    this.actionErrorMessage = null;
                }
                else if (!result.IsSuccess)
                {
                    this.actionErrorMessage = ResourceUtils.GetString(
                        result.Error?.MessageKey ?? "Language_Netease_Service_Unavailable");
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not update the personal FM liked-song state. ErrorType={0}",
                    ex.GetType().Name);
                this.actionErrorMessage = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
            }
            finally
            {
                this.isLikeOperationRunning = false;
                this.RefreshState();
            }
        }

        private void CancelLikeStatusLookup()
        {
            CancellationTokenSource source = Interlocked.Exchange(
                ref this.likeStatusCancellationTokenSource,
                null);
            source?.Cancel();
            source?.Dispose();
            this.IsLikeStatusLoading = false;
        }

        private async void RefreshLyricsAsync()
        {
            string songId = this.personalFmService.CurrentTrack?.SourceInfo?.RemoteId;

            if (!this.IsLoggedIn || !this.IsActive || string.IsNullOrWhiteSpace(songId))
            {
                this.CancelLyricsLookup();
                this.ClearLyrics();
                return;
            }

            if (string.Equals(this.lyricsSongId, songId, StringComparison.Ordinal))
            {
                return;
            }

            this.CancelLyricsLookup();
            var source = new CancellationTokenSource();
            this.lyricsCancellationTokenSource = source;
            this.lyricsSongId = songId;
            this.KaraokeLyrics = string.Empty;
            this.FallbackLyrics = string.Empty;
            this.KaraokeTranslationLyrics = string.Empty;
            this.FallbackTranslationLyrics = string.Empty;
            this.IsLyricsLoading = true;
            RaisePropertyChanged(nameof(this.IsLyricsEmpty));

            try
            {
                NeteaseLyricResult result = await this.musicService.GetLyricsAsync(songId, source.Token);

                if (!source.IsCancellationRequested && string.Equals(
                    this.personalFmService.CurrentTrack?.SourceInfo?.RemoteId,
                    songId,
                    StringComparison.Ordinal))
                {
                    if (result.IsSuccess)
                    {
                        this.KaraokeLyrics = result.KaraokeLyric ?? string.Empty;
                        this.FallbackLyrics = result.Lyric ?? string.Empty;
                        this.KaraokeTranslationLyrics = result.KaraokeTranslationLyric ?? string.Empty;
                        this.FallbackTranslationLyrics = result.TranslationLyric ?? string.Empty;
                    }
                    else if (result.Error?.Code != NeteaseErrorCode.Cancelled)
                    {
                        AppLog.Warning("Could not load personal FM lyrics. ErrorCode={0}", result.Error?.Code);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not load personal FM lyrics. ErrorType={0}", ex.GetType().Name);
            }
            finally
            {
                if (object.ReferenceEquals(
                    Interlocked.CompareExchange(ref this.lyricsCancellationTokenSource, null, source),
                    source))
                {
                    source.Dispose();
                    this.IsLyricsLoading = false;
                    RaisePropertyChanged(nameof(this.IsLyricsEmpty));
                }
            }
        }

        private void CancelLyricsLookup()
        {
            CancellationTokenSource source = Interlocked.Exchange(
                ref this.lyricsCancellationTokenSource,
                null);
            source?.Cancel();
            source?.Dispose();
            this.IsLyricsLoading = false;
        }

        private void ClearLyrics()
        {
            this.lyricsSongId = null;
            this.KaraokeLyrics = string.Empty;
            this.FallbackLyrics = string.Empty;
            this.KaraokeTranslationLyrics = string.Empty;
            this.FallbackTranslationLyrics = string.Empty;
            RaisePropertyChanged(nameof(this.IsLyricsEmpty));
        }

        private void RefreshState()
        {
            RaisePropertyChanged(nameof(this.IsLoggedIn));
            RaisePropertyChanged(nameof(this.IsActive));
            RaisePropertyChanged(nameof(this.IsBusy));
            RaisePropertyChanged(nameof(this.IsLoggedOutVisible));
            RaisePropertyChanged(nameof(this.IsIdleVisible));
            RaisePropertyChanged(nameof(this.IsActiveVisible));
            RaisePropertyChanged(nameof(this.CurrentTrackTitle));
            RaisePropertyChanged(nameof(this.CurrentTrackSubtitle));
            RaisePropertyChanged(nameof(this.BufferedText));
            RaisePropertyChanged(nameof(this.LikeButtonText));
            RaisePropertyChanged(nameof(this.ErrorMessage));
            RaisePropertyChanged(nameof(this.HasError));
            this.StartCommand.RaiseCanExecuteChanged();
            this.SkipCommand.RaiseCanExecuteChanged();
            this.DislikeCommand.RaiseCanExecuteChanged();
            this.ToggleLikeCommand.RaiseCanExecuteChanged();
        }

        private void Dispatch(Action action)
        {
            if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(action);
            }
        }
    }
}
