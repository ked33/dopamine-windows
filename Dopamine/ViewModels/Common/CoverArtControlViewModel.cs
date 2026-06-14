using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.WPF.Controls;
using Dopamine.Data.Entities;
using Dopamine.ViewModels;
using Dopamine.Services.Cache;
using Dopamine.Services.Metadata;
using Dopamine.Services.Playback;
using Dopamine.Services.Shell;
using Prism.Mvvm;
using System;
using System.Threading.Tasks;
using System.Timers;
using Dopamine.Services.Entities;

namespace Dopamine.ViewModels.Common
{
    public class CoverArtControlViewModel : BindableBase
    {
        protected CoverArtViewModel coverArtViewModel;
        protected IPlaybackService playbackService;
        private ICacheService cacheService;
        private IMetadataService metadataService;
        private IAppVisibilityService appVisibilityService;
        private SlideDirection slideDirection;
        private byte[] previousArtwork;
        private byte[] artwork;

        public CoverArtViewModel CoverArtViewModel
        {
            get { return this.coverArtViewModel; }
            set { SetProperty<CoverArtViewModel>(ref this.coverArtViewModel, value); }
        }

        public SlideDirection SlideDirection
        {
            get { return this.slideDirection; }
            set { SetProperty<SlideDirection>(ref this.slideDirection, value); }
        }

        private void ClearArtwork()
        {
            this.CoverArtViewModel = new CoverArtViewModel { CoverArt = null };
            this.artwork = null;
        }

        public CoverArtControlViewModel(IPlaybackService playbackService, ICacheService cacheService, IMetadataService metadataService,
            IAppVisibilityService appVisibilityService)
        {
            this.playbackService = playbackService;
            this.cacheService = cacheService;
            this.metadataService = metadataService;
            this.appVisibilityService = appVisibilityService;

            this.playbackService.PlaybackSuccess += (_, e) =>
            {
                if (!this.CanLoadArtwork)
                {
                    this.ClearArtwork();
                    return;
                }

                this.SlideDirection = e.IsPlayingPreviousTrack ? SlideDirection.UpToDown : SlideDirection.DownToUp;
                this.RefreshCoverArtAsync(this.playbackService.CurrentTrack);
            };

            this.playbackService.PlayingTrackChanged += (_, __) =>
            {
                if (this.CanLoadArtwork)
                {
                    this.RefreshCoverArtAsync(this.playbackService.CurrentTrack);
                }
                else
                {
                    this.ClearArtwork();
                }
            };

            this.appVisibilityService.VisibilityChanged += (_, __) =>
            {
                if (this.CanLoadArtwork)
                {
                    this.RefreshCoverArtAsync(this.playbackService.CurrentTrack);
                }
                else
                {
                    this.ClearArtwork();
                }
            };

            // Defaults
            this.SlideDirection = SlideDirection.DownToUp;
            this.RefreshCoverArtAsync(this.playbackService.CurrentTrack);
        }

        private void RefreshTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.RefreshCoverArtAsync(this.playbackService.CurrentTrack);
        }

        protected async virtual void RefreshCoverArtAsync(TrackViewModel track)
        {
            await Task.Delay(250);

            if (!this.CanLoadArtwork)
            {
                this.ClearArtwork();
                return;
            }

            await Task.Run(async () =>
            {
                if (!this.CanLoadArtwork)
                {
                    this.ClearArtwork();
                    return;
                }

                this.previousArtwork = this.artwork;

                // No track selected: clear cover art.
                if (track == null)
                {
                    this.ClearArtwork();
                    return;
                }

                // Try to find artwork
                byte[] artwork = null;

                try
                {
                    artwork = await this.metadataService.GetArtworkAsync(track.Path);
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not get artwork for Track {0}. Exception: {1}", track.Path, ex.Message);
                }

                this.artwork = artwork;

                // Verify if the artwork changed
                if ((this.artwork != null & this.previousArtwork != null) && (this.artwork.LongLength == this.previousArtwork.LongLength))
                {
                    return;
                }
                else if (this.artwork == null & this.previousArtwork == null & this.CoverArtViewModel != null)
                {
                    return;
                }

                if (artwork != null)
                {
                    if (!this.CanLoadArtwork)
                    {
                        this.ClearArtwork();
                        return;
                    }

                    try
                    {
                        this.CoverArtViewModel = new CoverArtViewModel { CoverArt = artwork };
                    }
                    catch (Exception ex)
                    {
                        LogClient.Error("Could not show file artwork for Track {0}. Exception: {1}", track.Path, ex.Message);
                        this.ClearArtwork();
                    }

                    return;
                }
                else
                {
                    this.ClearArtwork();
                    return;
                }
            });
        }

        private bool CanLoadArtwork
        {
            get { return !this.appVisibilityService.IsBackgroundPlaybackMode; }
        }
    }
}
