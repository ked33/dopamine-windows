using Digimezzo.Foundation.WPF.Controls;
using Dopamine.Core.Base;
using Dopamine.Core.Enums;
using Dopamine.Core.Prism;
using Prism.Events;
using Prism.Mvvm;

namespace Dopamine.ViewModels.FullPlayer.Collection
{
    public class CollectionViewModel : BindableBase
    {
        private int slideInFrom;

        public int SlideInFrom
        {
            get { return this.slideInFrom; }
            set { SetProperty<int>(ref this.slideInFrom, value); }
        }

        public CollectionViewModel(IEventAggregator eventAggregator)
        {
            eventAggregator.GetEvent<IsCollectionPageChanged>().Subscribe(tuple =>
            {
                this.SetSlideDirection(tuple.Item1);
            });
        }

        private void SetSlideDirection(SlideDirection direction)
        {
            this.SlideInFrom = direction == SlideDirection.RightToLeft ? Constants.SlideDistance : -Constants.SlideDistance;
        }
    }
}
