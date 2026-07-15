using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Base;
using Dopamine.Core.Enums;
using Dopamine.Core.Logging;
using Dopamine.Core.Prism;
using Dopamine.Services.Dialog;
using Dopamine.Services.Folders;
using Dopamine.Services.Indexing;
using Dopamine.Views.FullPlayer;
using Prism.Commands;
using Prism.Ioc;
using Prism.Mvvm;
using Prism.Regions;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Dopamine.ViewModels.FullPlayer
{
    public class FullPlayerViewModel : BindableBase
    {
        private IRegionManager regionManager;
        private FullPlayerPage previousSelectedFullPlayerPage;
        private IIndexingService indexingService;
        private IContainerProvider container;
        private IDialogService dialogService;
        private IFoldersService foldersService;
        private int slideInFrom;
        private bool showBackButton;
        private int navigationGeneration;

        public DelegateCommand LoadedCommand { get; set; }

        public DelegateCommand ManageCollectionCommand { get; set; }

        public DelegateCommand<string> SetSelectedFullPlayerPageCommand { get; set; }

        public DelegateCommand BackButtonCommand { get; set; }

        public int SlideInFrom
        {
            get { return this.slideInFrom; }
            set { SetProperty<int>(ref this.slideInFrom, value); }
        }

        public bool ShowBackButton
        {
            get { return this.showBackButton; }
            set { SetProperty<bool>(ref this.showBackButton, value); }
        }

        public FullPlayerViewModel(IIndexingService indexingService, IRegionManager regionManager,
            IContainerProvider container, IDialogService dialogService, IFoldersService foldersService)
        {
            this.regionManager = regionManager;
            this.indexingService = indexingService;
            this.container = container;
            this.dialogService = dialogService;
            this.foldersService = foldersService;
            this.LoadedCommand = new DelegateCommand(() => this.NagivateToSelectedPage(FullPlayerPage.Collection));
            this.SetSelectedFullPlayerPageCommand = new DelegateCommand<string>(pageIndex => this.NagivateToSelectedPage((FullPlayerPage)Int32.Parse(pageIndex)));
            this.BackButtonCommand = new DelegateCommand(() => this.NagivateToSelectedPage(FullPlayerPage.Collection));
            this.ManageCollectionCommand = new DelegateCommand(() => this.ManageCollectionAsync());
        }

        private void NagivateToSelectedPage(FullPlayerPage page)
        {
            int generation = Interlocked.Increment(ref this.navigationGeneration);
            this.SlideInFrom = page <= this.previousSelectedFullPlayerPage ? -Constants.SlideDistance : Constants.SlideDistance;
            this.previousSelectedFullPlayerPage = page;

            switch (page)
            {
                case FullPlayerPage.Collection:
                    this.ShowBackButton = false;
                    this.NavigateContentThenMenu(
                        typeof(Views.FullPlayer.Collection.Collection).FullName,
                        typeof(Views.FullPlayer.Collection.CollectionMenu).FullName,
                        generation);
                    break;
                case FullPlayerPage.Settings:
                    this.ShowBackButton = true;
                    this.NavigateContentThenMenu(
                        typeof(Views.FullPlayer.Settings.Settings).FullName,
                        typeof(Views.FullPlayer.Settings.SettingsMenu).FullName,
                        generation);
                    break;
                case FullPlayerPage.Information:
                    this.ShowBackButton = true;
                    this.NavigateContentThenMenu(
                        typeof(Views.FullPlayer.Information.Information).FullName,
                        typeof(Views.FullPlayer.Information.InformationMenu).FullName,
                        generation);
                    break;
                default:
                    break;
            }
        }

        private void NavigateContentThenMenu(string contentTarget, string menuTarget, int generation)
        {
            AppLog.InfoAlways(
                "Full player content navigation requested. Region={0}, Target={1}",
                RegionNames.FullPlayerRegion,
                contentTarget);
            this.regionManager.RequestNavigate(RegionNames.FullPlayerRegion, contentTarget, result =>
            {
                if (generation != this.navigationGeneration)
                {
                    return;
                }

                if (result.Result != true)
                {
                    string error = result.Error == null
                        ? "No exception was provided by Prism."
                        : LogClient.GetAllExceptions(result.Error);
                    AppLog.ErrorAlways(
                        "Full player content navigation failed. Target={0}, Error={1}",
                        contentTarget,
                        error);
                    return;
                }

                AppLog.InfoAlways(
                    "Full player content navigation completed successfully. Target={0}",
                    contentTarget);
                this.DispatchMenuNavigation(menuTarget, generation);
            });
        }

        private void DispatchMenuNavigation(string menuTarget, int generation)
        {
            Action navigate = () =>
            {
                if (generation != this.navigationGeneration)
                {
                    return;
                }

                AppLog.InfoAlways(
                    "Full player menu navigation requested. Region={0}, Target={1}",
                    RegionNames.FullPlayerMenuRegion,
                    menuTarget);
                this.regionManager.RequestNavigate(RegionNames.FullPlayerMenuRegion, menuTarget, result =>
                {
                    if (generation != this.navigationGeneration)
                    {
                        return;
                    }

                    if (result.Result == true)
                    {
                        AppLog.InfoAlways(
                            "Full player menu navigation completed successfully. Target={0}",
                            menuTarget);
                        return;
                    }

                    string error = result.Error == null
                        ? "No exception was provided by Prism."
                        : LogClient.GetAllExceptions(result.Error);
                    AppLog.ErrorAlways(
                        "Full player menu navigation failed. Target={0}, Error={1}",
                        menuTarget,
                        error);
                });
            };

            if (Application.Current == null)
            {
                navigate();
                return;
            }

            Application.Current.Dispatcher.BeginInvoke(navigate, DispatcherPriority.Loaded);
        }

        private async void ManageCollectionAsync()
        {
            FullPlayerAddMusic view = this.container.Resolve<FullPlayerAddMusic>();
            view.DataContext = this.container.Resolve<FullPlayerAddMusicViewModel>();

            this.dialogService.ShowCustomDialog(
                0xE8D6,
                16,
                ResourceUtils.GetString("Language_Manage_Collection"),
                view,
                500,
                400,
                false,
                false,
                false,
                false,
                ResourceUtils.GetString("Language_Ok"),
                string.Empty,
                null);

            await this.foldersService.SaveToggledFoldersAsync();
            this.indexingService.RefreshCollectionIfFoldersChangedAsync();
        }
    }
}
