using Digimezzo.Foundation.Core.Settings;
using Digimezzo.Foundation.Core.Logging;
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

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public class CollectionMenuViewModel : BindableBase
    {
        private IEventAggregator eventAggregator;
        private IRegionManager regionManager;
        private CollectionPage previousPage;
        private CollectionPage selectedPage;

        public DelegateCommand LoadedCommand { get; set; }

        public CollectionPage SelectedPage
        {
            get { return this.selectedPage; }
            set
            {
                SetProperty<CollectionPage>(ref this.selectedPage, value);
                SettingsClient.Set<int>("FullPlayer", "SelectedCollectionPage", (int)value);
                this.NagivateToSelectedPage();
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

        private void NagivateToSelectedPage()
        {
            CollectionPage page = this.selectedPage;
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

            AppLog.InfoAlways(
                "Collection navigation requested. Page={0}, Region={1}, Target={2}",
                page,
                RegionNames.CollectionRegion,
                target);
            this.regionManager.RequestNavigate(RegionNames.CollectionRegion, target, result =>
            {
                if (result.Result == true)
                {
                    AppLog.InfoAlways(
                        "Collection navigation completed successfully. Page={0}, Target={1}",
                        page,
                        target);
                    return;
                }

                string error = result.Error == null
                    ? "No exception was provided by Prism."
                    : LogClient.GetAllExceptions(result.Error);
                AppLog.ErrorAlways(
                    "Collection navigation failed. Page={0}, Target={1}, Error={2}",
                    page,
                    target,
                    error);
            });
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
