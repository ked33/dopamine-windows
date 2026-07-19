using Dopamine.ViewModels.Common;
using System.Windows;
using System.Windows.Controls;

namespace Dopamine.Views.Common
{
    public partial class FileInformation : UserControl
    {
        public FileInformation()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = this.DataContext as FileInformationViewModel;

            if (viewModel != null)
            {
                await viewModel.LoadOnlineAudioInformationAsync();
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            var viewModel = this.DataContext as FileInformationViewModel;
            viewModel?.CancelOnlineAudioInformation();
        }
    }
}
