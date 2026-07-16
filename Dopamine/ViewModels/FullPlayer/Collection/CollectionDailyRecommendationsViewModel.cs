using Digimezzo.Foundation.Core.Utils;
using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using Dopamine.Core.Utils;
using Dopamine.Data.Entities;
using Dopamine.Services.Entities;
using Dopamine.Services.Dialog;
using Dopamine.Services.Extensions;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using Dopamine.Services.Provider;
using Dopamine.Services.Search;
using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public sealed class CollectionDailyRecommendationsViewModel : BindableBase
    {
        private readonly IContainerProvider container;
        private readonly INeteaseMusicService musicService;
        private readonly INeteaseSessionService sessionService;
        private readonly IPlaybackService playbackService;
        private readonly ISearchService searchService;
        private readonly IProviderService providerService;
        private readonly IDialogService dialogService;

        private ObservableCollection<TrackViewModel> items = new ObservableCollection<TrackViewModel>();
        private CollectionViewSource itemsCvs;
        private TrackViewModel selectedItem;
        private bool isInitialLoading;
        private bool isRefreshing;
        private bool hasError;
        private string errorMessage;
        private bool isLoaded;
        private CancellationTokenSource loadingCancellationTokenSource;
        private int loadingGeneration;
        private bool isStartingPlayback;
        private System.Threading.Timer recommendationRefreshTimer;
        private ObservableCollection<SearchProvider> contextMenuSearchProviders;
        private CancellationTokenSource likeStatusCancellationTokenSource;
        private string likeStatusSongId;
        private bool isLikeStatusLoading;
        private bool selectedSongIsLiked;
        private bool isLikeOperationRunning;
        private bool isDislikeOperationRunning;

        public DelegateCommand LoadedCommand { get; private set; }

        public DelegateCommand UnloadedCommand { get; private set; }

        public DelegateCommand RefreshCommand { get; private set; }

        public DelegateCommand PlayAllCommand { get; private set; }

        public DelegateCommand PlaySelectedCommand { get; private set; }

        public DelegateCommand PlayNextCommand { get; private set; }

        public DelegateCommand AddToNowPlayingCommand { get; private set; }

        public DelegateCommand<string> SearchOnlineCommand { get; private set; }

        public DelegateCommand ToggleLikeCommand { get; private set; }

        public DelegateCommand DislikeCommand { get; private set; }

        public ObservableCollection<SearchProvider> ContextMenuSearchProviders
        {
            get { return this.contextMenuSearchProviders; }
            private set
            {
                SetProperty<ObservableCollection<SearchProvider>>(ref this.contextMenuSearchProviders, value);
                RaisePropertyChanged(nameof(this.HasContextMenuSearchProviders));
            }
        }

        public bool HasContextMenuSearchProviders =>
            this.ContextMenuSearchProviders != null && this.ContextMenuSearchProviders.Count > 0;

        public ObservableCollection<TrackViewModel> Items
        {
            get { return this.items; }
            private set { SetProperty<ObservableCollection<TrackViewModel>>(ref this.items, value); }
        }

        public CollectionViewSource ItemsCvs
        {
            get { return this.itemsCvs; }
            private set { SetProperty<CollectionViewSource>(ref this.itemsCvs, value); }
        }

        public TrackViewModel SelectedItem
        {
            get { return this.selectedItem; }
            set
            {
                if (SetProperty<TrackViewModel>(ref this.selectedItem, value))
                {
                    this.CancelLikeStatusLookup();
                    this.likeStatusSongId = null;
                    this.SelectedSongIsLiked = false;
                    this.RaiseSelectionCommandStates();
                }
            }
        }

        public bool IsLikeStatusLoading
        {
            get { return this.isLikeStatusLoading; }
            private set
            {
                if (SetProperty<bool>(ref this.isLikeStatusLoading, value))
                {
                    RaisePropertyChanged(nameof(this.LikeMenuHeader));
                    this.RaiseSelectionCommandStates();
                }
            }
        }

        public bool SelectedSongIsLiked
        {
            get { return this.selectedSongIsLiked; }
            private set
            {
                if (SetProperty<bool>(ref this.selectedSongIsLiked, value))
                {
                    RaisePropertyChanged(nameof(this.LikeMenuHeader));
                    this.RaiseSelectionCommandStates();
                }
            }
        }

        public string LikeMenuHeader => this.IsLikeStatusLoading
            ? ResourceUtils.GetString("Language_Netease_Like_Status_Loading")
            : ResourceUtils.GetString(this.SelectedSongIsLiked
                ? "Language_Netease_Unlike"
                : "Language_Netease_Like");

        public int Count => this.ItemsCvs == null ? 0 : this.ItemsCvs.View.Cast<TrackViewModel>().Count();

        public bool IsInitialLoading
        {
            get { return this.isInitialLoading; }
            private set
            {
                SetProperty<bool>(ref this.isInitialLoading, value);
                this.RefreshCommand?.RaiseCanExecuteChanged();
                this.RaiseStateProperties();
            }
        }

        public bool IsRefreshing
        {
            get { return this.isRefreshing; }
            private set
            {
                SetProperty<bool>(ref this.isRefreshing, value);
                this.RefreshCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsLoggedIn => this.sessionService.State == NeteaseSessionState.SignedIn;

        public bool HasError
        {
            get { return this.hasError; }
            private set
            {
                SetProperty<bool>(ref this.hasError, value);
                this.RaiseStateProperties();
            }
        }

        public string ErrorMessage
        {
            get { return this.errorMessage; }
            private set { SetProperty<string>(ref this.errorMessage, value); }
        }

        public bool IsLoggedOutVisible => !this.IsLoggedIn && !this.IsInitialLoading;

        public bool IsErrorVisible => this.HasError && this.Items.Count == 0 && !this.IsInitialLoading;

        public bool IsEmptyVisible => this.IsLoggedIn && !this.HasError && !this.IsInitialLoading && this.Items.Count == 0;

        public bool IsListVisible => this.Items.Count > 0;

        public CollectionDailyRecommendationsViewModel(
            IContainerProvider container,
            INeteaseMusicService musicService,
            INeteaseSessionService sessionService,
            IPlaybackService playbackService,
            ISearchService searchService,
            IDialogService dialogService)
        {
            this.container = container;
            this.musicService = musicService;
            this.sessionService = sessionService;
            this.playbackService = playbackService;
            this.searchService = searchService;
            this.dialogService = dialogService;
            this.providerService = container.Resolve<IProviderService>();

            this.LoadedCommand = new DelegateCommand(() => this.LoadedAsync());
            this.UnloadedCommand = new DelegateCommand(this.Unloaded);
            this.RefreshCommand = new DelegateCommand(
                async () => await this.LoadAsync(),
                () => !this.IsInitialLoading && !this.IsRefreshing);
            this.PlayAllCommand = new DelegateCommand(
                () => this.PlayAllAsync(),
                () => this.Items.Count > 0 && !this.isStartingPlayback);
            this.PlaySelectedCommand = new DelegateCommand(
                () => this.PlaySelectedAsync(),
                () => this.SelectedItem != null && !this.isStartingPlayback);
            this.PlayNextCommand = new DelegateCommand(
                () => this.PlayNextAsync(),
                () => this.CanModifyCurrentOnlineQueue());
            this.AddToNowPlayingCommand = new DelegateCommand(
                () => this.AddToNowPlayingAsync(),
                () => this.CanModifyCurrentOnlineQueue());
            this.SearchOnlineCommand = new DelegateCommand<string>(
                id => this.SearchOnline(id),
                _ => this.SelectedItem != null);
            this.ToggleLikeCommand = new DelegateCommand(
                () => this.ToggleLikeAsync(),
                () => this.CanToggleLike());
            this.DislikeCommand = new DelegateCommand(
                () => this.DislikeSelectedAsync(),
                () => this.CanDislikeSelected());

            this.searchService.DoSearch += (_) => this.DispatchFilterRefresh();
            this.sessionService.SessionChanged += (_, __) => this.DispatchSessionChanged();
            this.playbackService.PlaybackSuccess += (_, __) => this.DispatchPlayingStateRefresh();
            this.playbackService.PlaybackStopped += (_, __) => this.DispatchPlayingStateRefresh();
            this.playbackService.QueueChanged += (_, __) => this.Dispatch(this.RaiseSelectionCommandStates);
            this.providerService.SearchProvidersChanged += (_, __) => this.GetSearchProvidersAsync();
            this.playbackService.PlaybackFailed += (_, e) =>
            {
                this.DispatchPlayingStateRefresh();

                if (this.playbackService.CurrentTrack != null && this.playbackService.CurrentTrack.IsOnline &&
                    !string.IsNullOrWhiteSpace(e.MessageKey))
                {
                    this.Dispatch(() => this.ErrorMessage = ResourceUtils.GetString(e.MessageKey));
                }
            };

            this.RebuildCollectionView();
            this.GetSearchProvidersAsync();
        }

        public async Task PlayFromAsync(TrackViewModel track)
        {
            if (track == null || this.Items.Count == 0)
            {
                return;
            }

            await this.StartPlaybackAsync(track);
        }

        public async Task PrepareContextMenuAsync()
        {
            TrackViewModel target = this.SelectedItem;
            string songId = target?.SourceInfo?.RemoteId;

            if (!this.IsLoggedIn || string.IsNullOrWhiteSpace(songId))
            {
                return;
            }

            this.CancelLikeStatusLookup();
            var cancellationTokenSource = new CancellationTokenSource();
            this.likeStatusCancellationTokenSource = cancellationTokenSource;
            long generation = this.sessionService.SessionGeneration;
            this.likeStatusSongId = songId;
            this.IsLikeStatusLoading = true;

            try
            {
                NeteaseResult<bool> result = await this.musicService.IsSongLikedAsync(
                    songId,
                    cancellationTokenSource.Token);

                if (!cancellationTokenSource.IsCancellationRequested &&
                    generation == this.sessionService.SessionGeneration &&
                    string.Equals(this.SelectedItem?.SourceInfo?.RemoteId, songId, StringComparison.Ordinal))
                {
                    if (result.IsSuccess)
                    {
                        this.SelectedSongIsLiked = result.Value;
                    }
                    else if (result.Error?.Code != NeteaseErrorCode.Cancelled)
                    {
                        this.ShowNeteaseActionError(result.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not query the Netease liked-song status. ErrorType={0}",
                    ex.GetType().Name);

                if (generation == this.sessionService.SessionGeneration && this.isLoaded)
                {
                    this.ShowNeteaseActionError(null);
                }
            }
            finally
            {
                if (object.ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref this.likeStatusCancellationTokenSource,
                        null,
                        cancellationTokenSource),
                    cancellationTokenSource))
                {
                    if (string.Equals(this.likeStatusSongId, songId, StringComparison.Ordinal))
                    {
                        this.IsLikeStatusLoading = false;
                    }
                }

                cancellationTokenSource.Dispose();
            }
        }

        private async void LoadedAsync()
        {
            this.isLoaded = true;
            this.RaiseStateProperties();
            this.ScheduleRecommendationRefresh();

            if (this.IsLoggedIn)
            {
                await this.LoadAsync();
            }
        }

        private void Unloaded()
        {
            this.isLoaded = false;
            this.CancelLoading();
            this.CancelLikeStatusLookup();
            this.DisposeRecommendationRefreshTimer();
        }

        private async void PlayAllAsync()
        {
            if (this.Items.Count > 0)
            {
                await this.StartPlaybackAsync(this.Items[0]);
            }
        }

        private async Task LoadAsync()
        {
            if (!this.isLoaded || !this.IsLoggedIn || this.IsInitialLoading || this.IsRefreshing)
            {
                this.RaiseStateProperties();
                return;
            }

            this.CancelLoading();
            int generation = Interlocked.Increment(ref this.loadingGeneration);
            var cancellationTokenSource = new CancellationTokenSource();
            Interlocked.Exchange(ref this.loadingCancellationTokenSource, cancellationTokenSource);
            CancellationToken cancellationToken = cancellationTokenSource.Token;
            bool hasOldItems = this.Items.Count > 0;

            this.IsInitialLoading = !hasOldItems;
            this.IsRefreshing = hasOldItems;
            this.HasError = false;
            this.ErrorMessage = string.Empty;

            try
            {
                NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>> result =
                    await this.musicService.GetDailyRecommendationsAsync(cancellationToken);

                if (generation != this.loadingGeneration || cancellationToken.IsCancellationRequested || !this.isLoaded)
                {
                    return;
                }

                if (!result.IsSuccess)
                {
                    this.HasError = true;
                    this.ErrorMessage = ResourceUtils.GetString(result.Error?.MessageKey ?? "Language_Netease_Service_Unavailable");
                    return;
                }

                var mapped = new ObservableCollection<TrackViewModel>(result.Value.Select(this.MapTrack));
                this.Items = mapped;
                this.RebuildCollectionView();
                this.RefreshPlayingState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not load Netease daily recommendations. ErrorType={0}", ex.GetType().Name);

                if (generation == this.loadingGeneration && this.isLoaded)
                {
                    this.HasError = true;
                    this.ErrorMessage = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
                }
            }
            finally
            {
                if (object.ReferenceEquals(
                    Interlocked.CompareExchange(ref this.loadingCancellationTokenSource, null, cancellationTokenSource),
                    cancellationTokenSource))
                {
                    this.IsInitialLoading = false;
                    this.IsRefreshing = false;
                    this.RaiseStateProperties();
                }

                cancellationTokenSource.Dispose();

                if (this.isLoaded)
                {
                    this.ScheduleRecommendationRefresh();
                }
            }
        }

        private async Task StartPlaybackAsync(TrackViewModel startTrack)
        {
            if (startTrack == null || this.isStartingPlayback)
            {
                return;
            }

            this.isStartingPlayback = true;
            this.PlayAllCommand.RaiseCanExecuteChanged();

            try
            {
                await this.playbackService.PlayTransientQueueAsync(
                    this.ItemsCvs.View.Cast<TrackViewModel>().ToList(),
                    startTrack,
                    PlaybackQueueContext.NeteaseDailyRecommendations);
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not start the Netease daily recommendations queue. ErrorType={0}", ex.GetType().Name);
                this.ErrorMessage = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
            }
            finally
            {
                this.isStartingPlayback = false;
                this.PlayAllCommand.RaiseCanExecuteChanged();
                this.RaiseSelectionCommandStates();
            }
        }

        private async void PlaySelectedAsync()
        {
            await this.StartPlaybackAsync(this.SelectedItem);
        }

        private async void PlayNextAsync()
        {
            if (this.SelectedItem != null)
            {
                await this.playbackService.AddToQueueNextAsync(
                    new List<TrackViewModel> { this.SelectedItem });
            }
        }

        private async void AddToNowPlayingAsync()
        {
            if (this.SelectedItem != null)
            {
                await this.playbackService.AddToQueueAsync(
                    new List<TrackViewModel> { this.SelectedItem });
            }
        }

        private bool CanModifyCurrentOnlineQueue()
        {
            return this.SelectedItem != null && this.playbackService.HasQueue &&
                this.playbackService.Queue.All(x => x != null && x.IsOnline);
        }

        private void RaiseSelectionCommandStates()
        {
            this.PlaySelectedCommand?.RaiseCanExecuteChanged();
            this.PlayNextCommand?.RaiseCanExecuteChanged();
            this.AddToNowPlayingCommand?.RaiseCanExecuteChanged();
            this.SearchOnlineCommand?.RaiseCanExecuteChanged();
            this.ToggleLikeCommand?.RaiseCanExecuteChanged();
            this.DislikeCommand?.RaiseCanExecuteChanged();
        }

        private bool CanToggleLike()
        {
            return this.IsLoggedIn && this.SelectedItem?.SourceInfo != null &&
                !string.IsNullOrWhiteSpace(this.SelectedItem.SourceInfo.RemoteId) &&
                !this.IsLikeStatusLoading && !this.isLikeOperationRunning &&
                !this.isDislikeOperationRunning &&
                string.Equals(
                    this.likeStatusSongId,
                    this.SelectedItem.SourceInfo.RemoteId,
                    StringComparison.Ordinal);
        }

        private bool CanDislikeSelected()
        {
            return this.IsLoggedIn && this.SelectedItem?.SourceInfo != null &&
                !string.IsNullOrWhiteSpace(this.SelectedItem.SourceInfo.RemoteId) &&
                !this.isDislikeOperationRunning && !this.isLikeOperationRunning;
        }

        private async void ToggleLikeAsync()
        {
            TrackViewModel target = this.SelectedItem;
            string songId = target?.SourceInfo?.RemoteId;

            if (!this.CanToggleLike() || string.IsNullOrWhiteSpace(songId))
            {
                return;
            }

            bool desiredState = !this.SelectedSongIsLiked;
            long generation = this.sessionService.SessionGeneration;
            this.isLikeOperationRunning = true;
            this.RaiseSelectionCommandStates();

            try
            {
                NeteaseResult<bool> result = await this.musicService.SetSongLikedAsync(
                    songId,
                    desiredState,
                    CancellationToken.None);

                if (result.IsSuccess)
                {
                    if (generation == this.sessionService.SessionGeneration &&
                        string.Equals(
                            this.SelectedItem?.SourceInfo?.RemoteId,
                            songId,
                            StringComparison.Ordinal))
                    {
                        this.likeStatusSongId = songId;
                        this.SelectedSongIsLiked = desiredState;
                    }
                }
                else
                {
                    if (this.CanPresentOperationResult(generation))
                    {
                        this.ShowNeteaseActionError(result.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not update the Netease liked-song state. ErrorType={0}",
                    ex.GetType().Name);

                if (this.CanPresentOperationResult(generation))
                {
                    this.ShowNeteaseActionError(null);
                }
            }
            finally
            {
                this.isLikeOperationRunning = false;
                this.RaiseSelectionCommandStates();
            }
        }

        private async void DislikeSelectedAsync()
        {
            TrackViewModel target = this.SelectedItem;
            string songId = target?.SourceInfo?.RemoteId;

            if (!this.CanDislikeSelected() || string.IsNullOrWhiteSpace(songId))
            {
                return;
            }

            string confirmation = ResourceUtils.GetString(
                "Language_Netease_Not_Interested_Confirmation").Replace(
                    "{song}",
                    target.TrackTitle ?? string.Empty);

            if (!this.dialogService.ShowConfirmation(
                0xe11b,
                16,
                ResourceUtils.GetString("Language_Netease_Not_Interested"),
                confirmation,
                ResourceUtils.GetString("Language_Yes"),
                ResourceUtils.GetString("Language_No")))
            {
                return;
            }

            long generation = this.sessionService.SessionGeneration;
            this.CancelLikeStatusLookup();
            this.likeStatusSongId = null;
            this.SelectedSongIsLiked = false;
            this.isDislikeOperationRunning = true;
            this.RaiseSelectionCommandStates();

            try
            {
                NeteaseResult<NeteaseRecommendationMutation> result =
                    await this.musicService.DislikeDailyRecommendationAsync(
                        songId,
                        CancellationToken.None);

                if (!result.IsSuccess)
                {
                    if (this.CanPresentOperationResult(generation))
                    {
                        this.ShowNeteaseActionError(result.Error);
                    }

                    return;
                }

                if (this.CanPresentOperationResult(generation))
                {
                    this.ApplyRecommendationMutation(result.Value);

                    if (result.Value != null && result.Value.RequiresRefresh)
                    {
                        await this.LoadAsync();
                    }
                }

                if (this.CanPresentOperationResult(generation) &&
                    result.Value != null && !result.Value.PersistenceSucceeded)
                {
                    this.dialogService.ShowNotification(
                        0xe711,
                        16,
                        ResourceUtils.GetString("Language_Netease_Music"),
                        ResourceUtils.GetString("Language_Netease_Recommendation_Cache_Save_Failed"),
                        ResourceUtils.GetString("Language_Ok"),
                        false);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not mark the Netease daily recommendation as not interested. ErrorType={0}",
                    ex.GetType().Name);

                if (this.CanPresentOperationResult(generation))
                {
                    this.ShowNeteaseActionError(null);
                }
            }
            finally
            {
                this.isDislikeOperationRunning = false;
                this.RaiseSelectionCommandStates();
            }
        }

        private void ApplyRecommendationMutation(NeteaseRecommendationMutation mutation)
        {
            if (mutation == null || string.IsNullOrWhiteSpace(mutation.RemovedSongId))
            {
                return;
            }

            int index = -1;

            for (int i = 0; i < this.Items.Count; i++)
            {
                if (string.Equals(
                    this.Items[i]?.SourceInfo?.RemoteId,
                    mutation.RemovedSongId,
                    StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                if (mutation.UpdatedRecommendations != null)
                {
                    this.Items = new ObservableCollection<TrackViewModel>(
                        mutation.UpdatedRecommendations.Select(this.MapTrack));
                    this.SelectedItem = null;
                    this.RebuildCollectionView();
                    this.RefreshPlayingState();
                }

                return;
            }

            if (mutation.Replacement == null)
            {
                this.Items.RemoveAt(index);
                this.SelectedItem = null;
            }
            else
            {
                TrackViewModel replacement = this.MapTrack(mutation.Replacement);
                this.Items[index] = replacement;
                this.SelectedItem = replacement;
            }

            this.ItemsCvs?.View.Refresh();
            RaisePropertyChanged(nameof(this.Count));
            this.RefreshPlayingState();
            this.RaiseStateProperties();
        }

        private void CancelLikeStatusLookup()
        {
            CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(
                ref this.likeStatusCancellationTokenSource,
                null);
            cancellationTokenSource?.Cancel();
            this.IsLikeStatusLoading = false;
        }

        private void ShowNeteaseActionError(NeteaseError error)
        {
            this.dialogService.ShowNotification(
                0xe711,
                16,
                ResourceUtils.GetString("Language_Error"),
                ResourceUtils.GetString(error?.MessageKey ?? "Language_Netease_Service_Unavailable"),
                ResourceUtils.GetString("Language_Ok"),
                false);
        }

        private bool CanPresentOperationResult(long generation)
        {
            return this.isLoaded && generation == this.sessionService.SessionGeneration;
        }

        private void SearchOnline(string providerId)
        {
            if (this.SelectedItem != null)
            {
                this.providerService.SearchOnline(
                    providerId,
                    new[] { this.SelectedItem.ArtistName, this.SelectedItem.TrackTitle });
            }
        }

        private async void GetSearchProvidersAsync()
        {
            try
            {
                List<SearchProvider> providers = await this.providerService.GetSearchProvidersAsync();
                this.ContextMenuSearchProviders = new ObservableCollection<SearchProvider>(providers);
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not load online search providers for Netease recommendations. ErrorType={0}",
                    ex.GetType().Name);
                this.ContextMenuSearchProviders = new ObservableCollection<SearchProvider>();
            }
        }

        private void ScheduleRecommendationRefresh()
        {
            this.DisposeRecommendationRefreshTimer();
            TimeSpan dueTime = NeteaseDailyRecommendationSchedule.GetDelayUntilNextRefresh(
                DateTimeOffset.UtcNow);
            this.recommendationRefreshTimer = new System.Threading.Timer(
                _ => this.Dispatch(() =>
                {
                    if (this.isLoaded && this.IsLoggedIn)
                    {
                        this.LoadAsync();
                    }
                }),
                null,
                dueTime,
                Timeout.InfiniteTimeSpan);
        }

        private void DisposeRecommendationRefreshTimer()
        {
            System.Threading.Timer timer = Interlocked.Exchange(
                ref this.recommendationRefreshTimer,
                null);
            timer?.Dispose();
        }

        private TrackViewModel MapTrack(NeteaseRecommendedSong song)
        {
            return NeteaseTrackFactory.Create(this.container, song);
        }

        private void RebuildCollectionView()
        {
            this.ItemsCvs = new CollectionViewSource { Source = this.Items };
            this.ItemsCvs.Filter += this.ItemsCvs_Filter;
            this.RaiseStateProperties();
        }

        private void ItemsCvs_Filter(object sender, FilterEventArgs e)
        {
            var track = e.Item as TrackViewModel;
            string searchText = this.searchService.SearchText;

            e.Accepted = track != null && (string.IsNullOrWhiteSpace(searchText) ||
                Contains(track.TrackTitle, searchText) ||
                Contains(track.ArtistName, searchText) ||
                Contains(track.AlbumTitle, searchText));
        }

        private void DispatchFilterRefresh()
        {
            this.Dispatch(() =>
            {
                this.ItemsCvs?.View.Refresh();
                RaisePropertyChanged(nameof(this.Count));
            });
        }

        private void DispatchSessionChanged()
        {
            this.Dispatch(() =>
            {
                this.CancelLikeStatusLookup();
                this.likeStatusSongId = null;
                this.SelectedSongIsLiked = false;
                RaisePropertyChanged(nameof(this.IsLoggedIn));

                if (!this.IsLoggedIn)
                {
                    this.CancelLoading();
                    this.Items = new ObservableCollection<TrackViewModel>();
                    this.RebuildCollectionView();
                }
                else if (this.isLoaded)
                {
                    this.LoadAsync();
                }

                this.RaiseStateProperties();
                this.RaiseSelectionCommandStates();
            });
        }

        private void DispatchPlayingStateRefresh()
        {
            this.Dispatch(this.RefreshPlayingState);
        }

        private void RefreshPlayingState()
        {
            TrackViewModel currentTrack = this.playbackService.CurrentTrack;

            foreach (TrackViewModel track in this.Items)
            {
                track.IsPlaying = currentTrack != null && currentTrack.IsOnline && currentTrack.SafePath.Equals(track.SafePath);
                track.IsPaused = track.IsPlaying && !this.playbackService.IsPlaying;
            }
        }

        private void CancelLoading()
        {
            Interlocked.Increment(ref this.loadingGeneration);
            CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(
                ref this.loadingCancellationTokenSource,
                null);
            cancellationTokenSource?.Cancel();

            this.IsInitialLoading = false;
            this.IsRefreshing = false;
        }

        private void RaiseStateProperties()
        {
            RaisePropertyChanged(nameof(this.Count));
            RaisePropertyChanged(nameof(this.IsLoggedIn));
            RaisePropertyChanged(nameof(this.IsLoggedOutVisible));
            RaisePropertyChanged(nameof(this.IsErrorVisible));
            RaisePropertyChanged(nameof(this.IsEmptyVisible));
            RaisePropertyChanged(nameof(this.IsListVisible));
            this.PlayAllCommand?.RaiseCanExecuteChanged();
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

        private static bool Contains(string value, string searchText)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }
    }
}
