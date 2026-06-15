using CommonServiceLocator;
using Dopamine.Core.Settings;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Enums;
using Dopamine.Core.Helpers;
using Dopamine.Services.Playback;
using Dopamine.Services.Shell;
using Dopamine.ViewModels.Common;
using System.Windows;
using System.Windows.Controls;

namespace Dopamine.Views.Common
{
    public partial class SpectrumAnalyzerControl : UserControl
    {
        private IPlaybackService playbackService;
        private IShellService shellService;
        private bool hasSubscribedToEvents;

        public new object DataContext
        {
            get { return base.DataContext; }
            set { base.DataContext = value; }
        }

        public SpectrumAnalyzerControl()
        {
            InitializeComponent();

            this.playbackService = ServiceLocator.Current.GetInstance<IPlaybackService>();
            this.shellService = ServiceLocator.Current.GetInstance<IShellService>();

            this.DataContextChanged += this.SpectrumAnalyzerControl_DataContextChanged;
            this.Loaded += this.SpectrumAnalyzerControl_Loaded;
            this.Unloaded += this.SpectrumAnalyzerControl_Unloaded;
        }

        private void SpectrumAnalyzerControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SpectrumAnalyzerControlViewModel oldViewModel = e.OldValue as SpectrumAnalyzerControlViewModel;
            oldViewModel?.Deactivate();

            if (!this.hasSubscribedToEvents)
            {
                return;
            }

            SpectrumAnalyzerControlViewModel newViewModel = e.NewValue as SpectrumAnalyzerControlViewModel;
            newViewModel?.Activate();
        }

        private void SpectrumAnalyzerControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.SubscribeToEvents();
            this.ActivateViewModel();
            this.TryRegisterSpectrumPlayers();
        }

        private void SpectrumAnalyzerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            this.UnregisterSpectrumPlayers();
            this.DeactivateViewModel();
            this.UnsubscribeFromEvents();
        }

        private void SubscribeToEvents()
        {
            if (this.hasSubscribedToEvents)
            {
                return;
            }

            this.playbackService.PlaybackSuccess += this.PlaybackService_PlaybackSuccess;
            this.shellService.WindowStateChanged += this.ShellService_WindowStateChanged;
            SettingsClient.SettingChanged += this.SettingsClient_SettingChanged;
            this.hasSubscribedToEvents = true;
        }

        private void UnsubscribeFromEvents()
        {
            if (!this.hasSubscribedToEvents)
            {
                return;
            }

            this.playbackService.PlaybackSuccess -= this.PlaybackService_PlaybackSuccess;
            this.shellService.WindowStateChanged -= this.ShellService_WindowStateChanged;
            SettingsClient.SettingChanged -= this.SettingsClient_SettingChanged;
            this.hasSubscribedToEvents = false;
        }

        private void ActivateViewModel()
        {
            SpectrumAnalyzerControlViewModel viewModel = this.DataContext as SpectrumAnalyzerControlViewModel;
            viewModel?.Activate();
        }

        private void DeactivateViewModel()
        {
            SpectrumAnalyzerControlViewModel viewModel = this.DataContext as SpectrumAnalyzerControlViewModel;
            viewModel?.Deactivate();
        }

        private void PlaybackService_PlaybackSuccess(object sender, PlaybackSuccessEventArgs e)
        {
            this.TryRegisterSpectrumPlayers();
        }

        private void ShellService_WindowStateChanged(object sender, WindowStateChangedEventArgs e)
        {
            this.TryRegisterSpectrumPlayers();
        }

        private void SettingsClient_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (SettingsClient.IsSettingChanged(e, "Playback", "ShowSpectrumAnalyzer") ||
                SettingsClient.IsSettingChanged(e, "Appearance", "EnableAnimations"))
            {
                this.TryRegisterSpectrumPlayers();
            }
        }

        private void TryRegisterSpectrumPlayers()
        {
            this.UnregisterSpectrumPlayers();

            if (!this.playbackService.HasMediaFoundationSupport)
            {
                return;
            }

            if (!SettingsClient.Get<bool>("Playback", "ShowSpectrumAnalyzer"))
            {
                // The settings don't allow showing the spectrum analyzer
                return;
            }

            if (!UiAnimationSettings.AreAnimationsEnabled)
            {
                // The animation settings don't allow showing animated spectrum visuals
                return;
            }

            if (this.shellService.WindowState == WindowState.Minimized)
            {
                // The window state doesn't allow showing the spectrum analyzer
                return;
            }

            Application.Current.Dispatcher.Invoke(() => this.SpectrumContainer.Visibility = Visibility.Visible);

            if (this.playbackService.Player != null)
            {
                Application.Current.Dispatcher.Invoke(() => this.LeftSpectrumAnalyzer.RegisterSoundPlayer(this.playbackService.Player.GetWrapperSpectrumPlayer(SpectrumChannel.Left)));
                Application.Current.Dispatcher.Invoke(() => this.RightSpectrumAnalyzer.RegisterSoundPlayer(this.playbackService.Player.GetWrapperSpectrumPlayer(SpectrumChannel.Right)));
            }
        }

        private void UnregisterSpectrumPlayers()
        {
            Application.Current.Dispatcher.Invoke(() => this.SpectrumContainer.Visibility = Visibility.Collapsed);
            Application.Current.Dispatcher.Invoke(() => this.LeftSpectrumAnalyzer.UnregisterSoundPlayer());
            Application.Current.Dispatcher.Invoke(() => this.RightSpectrumAnalyzer.UnregisterSoundPlayer());
        }
    }
}
