using Dopamine.Services.Playback;
using Dopamine.Services.Shell;
using CommonServiceLocator;

namespace Dopamine.ViewModels.Common
{
    public class ProgressControlsThinViewModel : ProgressControlsViewModel
    {
        public ProgressControlsThinViewModel() : base(
            ServiceLocator.Current.GetInstance<IPlaybackService>(),
            ServiceLocator.Current.GetInstance<IAppVisibilityService>())
        {
        }
    }
}
