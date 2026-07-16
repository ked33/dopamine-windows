using Dopamine.Core.Base;
using Dopamine.Core.Utils;
using Dopamine.Services.Entities;
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
    public sealed class CollectionIntelligenceRecommendationsViewModel : BindableBase
    {
        private static readonly object RandomLock = new object();
        private static readonly Random Random = new Random();

        private readonly IContainerProvider container;
        private readonly INeteaseMusicService musicService;
        private readonly INeteaseSessionService sessionService;
        private readonly IPlaybackService playbackService;
        private readonly ISearchService searchService;

        private ObservableCollection<TrackViewModel> items = new ObservableCollection<TrackViewModel>();
        private CollectionViewSource itemsCvs;
        private TrackViewModel selectedItem;
        private bool isLoaded;
        private bool isGenerating;
        private bool isStartingPlayback;
        private string errorMessage;
        private CancellationTokenSource cancellationTokenSource;

        public CollectionIntelligenceRecommendationsViewModel(
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

            this.RebuildCollectionView();
        }

        public DelegateCommand LoadedCommand { get; private set; }

        public DelegateCommand UnloadedCommand { get; private set; }

        public DelegateCommand GenerateCommand { get; private set; }

        public DelegateCommand PlayAllCommand { get; private set; }

        public DelegateCommand PlaySelectedCommand { get; private set; }

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
                    this.PlaySelectedCommand.RaiseCanExecuteChanged();
                }
            }
        }

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
                    RaisePropertyChanged(nameof(this.IsEmptyVisible));
                }
            }
        }

        public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

        public int Count => this.ItemsCvs == null ? 0 : this.ItemsCvs.View.Cast<TrackViewModel>().Count();

        public bool IsListVisible => this.Items.Count > 0;

        public bool IsLoggedOutVisible => !this.IsLoggedIn && !this.IsGenerating;

        public bool IsEmptyVisible => this.IsLoggedIn && !this.IsGenerating && !this.HasError && this.Items.Count == 0;

        private void Loaded()
        {
            if (this.isLoaded)
            {
                return;
            }

            this.isLoaded = true;
            this.sessionService.SessionChanged += this.SessionService_SessionChanged;
            this.searchService.DoSearch += this.SearchService_DoSearch;
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

        private async void StartPlaybackAsync(TrackViewModel startTrack)
        {
            if (startTrack == null || this.isStartingPlayback)
            {
                return;
            }

            this.isStartingPlayback = true;
            this.PlayAllCommand.RaiseCanExecuteChanged();
            this.PlaySelectedCommand.RaiseCanExecuteChanged();

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
                this.PlaySelectedCommand.RaiseCanExecuteChanged();
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
                RaisePropertyChanged(nameof(this.IsLoggedIn));

                if (!this.IsLoggedIn)
                {
                    this.CancelRequest();
                    this.Items = new ObservableCollection<TrackViewModel>();
                    this.ErrorMessage = null;
                    this.RebuildCollectionView();
                }

                this.RaiseStateProperties();
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
            RaisePropertyChanged(nameof(this.Count));
            this.GenerateCommand?.RaiseCanExecuteChanged();
            this.PlayAllCommand?.RaiseCanExecuteChanged();
            this.PlaySelectedCommand?.RaiseCanExecuteChanged();
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
