using Dopamine.Services.Entities;
using Dopamine.ViewModels.FullPlayer.Collection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dopamine.Views.FullPlayer.Collection
{
    public partial class CollectionIntelligenceRecommendations : UserControl
    {
        public CollectionIntelligenceRecommendations()
        {
            InitializeComponent();
        }

        private async void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = sender as DataGridRow;
            await this.PlayTrackAsync(row?.Item as TrackViewModel);
        }

        private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = sender as DataGridRow;

            if (row != null)
            {
                row.IsSelected = true;
                this.DataGridRecommendations.SelectedItem = row.Item;
            }
        }

        private async void IntelligenceRecommendationsContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var viewModel = this.DataContext as CollectionIntelligenceRecommendationsViewModel;

            if (viewModel != null)
            {
                await viewModel.PrepareContextMenuAsync();
            }
        }

        private async void DataGridRecommendations_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await this.PlayTrackAsync(this.DataGridRecommendations.SelectedItem as TrackViewModel);
            }
        }

        private void DataGridRecommendations_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.Control)
            {
                this.JumpToPlayingTrack();
            }
        }

        private void JumpToPlayingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.JumpToPlayingTrack();
        }

        private void JumpToPlayingTrack()
        {
            TrackViewModel playing = this.DataGridRecommendations.Items
                .OfType<TrackViewModel>()
                .FirstOrDefault(x => x.IsPlaying);

            if (playing != null)
            {
                this.DataGridRecommendations.SelectedItem = playing;
                this.DataGridRecommendations.ScrollIntoView(playing);
            }
        }

        private Task PlayTrackAsync(TrackViewModel track)
        {
            var viewModel = this.DataContext as CollectionIntelligenceRecommendationsViewModel;
            return viewModel == null ? Task.CompletedTask : viewModel.PlayFromAsync(track);
        }
    }
}
