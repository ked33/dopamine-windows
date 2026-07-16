using Prism.Mvvm;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public sealed class CollectionNeteaseRecommendationsViewModel : BindableBase
    {
        private int selectedTabIndex;

        public int SelectedTabIndex
        {
            get { return this.selectedTabIndex; }
            set { SetProperty<int>(ref this.selectedTabIndex, value); }
        }
    }
}
