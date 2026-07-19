using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Base;
using Dopamine.Core.Logging;
using Dopamine.Core.Utils;
using Dopamine.Services.Dialog;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using Dopamine.Services.Provider;
using Dopamine.Services.Search;
using Dopamine.ViewModels.Common;
using Dopamine.Views.Common;
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
    public sealed class CollectionIntelligenceRecommendationsViewModel : BindableBase
    {
        private static readonly object RandomLock = new object();
        private static readonly Random Random = new Random();

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
        private bool isLoaded;
        private bool isGenerating;
        private bool isStartingPlayback;
        private string errorMessage;
        private CancellationTokenSource cancellationTokenSource;
        private ObservableCollection<SearchProvider> contextMenuSearchProviders;
        private CancellationTokenSource likeStatusCancellationTokenSource;
        private string likeStatusSongId;
        private bool isLikeStatusLoading;
        private bool selectedSongIsLiked;
        private bool isLikeOperationRunning;
        private bool isDislikeOperationRunning;

        public CollectionIntelligenceRecommendationsViewModel(
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

            this.LoadedCommand = new DelegateCommand(this.Loaded);
            this.UnloadedCommand = new DelegateCommand(this.Unloaded);
            this.GenerateCommand = new DelegateCommand(
                () => this.GenerateAsync(),
                () => this.IsLoggedIn && !this.IsGenerating);
            this.PlayAllCommand = new DelegateCommand(
                () => this.StartPlaybackAsync(this.ItemsCvs?.View.Cast<TrackViewModel>().FirstOrDefault()),
                () => this.Count > 0 && !this.isStartingPlayback);
            this.PlaySelectedCommand = new DelegateCommand(
                () => this.StartPlaybackAsync(this.SelectedItem),
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

            this.ShowTrackInformationCommand = new DelegateCommand(
                () => this.ShowTrackInformation(),
                () => this.SelectedItem != null);

            this.playbackService.PlaybackSuccess += (_, __) => this.Dispatch(this.RefreshPlayingState);
            this.playbackService.PlaybackStopped += (_, __) => this.Dispatch(this.RefreshPlayingState);
            this.playbackService.QueueChanged += (_, __) => this.Dispatch(this.RaiseSelectionCommandStates);
            this.providerService.SearchProvidersChanged += (_, __) => this.GetSearchProvidersAsync();

            this.RebuildCollectionView();
            this.GetSearchProvidersAsync();
        }

        public DelegateCommand LoadedCommand { get; private set; }

        public DelegateCommand UnloadedCommand { get; private set; }

        public DelegateCommand GenerateCommand { get; private set; }

        public DelegateCommand PlayAllCommand { get; private set; }

        public DelegateCommand PlaySelectedCommand { get; private set; }

        public DelegateCommand PlayNextCommand { get; private set; }

        public DelegateCommand AddToNowPlayingCommand { get; private set; }

        public DelegateCommand<string> SearchOnlineCommand { get; private set; }

        public DelegateCommand ToggleLikeCommand { get; private set; }

        public DelegateCommand DislikeCommand { get; private set; }

        public DelegateCommand ShowTrackInformationCommand { get; private set; }

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

        public bool IsLoggedIn => this.sessionService.State == NeteaseSessionState.SignedIn;

        public bool IsGenerating
        {
            get { return this.isGenerating; }
            private set
            {
                if (SetProperty<bool>(ref this.isGenerating, value))
                {
                    this.GenerateCommand.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(this.IsListVisible));
                    RaisePropertyChanged(nameof(this.IsEmptyVisible));
                    RaisePropertyChanged(nameof(this.IsErrorVisible));
                    RaisePropertyChanged(nameof(this.IsInitialGenerating));
                }
            }
        }

        public string ErrorMessage
        {
            get { return this.errorMessage; }
            private set
            {
                if (SetProperty<string>(ref this.errorMessage, value))
                {
                    RaisePropertyChanged(nameof(this.HasError));
                    RaisePropertyChanged(nameof(this.IsErrorVisible));
                    RaisePropertyChanged(nameof(this.IsEmptyVisible));
                }
            }
        }

        public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

        public bool IsErrorVisible => this.HasError && this.Items.Count == 0 && !this.IsGenerating;

        public bool IsInitialGenerating => this.IsGenerating && this.Items.Count == 0;

        public int Count => this.ItemsCvs == null ? 0 : this.ItemsCvs.View.Cast<TrackViewModel>().Count();

        public bool IsListVisible => this.Items.Count > 0;

        public bool IsLoggedOutVisible => !this.IsLoggedIn && !this.IsGenerating;

        public bool IsEmptyVisible => this.IsLoggedIn && !this.IsGenerating && !this.HasError && this.Items.Count == 0;

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
                    "Could not query the heart recommendation liked-song status. ErrorType={0}",
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
                    cancellationTokenSource.Dispose();
                    this.IsLikeStatusLoading = false;
                }
            }
        }

        private void Loaded()
        {
            if (this.isLoaded)
            {
                return;
            }

            this.isLoaded = true;
            this.sessionService.SessionChanged += this.SessionService_SessionChanged;
            this.searchService.DoSearch += this.SearchService_DoSearch;
            this.RefreshPlayingState();
            this.RaiseStateProperties();
        }

        private void Unloaded()
        {
            if (!this.isLoaded)
            {
                return;
            }

            this.isLoaded = false;
            this.sessionService.SessionChanged -= this.SessionService_SessionChanged;
            this.searchService.DoSearch -= this.SearchService_DoSearch;
            this.CancelRequest();
            this.CancelLikeStatusLookup();
        }

        private async void GenerateAsync()
        {
            this.CancelRequest();
            this.cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = this.cancellationTokenSource.Token;
            this.IsGenerating = true;
            this.ErrorMessage = null;

            try
            {
                NeteaseResult<NeteaseLikedLibrary> libraryResult =
                    await this.musicService.GetLikedLibraryAsync(token);

                if (!libraryResult.IsSuccess)
                {
                    this.ErrorMessage = ResourceUtils.GetString(
                        libraryResult.Error?.MessageKey ?? "Language_Netease_Service_Unavailable");
                    return;
                }

                IReadOnlyList<string> songIds = libraryResult.Value?.SongIds ?? Array.Empty<string>();

                if (songIds.Count == 0)
                {
                    this.ErrorMessage = ResourceUtils.GetString("Language_Netease_Liked_Music_Empty");
                    return;
                }

                int seedIndex;

                lock (RandomLock)
                {
                    seedIndex = Random.Next(songIds.Count);
                }

                string seedSongId = songIds[seedIndex];
                string currentSongId = this.playbackService.CurrentTrack?.SourceInfo?.Kind == TrackSourceKind.Netease
                    ? this.playbackService.CurrentTrack.SourceInfo.RemoteId
                    : null;
                string startMusicId = string.IsNullOrWhiteSpace(currentSongId)
                    ? seedSongId
                    : currentSongId;

                NeteaseResult<IReadOnlyList<NeteaseIntelligenceRecommendation>> result =
                    await this.musicService.GetIntelligenceRecommendationsAsync(
                        libraryResult.Value.PlaylistId,
                        seedSongId,
                        startMusicId,
                        songIds.Count,
                        token);

                if (!result.IsSuccess)
                {
                    this.ErrorMessage = ResourceUtils.GetString(
                        result.Error?.MessageKey ?? "Language_Netease_Service_Unavailable");
                    return;
                }

                var mapped = new ObservableCollection<TrackViewModel>(
                    (result.Value ?? Array.Empty<NeteaseIntelligenceRecommendation>())
                        .Select(x => NeteaseTrackFactory.Create(this.container, x?.Song))
                        .Where(x => x != null));

                if (mapped.Count == 0)
                {
                    this.ErrorMessage = ResourceUtils.GetString("Language_Netease_Intelligence_Empty");
                    return;
                }

                this.Items = mapped;
                this.SelectedItem = null;
                this.RebuildCollectionView();
                this.RefreshPlayingState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                this.ErrorMessage = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
            }
            finally
            {
                this.IsGenerating = false;
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
            this.RaiseSelectionCommandStates();

            try
            {
                bool started = await this.playbackService.PlayTransientQueueAsync(
                    this.ItemsCvs.View.Cast<TrackViewModel>().ToList(),
                    startTrack,
                    PlaybackQueueContext.NeteaseIntelligenceRecommendations);

                if (!started)
                {
                    this.ErrorMessage = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
                }
            }
            finally
            {
                this.isStartingPlayback = false;
                this.PlayAllCommand.RaiseCanExecuteChanged();
                this.RaiseSelectionCommandStates();
            }
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
            this.ShowTrackInformationCommand?.RaiseCanExecuteChanged();
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
                else if (this.CanPresentOperationResult(generation))
                {
                    this.ShowNeteaseActionError(result.Error);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not update the heart recommendation liked-song state. ErrorType={0}",
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
                NeteaseResult<bool> result = await this.musicService.DislikeIntelligenceRecommendationAsync(
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
                    this.Items.Remove(target);
                    this.SelectedItem = null;
                    this.ItemsCvs?.View.Refresh();
                    RaisePropertyChanged(nameof(this.Count));
                    this.RefreshPlayingState();
                    this.RaiseStateProperties();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not mark a heart recommendation as not interested. ErrorType={0}",
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

        private void ShowTrackInformation()
        {
            TrackViewModel target = this.SelectedItem;

            if (target == null || target.IsLocalFile)
            {
                return;
            }

            FileInformation view = this.container.Resolve<FileInformation>();
            view.DataContext = new FileInformationViewModel(
                target,
                this.musicService,
                this.container.Resolve<IEnumerable<IOnlineAudioFallbackProvider>>());
            this.dialogService.ShowCustomDialog(
                0xe8d6,
                16,
                ResourceUtils.GetString("Language_Information"),
                view,
                400,
                700,
                true,
                true,
                true,
                false,
                ResourceUtils.GetString("Language_Ok"),
                string.Empty,
                null);
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
                    "Could not load online search providers for heart recommendations. ErrorType={0}",
                    ex.GetType().Name);
                this.ContextMenuSearchProviders = new ObservableCollection<SearchProvider>();
            }
        }

        private void RefreshPlayingState()
        {
            TrackViewModel currentTrack = this.playbackService.CurrentTrack;

            foreach (TrackViewModel track in this.Items)
            {
                track.IsPlaying = currentTrack != null && currentTrack.IsOnline &&
                    currentTrack.SafePath.Equals(track.SafePath);
                track.IsPaused = track.IsPlaying && !this.playbackService.IsPlaying;
            }
        }

        private void RebuildCollectionView()
        {
            this.ItemsCvs = new CollectionViewSource { Source = this.Items };
            this.ItemsCvs.Filter += this.ItemsCvs_Filter;
            RaisePropertyChanged(nameof(this.Count));
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

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                source.IndexOf(value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SessionService_SessionChanged(object sender, EventArgs e)
        {
            this.Dispatch(() =>
            {
                this.CancelLikeStatusLookup();
                this.likeStatusSongId = null;
                this.SelectedSongIsLiked = false;
                RaisePropertyChanged(nameof(this.IsLoggedIn));

                if (!this.IsLoggedIn)
                {
                    this.CancelRequest();
                    this.Items = new ObservableCollection<TrackViewModel>();
                    this.ErrorMessage = null;
                    this.RebuildCollectionView();
                }

                this.RaiseStateProperties();
                this.RaiseSelectionCommandStates();
            });
        }

        private void SearchService_DoSearch(string value)
        {
            this.Dispatch(() =>
            {
                this.ItemsCvs?.View.Refresh();
                RaisePropertyChanged(nameof(this.Count));
            });
        }

        private void CancelRequest()
        {
            CancellationTokenSource source = Interlocked.Exchange(
                ref this.cancellationTokenSource,
                null);
            source?.Cancel();
            source?.Dispose();
        }

        private void RaiseStateProperties()
        {
            RaisePropertyChanged(nameof(this.IsLoggedOutVisible));
            RaisePropertyChanged(nameof(this.IsEmptyVisible));
            RaisePropertyChanged(nameof(this.IsListVisible));
            RaisePropertyChanged(nameof(this.IsErrorVisible));
            RaisePropertyChanged(nameof(this.IsInitialGenerating));
            RaisePropertyChanged(nameof(this.Count));
            this.GenerateCommand?.RaiseCanExecuteChanged();
            this.PlayAllCommand?.RaiseCanExecuteChanged();
            this.RaiseSelectionCommandStates();
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
