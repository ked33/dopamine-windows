using Dopamine.Services.Metadata;
using Dopamine.Services.Playback;
using Dopamine.Services.Scrobbling;
using Dopamine.Services.Shell;

namespace Dopamine.ViewModels.Common
{
    public class PlaybackInfoControlNanoViewModel : PlaybackInfoControlViewModel
    {
        public PlaybackInfoControlNanoViewModel(
            IPlaybackService playbackService, 
            IMetadataService metadataService,
            IScrobblingService scrobblingService,
            IAppVisibilityService appVisibilityService) : base(
            playbackService, 
            metadataService,
            scrobblingService,
            appVisibilityService
            )
        {
        }
    }
}
