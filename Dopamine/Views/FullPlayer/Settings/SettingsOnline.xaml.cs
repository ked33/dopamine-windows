using Dopamine.Services.Scrobbling;
using CommonServiceLocator;
using Dopamine.ViewModels.FullPlayer.Settings;
using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace Dopamine.Views.FullPlayer.Settings
{
    public partial class SettingsOnline : UserControl
    {
        private IScrobblingService scrobblingService;

        public SettingsOnline()
        {
            InitializeComponent();

            this.scrobblingService = ServiceLocator.Current.GetInstance<IScrobblingService>();
            this.scrobblingService.SignInStateChanged += (_) => this.PasswordBox.Password = scrobblingService.Password;
            this.PasswordBox.Password = scrobblingService.Password;
        }

        private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            scrobblingService.Password = this.PasswordBox.Password;
        }

        private async void SettingsOnline_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = this.DataContext as SettingsOnlineViewModel;

            if (viewModel != null)
            {
                await viewModel.OnNeteaseLoadedAsync();
            }
        }

        private void SettingsOnline_Unloaded(object sender, RoutedEventArgs e)
        {
            (this.DataContext as SettingsOnlineViewModel)?.OnNeteaseUnloaded();
        }

        private async void NeteaseCookieLogin_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = this.DataContext as SettingsOnlineViewModel;

            if (viewModel == null)
            {
                return;
            }

            using (SecureString cookie = this.NeteaseCookiePasswordBox.SecurePassword.Copy())
            {
                this.NeteaseCookiePasswordBox.Clear();
                await viewModel.LoginWithNeteaseCookieAsync(cookie);
            }
        }
    }
}
