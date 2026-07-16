using Dopamine.Core.Base;
using Dopamine.Core.Utils;
using Dopamine.Services.Online.Netease;
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
        private bool isLoaded;
        private CancellationTokenSource operationCancellationTokenSource;

        public CollectionPersonalFmViewModel(
            INeteasePersonalFmService personalFmService,
            INeteaseSessionService sessionService)
        {
            this.personalFmService = personalFmService;
            this.sessionService = sessionService;

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
                () => this.IsActive && !this.IsBusy && this.CurrentTrack != null);
            this.ExitCommand = new DelegateCommand(
                this.Exit,
                () => this.IsActive);
        }

        public DelegateCommand LoadedCommand { get; private set; }

        public DelegateCommand UnloadedCommand { get; private set; }

        public DelegateCommand StartCommand { get; private set; }

        public DelegateCommand SkipCommand { get; private set; }

        public DelegateCommand DislikeCommand { get; private set; }

        public DelegateCommand ExitCommand { get; private set; }

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

        public string ErrorMessage => this.personalFmService.Error == null
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
            this.RefreshState();
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
            this.CancelOperation();
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

        private void Exit()
        {
            this.CancelOperation();
            this.personalFmService.Exit();
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
            this.Dispatch(this.RefreshState);
        }

        private void SessionService_SessionChanged(object sender, EventArgs e)
        {
            this.Dispatch(this.RefreshState);
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
            RaisePropertyChanged(nameof(this.ErrorMessage));
            RaisePropertyChanged(nameof(this.HasError));
            this.StartCommand.RaiseCanExecuteChanged();
            this.SkipCommand.RaiseCanExecuteChanged();
            this.DislikeCommand.RaiseCanExecuteChanged();
            this.ExitCommand.RaiseCanExecuteChanged();
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
