using Dopamine.Services.Online.Netease;
using Prism.Mvvm;
using System.Threading;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public sealed class CollectionNeteaseRecommendationsViewModel : BindableBase
    {
        private readonly INeteasePersonalFmService personalFmService;
        private readonly INeteaseSessionService sessionService;
        private int selectedTabIndex;

        public CollectionNeteaseRecommendationsViewModel(
            INeteasePersonalFmService personalFmService,
            INeteaseSessionService sessionService)
        {
            this.personalFmService = personalFmService;
            this.sessionService = sessionService;
        }

        public int SelectedTabIndex
        {
            get { return this.selectedTabIndex; }
            set
            {
                if (SetProperty<int>(ref this.selectedTabIndex, value) && value == 2)
                {
                    this.StartPersonalFmAsync();
                }
            }
        }

        private async void StartPersonalFmAsync()
        {
            if (this.sessionService.State == NeteaseSessionState.SignedIn &&
                !this.personalFmService.IsActive && !this.personalFmService.IsBusy)
            {
                await this.personalFmService.StartAsync(CancellationToken.None);
            }
        }
    }
}
