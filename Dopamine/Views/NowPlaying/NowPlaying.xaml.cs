using CommonServiceLocator;
using Dopamine.Services.Shell;
using System;
using System.Timers;
using System.Windows;
using System.Windows.Controls;

namespace Dopamine.Views.NowPlaying
{
    public partial class NowPlaying : UserControl
    {
        private Timer hideControlsTimer = new Timer();
        private IAppVisibilityService appVisibilityService;

        public bool CanShowControls
        {
            get { return Convert.ToBoolean(GetValue(CanShowControlsProperty)); }
            set { SetValue(CanShowControlsProperty, value); }
        }

        public static readonly DependencyProperty CanShowControlsProperty =
            DependencyProperty.Register(nameof(CanShowControls), typeof(bool), typeof(NowPlaying), new PropertyMetadata(null));

        public NowPlaying()
        {
            InitializeComponent();

            this.appVisibilityService = ServiceLocator.Current.GetInstance<IAppVisibilityService>();
            this.appVisibilityService.VisibilityChanged += (_, __) => this.HandleVisibilityChanged();

            this.hideControlsTimer.Interval = 2000;
            this.hideControlsTimer.Elapsed += new ElapsedEventHandler(this.CleanupNowPlayingHandler);
            this.ShowControls();
        }

        private bool CanRunHideControlsTimer
        {
            get { return !this.appVisibilityService.IsBackgroundPlaybackMode; }
        }

        private void ShowControls()
        {
            this.hideControlsTimer.Stop();
            this.CanShowControls = true;

            if (this.CanRunHideControlsTimer)
            {
                this.hideControlsTimer.Start();
            }
        }

        private void HandleVisibilityChanged()
        {
            if (this.CanRunHideControlsTimer)
            {
                this.ShowControls();
            }
            else
            {
                this.hideControlsTimer.Stop();
            }
        }

        public void CleanupNowPlayingHandler(object sender, ElapsedEventArgs e)
        {
            if (!this.CanRunHideControlsTimer)
            {
                this.hideControlsTimer.Stop();
                return;
            }

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!this.BackButton.IsMouseOver)
                {
                    this.CanShowControls = false;
                }
            }));
        }

        private void NowPlaying_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!this.CanRunHideControlsTimer)
            {
                return;
            }

            this.ShowControls();
        }

        private void SpectrumAnalyzer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.AlignSpectrumAnalyzer();
        }

        private void NowPlaying_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.AlignSpectrumAnalyzer();
            this.AlignBackgroundCoverArt();
        }

        private void AlignSpectrumAnalyzer()
        {
            // This makes sure the spectrum analyzer is centered on the screen, based on the left pixel.
            // When we align center, alignment is sometimes (depending on the width of the screen) done
            // on a half pixel. This causes a blurry spectrum analyzer.
            try
            {
                this.SpectrumAnalyzer.Margin = new Thickness(Convert.ToInt32(this.ActualWidth / 2) - Convert.ToInt32(this.SpectrumAnalyzer.ActualWidth / 2), 0, 0, 0);
            }
            catch (Exception)
            {
                // Swallow this exception
            }
        }

        private void AlignBackgroundCoverArt()
        {
            try
            {
                this.BackgroundCoverArtControl.Margin = new Thickness(0, -Convert.ToInt32(this.ActualHeight / 2), 0, 0);
            }
            catch (Exception)
            {
                // Swallow this exception
            }
        }
    }
}
