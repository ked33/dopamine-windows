using Digimezzo.Foundation.Core.Utils;
using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using Dopamine.Core.Utils;
using Dopamine.Data.Entities;
using Dopamine.Services.Entities;
using Dopamine.Services.Extensions;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
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

        public DelegateCommand LoadedCommand { get; private set; }

        public DelegateCommand UnloadedCommand { get; private set; }

        public DelegateCommand RefreshCommand { get; private set; }

        public DelegateCommand PlayAllCommand { get; private set; }

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
            set { SetProperty<TrackViewModel>(ref this.selectedItem, value); }
        }

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
            ISearchService searchService)
        {
            this.container = container;
            this.musicService = musicService;
            this.sessionService = sessionService;
            this.playbackService = playbackService;
            this.searchService = searchService;

            this.LoadedCommand = new DelegateCommand(() => this.LoadedAsync());
            this.UnloadedCommand = new DelegateCommand(this.Unloaded);
            this.RefreshCommand = new DelegateCommand(
                async () => await this.LoadAsync(true),
                () => !this.IsInitialLoading && !this.IsRefreshing);
            this.PlayAllCommand = new DelegateCommand(
                () => this.PlayAllAsync(),
                () => this.Items.Count > 0 && !this.isStartingPlayback);

            this.searchService.DoSearch += (_) => this.DispatchFilterRefresh();
            this.sessionService.SessionChanged += (_, __) => this.DispatchSessionChanged();
            this.playbackService.PlaybackSuccess += (_, __) => this.DispatchPlayingStateRefresh();
            this.playbackService.PlaybackStopped += (_, __) => this.DispatchPlayingStateRefresh();
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
        }

        public async Task PlayFromAsync(TrackViewModel track)
        {
            if (track == null || this.Items.Count == 0)
            {
                return;
            }

            await this.StartPlaybackAsync(track);
        }

        private async void LoadedAsync()
        {
            this.isLoaded = true;
            this.RaiseStateProperties();

            if (this.IsLoggedIn && this.Items.Count == 0)
            {
                await this.LoadAsync(false);
            }
        }

        private void Unloaded()
        {
            this.isLoaded = false;
            this.CancelLoading();
        }

        private async void PlayAllAsync()
        {
            if (this.Items.Count > 0)
            {
                await this.StartPlaybackAsync(this.Items[0]);
            }
        }

        private async Task LoadAsync(bool forceRefresh)
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
                    await this.musicService.GetDailyRecommendationsAsync(forceRefresh, cancellationToken);

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
                await this.playbackService.PlayTransientQueueAsync(this.Items.ToList(), startTrack, true);
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
            }
        }

        private TrackViewModel MapTrack(NeteaseRecommendedSong song)
        {
            string path = "netease://song/" + song.Id;
            string artists = string.Join(string.Empty, (song.Artists ?? new List<string>()).Select(FormatUtils.DelimitValue));
            var track = new Track
            {
                Path = path,
                SafePath = path,
                FileName = song.Name ?? string.Empty,
                TrackTitle = song.Name ?? string.Empty,
                Artists = artists,
                AlbumArtists = artists,
                AlbumTitle = song.AlbumName ?? string.Empty,
                AlbumKey = FormatUtils.DelimitValue(song.AlbumName ?? string.Empty) + artists.ToLowerInvariant(),
                TrackNumber = 0,
                TrackCount = 0,
                DiscNumber = 0,
                DiscCount = 0,
                Duration = song.DurationMilliseconds,
                Year = 0,
                HasLyrics = 0,
                FileSize = 0,
                BitRate = 0,
                SampleRate = 0,
                DateAdded = DateTime.Now.Ticks,
                DateFileCreated = 0,
                DateLastSynced = 0,
                DateFileModified = 0,
                Rating = 0,
                Love = 0,
                PlayCount = 0,
                SkipCount = 0
            };

            TrackViewModel viewModel = this.container.ResolveTrackViewModel(track);
            viewModel.SourceInfo = new TrackSourceInfo
            {
                Kind = TrackSourceKind.Netease,
                ProviderId = "netease",
                RemoteId = song.Id,
                ArtworkUrl = song.ArtworkUrl
            };
            return viewModel;
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
                RaisePropertyChanged(nameof(this.IsLoggedIn));

                if (!this.IsLoggedIn)
                {
                    this.CancelLoading();
                    this.Items = new ObservableCollection<TrackViewModel>();
                    this.RebuildCollectionView();
                }
                else if (this.isLoaded)
                {
                    this.LoadAsync(false);
                }

                this.RaiseStateProperties();
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
