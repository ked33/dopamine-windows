using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Utils;
using Dopamine.ViewModels;
using Dopamine.Services.Dialog;
using Dopamine.Services.Playback;
using Dopamine.Services.Shell;
using Dopamine.Views.Common;
using Prism.Commands;
using Prism.Ioc;

namespace Dopamine.ViewModels.Common.Base
{
    public class PlaybackControlsViewModelBase : ContextMenuViewModelBase
    {
        private PlaybackInfoViewModel playbackInfoViewModel;

        public DelegateCommand ShowEqualizerCommand { get; set; }

        public IPlaybackService PlaybackService { get; }
        public IDialogService DialogService { get; }
        public IAppVisibilityService AppVisibilityService { get; }

        public PlaybackInfoViewModel PlaybackInfoViewModel
        {
            get { return this.playbackInfoViewModel; }
            set { SetProperty<PlaybackInfoViewModel>(ref this.playbackInfoViewModel, value); }
        }

        public PlaybackControlsViewModelBase(IContainerProvider container) : base(container)
        {
            this.PlaybackService = container.Resolve<IPlaybackService>();
            this.DialogService = container.Resolve<IDialogService>();
            this.AppVisibilityService = container.Resolve<IAppVisibilityService>();
            this.PlaybackService.PlaybackProgressChanged += (_, __) =>
            {
                if (this.CanRefreshUi)
                {
                    this.UpdateTime();
                }
            };
            this.AppVisibilityService.VisibilityChanged += (_, __) =>
            {
                if (this.CanRefreshUi)
                {
                    this.UpdateTime();
                }
            };

            this.ShowEqualizerCommand = new DelegateCommand(() =>
            {
                EqualizerControl view = container.Resolve<EqualizerControl>();
                view.DataContext = container.Resolve<EqualizerControlViewModel>();

                this.DialogService.ShowCustomDialog(
                     new EqualizerIcon() { IsDialogIcon = true },
                     0,
                     ResourceUtils.GetString("Language_Equalizer"),
                     view,
                     570,
                     0,
                     false,
                     true,
                     true,
                     false,
                     ResourceUtils.GetString("Language_Close"),
                     string.Empty,
                     null);
            });

            this.Reset();
        }

        protected void UpdateTime()
        {
            if (!this.CanRefreshUi || this.PlaybackInfoViewModel == null)
            {
                return;
            }

            this.PlaybackInfoViewModel.CurrentTime = FormatUtils.FormatTime(this.PlaybackService.GetCurrentTime);
            this.PlaybackInfoViewModel.TotalTime = " / " + FormatUtils.FormatTime(this.PlaybackService.GetTotalTime);
        }

        protected bool CanRefreshUi
        {
            get { return !this.AppVisibilityService.IsBackgroundPlaybackMode; }
        }

        protected void Reset()
        {
            this.PlaybackInfoViewModel = new PlaybackInfoViewModel
            {
                Title = string.Empty,
                Artist = string.Empty,
                Album = string.Empty,
                Year = string.Empty,
                CurrentTime = string.Empty,
                TotalTime = string.Empty
            };
        }

        protected override void SearchOnline(string id)
        {
            // No implementation required here
        }
    }
}
