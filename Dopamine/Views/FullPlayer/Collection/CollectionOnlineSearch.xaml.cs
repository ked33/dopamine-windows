using Dopamine.Services.Entities;
using Dopamine.ViewModels.FullPlayer.Collection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dopamine.Views.FullPlayer.Collection
{
    public partial class CollectionOnlineSearch : UserControl
    {
        public CollectionOnlineSearch()
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
                this.DataGridOnlineSearch.SelectedItem = row.Item;
            }
        }

        private async void DataGridOnlineSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await this.PlayTrackAsync(this.DataGridOnlineSearch.SelectedItem as TrackViewModel);
            }
        }

        private void DataGridOnlineSearch_KeyUp(object sender, KeyEventArgs e)
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
            TrackViewModel playing = this.DataGridOnlineSearch.Items
                .OfType<TrackViewModel>()
                .FirstOrDefault(x => x.IsPlaying);

            if (playing != null)
            {
                this.DataGridOnlineSearch.SelectedItem = playing;
                this.DataGridOnlineSearch.ScrollIntoView(playing);
            }
        }

        private Task PlayTrackAsync(TrackViewModel track)
        {
            var viewModel = this.DataContext as CollectionOnlineSearchViewModel;
            return viewModel == null ? Task.CompletedTask : viewModel.PlayFromAsync(track);
        }
    }
}
