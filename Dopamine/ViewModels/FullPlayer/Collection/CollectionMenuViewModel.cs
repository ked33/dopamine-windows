using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Digimezzo.Foundation.WPF.Controls;
using Dopamine.Core.Enums;
using Dopamine.Core.Logging;
using Dopamine.Core.Prism;
using Dopamine.Views.FullPlayer.Collection;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public class CollectionMenuViewModel : BindableBase
    {
        private IEventAggregator eventAggregator;
        private IRegionManager regionManager;
        private CollectionPage previousPage;
        private CollectionPage selectedPage;
        private int navigationGeneration;

        private sealed class NavigationAttempt
        {
            public bool IsSuccess { get; set; }

            public Exception Error { get; set; }
        }

        public DelegateCommand LoadedCommand { get; set; }

        public CollectionPage SelectedPage
        {
            get { return this.selectedPage; }
            set
            {
                SetProperty<CollectionPage>(ref this.selectedPage, value);
                SettingsClient.Set<int>("FullPlayer", "SelectedCollectionPage", (int)value);
                this.NagivateToSelectedPageAsync();
            }
        }

        public CollectionMenuViewModel(IEventAggregator eventAggregator, IRegionManager regionManager)
        {
            this.eventAggregator = eventAggregator;
            this.regionManager = regionManager;

            this.LoadedCommand = new DelegateCommand(() =>
            {
                if (SettingsClient.Get<bool>("Startup", "ShowLastSelectedPage"))
                {
                    int savedPage = SettingsClient.Get<int>("FullPlayer", "SelectedCollectionPage");
                    this.SelectedPage = Enum.IsDefined(typeof(CollectionPage), savedPage)
                        ? (CollectionPage)savedPage
                        : CollectionPage.Artists;
                }
                else
                {
                    this.SelectedPage = CollectionPage.Artists;
                }
            });
        }

        private async void NagivateToSelectedPageAsync()
        {
            CollectionPage page = this.selectedPage;
            int generation = Interlocked.Increment(ref this.navigationGeneration);
            SlideDirection direction = page >= this.previousPage
                ? SlideDirection.RightToLeft
                : SlideDirection.LeftToRight;
            this.eventAggregator.GetEvent<IsCollectionPageChanged>().Publish(
                new Tuple<SlideDirection, CollectionPage>(direction, page));
            this.previousPage = page;

            string target = this.GetNavigationTarget(page);

            if (string.IsNullOrWhiteSpace(target))
            {
                AppLog.ErrorAlways(
                    "Collection navigation target is invalid. Page={0}",
                    page);
                return;
            }

            if (!await this.WaitForCollectionRegionAsync(generation))
            {
                if (generation == this.navigationGeneration)
                {
                    string registeredRegions = string.Join(
                        ",",
                        this.regionManager.Regions.Select(x => x.Name).OrderBy(x => x));
                    AppLog.ErrorAlways(
                        "Collection navigation failed because the region was not registered. Page={0}, Region={1}, RegisteredRegions={2}",
                        page,
                        RegionNames.CollectionRegion,
                        registeredRegions);
                }

                return;
            }

            AppLog.InfoAlways(
                "Collection navigation requested. Page={0}, Region={1}, Target={2}",
                page,
                RegionNames.CollectionRegion,
                target);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                NavigationAttempt result = await this.RequestNavigationAsync(target);

                if (generation != this.navigationGeneration)
                {
                    return;
                }

                if (result.IsSuccess)
                {
                    AppLog.InfoAlways(
                        "Collection navigation completed successfully. Page={0}, Target={1}, Attempt={2}",
                        page,
                        target,
                        attempt);
                    return;
                }

                if (result.Error != null)
                {
                    AppLog.ErrorAlways(
                        "Collection navigation failed. Page={0}, Target={1}, Attempt={2}, Error={3}",
                        page,
                        target,
                        attempt,
                        LogClient.GetAllExceptions(result.Error));
                    return;
                }

                if (attempt < 3)
                {
                    await Task.Delay(100);
                }
            }

            AppLog.ErrorAlways(
                "Collection navigation failed after transient retries. Page={0}, Target={1}",
                page,
                target);
        }

        private async Task<bool> WaitForCollectionRegionAsync(int generation)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                if (generation != this.navigationGeneration)
                {
                    return false;
                }

                if (this.regionManager.Regions.ContainsRegionWithName(RegionNames.CollectionRegion))
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return false;
        }

        private async Task<NavigationAttempt> RequestNavigationAsync(string target)
        {
            var completionSource = new TaskCompletionSource<NavigationAttempt>();

            try
            {
                this.regionManager.RequestNavigate(RegionNames.CollectionRegion, target, result =>
                {
                    completionSource.TrySetResult(new NavigationAttempt
                    {
                        IsSuccess = result.Result == true,
                        Error = result.Error
                    });
                });
            }
            catch (Exception ex)
            {
                return new NavigationAttempt { Error = ex };
            }

            Task completedTask = await Task.WhenAny(completionSource.Task, Task.Delay(2000));

            return object.ReferenceEquals(completedTask, completionSource.Task)
                ? await completionSource.Task
                : new NavigationAttempt
                {
                    Error = new TimeoutException("Prism collection navigation callback timed out.")
                };
        }

        private string GetNavigationTarget(CollectionPage page)
        {
            switch (page)
            {
                case CollectionPage.Artists:
                    return typeof(CollectionArtists).FullName;
                case CollectionPage.Genres:
                    return typeof(CollectionGenres).FullName;
                case CollectionPage.Albums:
                    return typeof(CollectionAlbums).FullName;
                case CollectionPage.Songs:
                    return typeof(CollectionTracks).FullName;
                case CollectionPage.Playlists:
                    return typeof(CollectionPlaylists).FullName;
                case CollectionPage.Folders:
                    return typeof(CollectionFolders).FullName;
                case CollectionPage.DailyRecommendations:
                    return typeof(CollectionDailyRecommendations).FullName;
                default:
                    return string.Empty;
            }
        }
    }
}
