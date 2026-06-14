using System;
using System.Windows;
using System.Windows.Controls;
using Dopamine.Core.Base;
using Dopamine.ViewModels.Common;

namespace Dopamine.Views.Common
{
    public partial class BackgroundCoverArtControl : UserControl
    {
        private int requestedArtworkSize = 0;

        public BackgroundCoverArtControl()
        {
            InitializeComponent();
            this.DataContextChanged += (_, __) => this.UpdateRequestedArtworkSize();
        }
   
        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.ActualWidth == 0 | this.ActualHeight == 0) return;

            if (this.ActualWidth > this.ActualHeight)
            {
                this.CoverImage.Width = this.ActualWidth;
                this.CoverImage.Height = this.ActualWidth;
            }
            else
            {
                this.CoverImage.Width = this.ActualHeight;
                this.CoverImage.Height = this.ActualHeight;
            }

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
