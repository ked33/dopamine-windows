using System;
using System.Windows;
using System.Windows.Controls;
using Dopamine.Core.Base;
using Dopamine.ViewModels.Common;

namespace Dopamine.Views.Common
{
    public partial class CoverArtControl : UserControl
    {
        public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register("IconSize", typeof(double), typeof(CoverArtControl), new PropertyMetadata(null));
        private int requestedArtworkSize = 0;

        public new object DataContext
        {
            get { return base.DataContext; }
            set { base.DataContext = value; }
        }

        public double IconSize
        {
            get { return Convert.ToDouble(GetValue(IconSizeProperty)); }

            set { SetValue(IconSizeProperty, value); }
        }
     
        public CoverArtControl()
        {
            InitializeComponent();
            this.DataContextChanged += (_, __) => this.UpdateRequestedArtworkSize();
        }
    
        private void ThisControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.IconSize = Convert.ToDouble(Convert.ToInt32(this.ActualWidth / 2)); // We want this to be a rounded value
            this.UpdateRequestedArtworkSize();
        }
  
        private void ThisControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.IconSize = Convert.ToDouble(Convert.ToInt32(this.ActualWidth / 2)); // We want this to be a rounded value
            this.UpdateRequestedArtworkSize();
        }

        private void UpdateRequestedArtworkSize()
        {
            double scaledSize = Math.Max(this.ActualWidth, this.ActualHeight) * Constants.CoverUpscaleFactor;
            int normalizedSize = Constants.GetArtworkSizeBucket(scaledSize);

            if (this.requestedArtworkSize == normalizedSize)
            {
                return;
            }

            this.requestedArtworkSize = normalizedSize;

            CoverArtControlViewModel viewModel = this.DataContext as CoverArtControlViewModel;

            if (viewModel != null)
            {
                viewModel.RequestedArtworkSize = normalizedSize;
            }
        }
    }
}
