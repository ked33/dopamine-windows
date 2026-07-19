using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Logging;
using Dopamine.Services.Entities;
using Dopamine.Services.Online;
using Dopamine.Services.Online.GdMusic;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using Dopamine.Services.Provider;
using Dopamine.Services.Search;
using Dopamine.Services.Dialog;
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
    public sealed class CollectionOnlineSearchViewModel : BindableBase
    {
        private const int PageSize = 30;

        private readonly IContainerProvider container;
        private readonly IGdMusicApiClient apiClient;
        private readonly IPlaybackService playbackService;
        private readonly ISearchService searchService;
        private readonly IProviderService providerService;
        private readonly IDialogService dialogService;
        private readonly INeteaseMusicService neteaseMusicService;
        private readonly GdMusicDownloadCoordinator downloadCoordinator;

        private ObservableCollection<TrackViewModel> items = new ObservableCollection<TrackViewModel>();
        private CollectionViewSource itemsCvs;
        private TrackViewModel selectedItem;
        private string searchText;
        private bool isSearching;
        private bool isLoadingMore;
        private bool hasSearched;
        private bool hasError;
        private string errorMessage;
        private bool hasMore;
        private bool isLoaded;
        private bool isStartingPlayback;
        private string lastQuery;
        private string lastSource;
        private int currentPage;
        private int searchGeneration;
        private CancellationTokenSource searchCancellationTokenSource;
        private ObservableCollection<SearchProvider> contextMenuSearchProviders;
        private ObservableCollection<string> searchSourceOptions = new ObservableCollection<string>();
        private int selectedSearchSource;

        public DelegateCommand LoadedCommand { get; private set; }

        public DelegateCommand UnloadedCommand { get; private set; }

        public DelegateCommand SearchCommand { get; private set; }

        public DelegateCommand LoadMoreCommand { get; private set; }

        public DelegateCommand PlaySelectedCommand { get; private set; }

        public DelegateCommand PlayNextCommand { get; private set; }

        public DelegateCommand AddToNowPlayingCommand { get; private set; }

        public DelegateCommand ShowTrackInformationCommand { get; private set; }

        public DelegateCommand DownloadCommand { get; private set; }

        public DelegateCommand<string> SearchOnlineCommand { get; private set; }

        public CollectionOnlineSearchViewModel(
            IContainerProvider container,
            IGdMusicApiClient apiClient,
            IPlaybackService playbackService,
            ISearchService searchService,
            IDialogService dialogService,
            INeteaseMusicService neteaseMusicService)
        {
            this.container = container;
            this.apiClient = apiClient;
            this.playbackService = playbackService;
            this.searchService = searchService;
            this.dialogService = dialogService;
            this.neteaseMusicService = neteaseMusicService;
            this.providerService = container.Resolve<IProviderService>();
            this.downloadCoordinator = container.Resolve<GdMusicDownloadCoordinator>();

            this.LoadedCommand = new DelegateCommand(this.Loaded);
            this.UnloadedCommand = new DelegateCommand(this.Unloaded);
            this.SearchCommand = new DelegateCommand(
                () => this.SearchAsync(false),
                () => !this.IsSearching);
            this.LoadMoreCommand = new DelegateCommand(
                () => this.SearchAsync(true),
                () => this.HasMore && !this.IsSearching && !this.isLoadingMore);
            this.PlaySelectedCommand = new DelegateCommand(
                () => this.PlaySelectedAsync(),
                () => this.SelectedItem != null && !this.isStartingPlayback);
            this.PlayNextCommand = new DelegateCommand(
                () => this.PlayNextAsync(),
                () => this.CanModifyCurrentOnlineQueue());
            this.AddToNowPlayingCommand = new DelegateCommand(
                () => this.AddToNowPlayingAsync(),
                () => this.CanModifyCurrentOnlineQueue());
            this.ShowTrackInformationCommand = new DelegateCommand(
                () => this.ShowTrackInformation(),
                () => this.SelectedItem != null && this.SelectedItem.SourceInfo != null &&
                    this.SelectedItem.SourceInfo.Kind == TrackSourceKind.Netease);
            this.DownloadCommand = new DelegateCommand(
                () => this.DownloadSelectedAsync(),
                () => this.downloadCoordinator.CanDownload(this.SelectedItem));
            this.SearchOnlineCommand = new DelegateCommand<string>(
                id => this.SearchOnline(id),
                _ => this.SelectedItem != null);

            this.searchService.DoSearch += (_) => this.DispatchFilterRefresh();
            this.playbackService.PlaybackSuccess += (_, __) => this.DispatchPlayingStateRefresh();
            this.playbackService.PlaybackStopped += (_, __) => this.DispatchPlayingStateRefresh();
            this.playbackService.QueueChanged += (_, __) => this.Dispatch(this.RaiseSelectionCommandStates);
            this.providerService.SearchProvidersChanged += (_, __) => this.GetSearchProvidersAsync();

            foreach (string source in GdMusicSettings.SupportedSearchSources)
            {
                this.searchSourceOptions.Add(source);
            }

            this.selectedSearchSource = GetSearchSourceIndex(GdMusicSettings.SearchSource);

            this.RebuildCollectionView();
            this.GetSearchProvidersAsync();
        }

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
                    this.RaiseSelectionCommandStates();
                }
            }
        }

        public string SearchText
        {
            get { return this.searchText; }
            set { SetProperty<string>(ref this.searchText, value); }
        }

        public ObservableCollection<string> SearchSourceOptions => this.searchSourceOptions;

        public int SelectedSearchSource
        {
            get { return this.selectedSearchSource; }
            set
            {
                if (value < 0 || value >= GdMusicSettings.SupportedSearchSources.Count ||
                    !SetProperty<int>(ref this.selectedSearchSource, value))
                {
                    return;
                }

                GdMusicSettings.SearchSource = GdMusicSettings.SupportedSearchSources[value];

                if (!string.IsNullOrWhiteSpace(this.SearchText))
                {
                    this.CancelSearch();
                    this.SearchAsync(false);
                }
            }
        }

        public bool IsSearching
        {
            get { return this.isSearching; }
            private set
            {
                SetProperty<bool>(ref this.isSearching, value);
                this.SearchCommand?.RaiseCanExecuteChanged();
                this.LoadMoreCommand?.RaiseCanExecuteChanged();
                this.RaiseStateProperties();
            }
        }

        public bool IsLoadingMore
        {
            get { return this.isLoadingMore; }
            private set
            {
                SetProperty<bool>(ref this.isLoadingMore, value);
                this.LoadMoreCommand?.RaiseCanExecuteChanged();
                this.RaiseStateProperties();
            }
        }

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

        public bool HasMore
        {
            get { return this.hasMore; }
            private set
            {
                SetProperty<bool>(ref this.hasMore, value);
                this.LoadMoreCommand?.RaiseCanExecuteChanged();
                this.RaiseStateProperties();
            }
        }

        public int Count => this.ItemsCvs == null ? 0 : this.ItemsCvs.View.Cast<TrackViewModel>().Count();

        public bool IsPromptVisible => !this.hasSearched && !this.IsSearching && !this.HasError;

        public bool IsInitialSearching => this.IsSearching && !this.IsLoadingMore;

        public bool IsErrorVisible => this.HasError && this.Items.Count == 0 && !this.IsSearching;

        public bool IsEmptyVisible =>
            this.hasSearched && !this.HasError && !this.IsSearching && this.Items.Count == 0;

        public bool IsListVisible => this.Items.Count > 0;

        public bool IsLoadMoreVisible =>
            this.HasMore && this.Items.Count > 0 && !this.IsSearching && !this.IsLoadingMore;

        public async Task PlayFromAsync(TrackViewModel track)
        {
            if (track == null || this.Items.Count == 0)
            {
                return;
            }

            await this.StartPlaybackAsync(track);
        }

        private void Loaded()
        {
            this.isLoaded = true;
            this.downloadCoordinator.DownloadStateChanged -= this.DownloadCoordinator_DownloadStateChanged;
            this.downloadCoordinator.DownloadStateChanged += this.DownloadCoordinator_DownloadStateChanged;
            this.RaiseSelectionCommandStates();
        }

        private void Unloaded()
        {
            this.isLoaded = false;
            this.CancelSearch();
            this.downloadCoordinator.DownloadStateChanged -= this.DownloadCoordinator_DownloadStateChanged;
        }

        private async void SearchAsync(bool append)
        {
            string query = append ? this.lastQuery : (this.SearchText ?? string.Empty).Trim();
            string source = append ? this.lastSource : GdMusicSettings.SearchSource;
            if (string.IsNullOrWhiteSpace(query) || this.IsSearching || (append && this.isLoadingMore))
            {
                return;
            }

            int page = append ? this.currentPage + 1 : 1;
            this.CancelSearch();
            int generation = Interlocked.Increment(ref this.searchGeneration);
            var cancellationTokenSource = new CancellationTokenSource();
            Interlocked.Exchange(ref this.searchCancellationTokenSource, cancellationTokenSource);
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            this.IsLoadingMore = append;
            this.IsSearching = true;
            this.HasError = false;
            this.ErrorMessage = string.Empty;

            try
            {
                NeteaseResult<IReadOnlyList<GdMusicSearchResult>> result = await this.apiClient.SearchAsync(
                    source,
                    query,
                    PageSize,
                    page,
                    cancellationToken);

                if (generation != this.searchGeneration || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!result.IsSuccess)
                {
                    this.HasError = true;
                    this.ErrorMessage = ResourceUtils.GetString(
                        result.Error != null && !string.IsNullOrWhiteSpace(result.Error.MessageKey)
                            ? result.Error.MessageKey
                            : "Language_GdMusic_Search_Failed");
                    return;
                }

                List<TrackViewModel> mapped = result.Value
                    .Select(song => GdMusicTrackFactory.Create(this.container, song))
                    .Where(track => track != null)
                    .ToList();

                if (append)
                {
                    foreach (TrackViewModel track in mapped)
                    {
                        this.Items.Add(track);
                    }

                    this.ItemsCvs?.View.Refresh();
                }
                else
                {
                    this.Items = new ObservableCollection<TrackViewModel>(mapped);
                    this.SelectedItem = null;
                    this.RebuildCollectionView();
                }

                this.hasSearched = true;
                this.currentPage = page;
                this.lastQuery = query;
                this.lastSource = source;
                this.HasMore = result.Value.Count >= PageSize;
                this.RefreshPlayingState();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not search the GD music platform. ErrorType={0}", ex.GetType().Name);

                if (generation == this.searchGeneration)
                {
                    this.HasError = true;
                    this.ErrorMessage = ResourceUtils.GetString("Language_GdMusic_Search_Failed");
                }
            }
            finally
            {
                if (object.ReferenceEquals(
                    Interlocked.CompareExchange(ref this.searchCancellationTokenSource, null, cancellationTokenSource),
                    cancellationTokenSource))
                {
                    this.IsSearching = false;
                    this.IsLoadingMore = false;
                    this.RaiseStateProperties();
                }

                cancellationTokenSource.Dispose();
            }
        }

        private void CancelSearch()
        {
            Interlocked.Increment(ref this.searchGeneration);
            CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(
                ref this.searchCancellationTokenSource,
                null);
            cancellationTokenSource?.Cancel();

            this.IsSearching = false;
            this.IsLoadingMore = false;
        }

        private async Task StartPlaybackAsync(TrackViewModel startTrack)
        {
            if (startTrack == null || this.isStartingPlayback)
            {
                return;
            }

            this.isStartingPlayback = true;
            this.PlaySelectedCommand.RaiseCanExecuteChanged();

            try
            {
                await this.playbackService.PlayTransientQueueAsync(
                    this.ItemsCvs.View.Cast<TrackViewModel>().ToList(),
                    startTrack,
                    PlaybackQueueContext.OnlineSearch);
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not start the online search queue. ErrorType={0}", ex.GetType().Name);
                this.ErrorMessage = ResourceUtils.GetString("Language_GdMusic_Search_Failed");
            }
            finally
            {
                this.isStartingPlayback = false;
                this.PlaySelectedCommand.RaiseCanExecuteChanged();
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

        private async void DownloadSelectedAsync()
        {
            TrackViewModel target = this.SelectedItem;
            if (this.downloadCoordinator.CanDownload(target))
            {
                await this.downloadCoordinator.DownloadAsync(target);
            }
        }

        private void DownloadCoordinator_DownloadStateChanged(
            object sender,
            NeteaseDownloadStateChangedEventArgs e)
        {
            this.Dispatch(this.RaiseSelectionCommandStates);
        }

        private bool CanModifyCurrentOnlineQueue()
        {
            return this.SelectedItem != null && this.playbackService.HasQueue &&
                this.playbackService.Queue.All(x => x != null && x.IsOnline);
        }

        private void ShowTrackInformation()
        {
            TrackViewModel target = this.SelectedItem;

            if (target == null || target.SourceInfo == null ||
                target.SourceInfo.Kind != TrackSourceKind.Netease)
            {
                return;
            }

            FileInformation view = this.container.Resolve<FileInformation>();
            view.DataContext = new FileInformationViewModel(
                target,
                this.neteaseMusicService,
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
                    "Could not load online search providers for the online search tab. ErrorType={0}",
                    ex.GetType().Name);
                this.ContextMenuSearchProviders = new ObservableCollection<SearchProvider>();
            }
        }

        private void RaiseSelectionCommandStates()
        {
            this.PlaySelectedCommand?.RaiseCanExecuteChanged();
            this.PlayNextCommand?.RaiseCanExecuteChanged();
            this.AddToNowPlayingCommand?.RaiseCanExecuteChanged();
            this.ShowTrackInformationCommand?.RaiseCanExecuteChanged();
            this.DownloadCommand?.RaiseCanExecuteChanged();
            this.SearchOnlineCommand?.RaiseCanExecuteChanged();
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
            string filterText = this.searchService.SearchText;

            e.Accepted = track != null && (string.IsNullOrWhiteSpace(filterText) ||
                Contains(track.TrackTitle, filterText) ||
                Contains(track.ArtistName, filterText) ||
                Contains(track.AlbumTitle, filterText));
        }

        private void DispatchFilterRefresh()
        {
            this.Dispatch(() =>
            {
                this.ItemsCvs?.View.Refresh();
                RaisePropertyChanged(nameof(this.Count));
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

        private void RaiseStateProperties()
        {
            RaisePropertyChanged(nameof(this.Count));
            RaisePropertyChanged(nameof(this.IsPromptVisible));
            RaisePropertyChanged(nameof(this.IsInitialSearching));
            RaisePropertyChanged(nameof(this.IsErrorVisible));
            RaisePropertyChanged(nameof(this.IsEmptyVisible));
            RaisePropertyChanged(nameof(this.IsListVisible));
            RaisePropertyChanged(nameof(this.IsLoadMoreVisible));
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

        private static bool Contains(string value, string filterText)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(filterText, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static int GetSearchSourceIndex(string source)
        {
            for (int i = 0; i < GdMusicSettings.SupportedSearchSources.Count; i++)
            {
                if (string.Equals(GdMusicSettings.SupportedSearchSources[i], source, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
