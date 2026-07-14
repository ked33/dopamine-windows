using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using Dopamine.Services.Scrobbling;
using CommonServiceLocator;
using Dopamine.ViewModels.FullPlayer.Settings;
using System;
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
            AppLog.InfoAlways("Settings Online view construction started.");

            try
            {
                InitializeComponent();
                AppLog.InfoAlways("Settings Online InitializeComponent completed.");

                this.scrobblingService = ServiceLocator.Current.GetInstance<IScrobblingService>();
                this.scrobblingService.SignInStateChanged += (_) => this.PasswordBox.Password = scrobblingService.Password;
                this.PasswordBox.Password = scrobblingService.Password;
                AppLog.InfoAlways("Settings Online view construction completed.");
            }
            catch (Exception ex)
            {
                AppLog.ErrorAlways("Settings Online view construction failed. Error={0}", LogClient.GetAllExceptions(ex));
                throw;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            scrobblingService.Password = this.PasswordBox.Password;
        }

        private async void SettingsOnline_Loaded(object sender, RoutedEventArgs e)
        {
            var viewModel = this.DataContext as SettingsOnlineViewModel;
            AppLog.InfoAlways("Settings Online Loaded event started. HasExpectedViewModel={0}", viewModel != null);

            try
            {
                if (viewModel != null)
                {
                    await viewModel.OnNeteaseLoadedAsync();
                }

                AppLog.InfoAlways("Settings Online Loaded event completed.");
            }
            catch (Exception ex)
            {
                AppLog.ErrorAlways("Settings Online Loaded event failed. Error={0}", LogClient.GetAllExceptions(ex));
                throw;
            }
        }

        private void SettingsOnline_Unloaded(object sender, RoutedEventArgs e)
        {
            AppLog.InfoAlways("Settings Online Unloaded event received.");
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
