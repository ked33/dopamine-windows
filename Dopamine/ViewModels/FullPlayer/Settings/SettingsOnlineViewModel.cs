using Digimezzo.Foundation.Core.Helpers;
using Digimezzo.Foundation.Core.IO;
using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Base;
using Dopamine.Services.Dialog;
using Dopamine.Services.Provider;
using Dopamine.Services.Scrobbling;
using Dopamine.Services.I18n;
using Dopamine.Services.Online.Netease;
using Dopamine.Utils;
using Dopamine.Views.FullPlayer.Settings;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Security;
using System.Windows;
using System.Windows.Media;
using Prism.Ioc;

namespace Dopamine.ViewModels.FullPlayer.Settings
{
    public class SettingsOnlineViewModel : BindableBase
    {
        private IContainerProvider container;
        private IProviderService providerService;
        private IDialogService dialogService;
        private IEventAggregator eventAggregator;
        private ObservableCollection<SearchProvider> searchProviders;
        private SearchProvider selectedSearchProvider;
        private IScrobblingService scrobblingService;
        private bool isLastFmSignInInProgress;
        private bool checkBoxDownloadArtistInformationChecked;
        private bool checkBoxDownloadLyricsChecked;
        private bool checkBoxChartLyricsChecked;
        private bool checkBoxLoloLyricsChecked;
        private bool checkBoxMetroLyricsChecked;
        private bool checkBoxXiamiLyricsChecked;
        private bool checkBoxNeteaseLyricsChecked;
        private bool checkBoxEnableDiscordRichPresence;
        private ObservableCollection<NameValue> timeouts;
        private NameValue selectedTimeout;
        private INeteaseSessionService neteaseSessionService;
        private II18nService i18nService;
        private CancellationTokenSource neteaseQrCancellationTokenSource;
        private NeteaseQrSession activeNeteaseQrSession;
        private ImageSource neteaseQrCodeImage;
        private string neteaseStatusText;
        private bool isNeteaseSigningIn;
        private bool isNeteaseQrExpired;
        private bool isNeteasePageLoaded;
        private int selectedNeteaseLoginMethod;

        public DelegateCommand AddCommand { get; set; }
        public DelegateCommand EditCommand { get; set; }
        public DelegateCommand RemoveCommand { get; set; }
        public DelegateCommand LastfmSignInCommand { get; set; }
        public DelegateCommand LastfmSignOutCommand { get; set; }
        public DelegateCommand CreateLastFmAccountCommand { get; set; }
        public DelegateCommand RefreshNeteaseQrCommand { get; set; }
        public DelegateCommand NeteaseLogoutCommand { get; set; }

        public bool IsNeteaseSignedIn => this.neteaseSessionService.State == NeteaseSessionState.SignedIn;

        public string NeteaseAccountDisplay
        {
            get
            {
                if (this.neteaseSessionService.Account == null)
                {
                    return string.Empty;
                }

                return string.IsNullOrWhiteSpace(this.neteaseSessionService.Account.Nickname)
                    ? this.neteaseSessionService.Account.UserId
                    : this.neteaseSessionService.Account.Nickname;
            }
        }

        public ImageSource NeteaseQrCodeImage
        {
            get { return this.neteaseQrCodeImage; }
            set { SetProperty<ImageSource>(ref this.neteaseQrCodeImage, value); }
        }

        public string NeteaseStatusText
        {
            get { return this.neteaseStatusText; }
            set { SetProperty<string>(ref this.neteaseStatusText, value); }
        }

        public bool IsNeteaseSigningIn
        {
            get { return this.isNeteaseSigningIn; }
            set
            {
                SetProperty<bool>(ref this.isNeteaseSigningIn, value);
                this.RefreshNeteaseQrCommand?.RaiseCanExecuteChanged();
                this.NeteaseLogoutCommand?.RaiseCanExecuteChanged();
            }
        }

        public bool IsNeteaseQrExpired
        {
            get { return this.isNeteaseQrExpired; }
            set { SetProperty<bool>(ref this.isNeteaseQrExpired, value); }
        }

        public int SelectedNeteaseLoginMethod
        {
            get { return this.selectedNeteaseLoginMethod; }
            set
            {
                if (!SetProperty<int>(ref this.selectedNeteaseLoginMethod, value))
                {
                    return;
                }

                this.CancelNeteaseQrLogin();

                if (value == 0 && this.isNeteasePageLoaded && !this.IsNeteaseSignedIn)
                {
                    this.BeginNeteaseQrLoginAsync();
                }
            }
        }

        public ObservableCollection<NameValue> Timeouts
        {
            get { return this.timeouts; }
            set { SetProperty<ObservableCollection<NameValue>>(ref this.timeouts, value); }
        }

        public NameValue SelectedTimeout
        {
            get { return this.selectedTimeout; }
            set
            {
                if (value.Value != null)
                {
                    SettingsClient.Set<int>("Lyrics", "TimeoutSeconds", value.Value);
                }
                
                SetProperty<NameValue>(ref this.selectedTimeout, value);
            }
        }

        public bool CheckBoxChartLyricsChecked
        {
            get { return this.checkBoxChartLyricsChecked; }
            set
            {
                this.AddRemoveLyricsDownloadProvider("chartlyrics", value);
                SetProperty<bool>(ref this.checkBoxChartLyricsChecked, value);
            }
        }

        public bool CheckBoxLoloLyricsChecked
        {
            get { return this.checkBoxLoloLyricsChecked; }
            set
            {
                this.AddRemoveLyricsDownloadProvider("lololyrics", value);
                SetProperty<bool>(ref this.checkBoxLoloLyricsChecked, value);
            }
        }

        public bool CheckBoxMetroLyricsChecked
        {
            get { return this.checkBoxMetroLyricsChecked; }
            set
            {
                this.AddRemoveLyricsDownloadProvider("metrolyrics", value);
                SetProperty<bool>(ref this.checkBoxMetroLyricsChecked, value);
            }
        }

        public bool CheckBoxXiamiLyricsChecked
        {
            get { return this.checkBoxXiamiLyricsChecked; }
            set
            {
                this.AddRemoveLyricsDownloadProvider("xiamilyrics", value);
                SetProperty<bool>(ref this.checkBoxXiamiLyricsChecked, value);
            }
        }

        public bool CheckBoxNeteaseLyricsChecked
        {
            get { return this.checkBoxNeteaseLyricsChecked; }
            set
            {
                this.AddRemoveLyricsDownloadProvider("neteaselyrics", value);
                SetProperty<bool>(ref this.checkBoxNeteaseLyricsChecked, value);
            }
        }

        public ObservableCollection<SearchProvider> SearchProviders
        {
            get { return this.searchProviders; }
            set
            {
                SetProperty<ObservableCollection<SearchProvider>>(ref this.searchProviders, value);
                this.SelectedSearchProvider = null;
            }
        }

        public SearchProvider SelectedSearchProvider
        {
            get { return this.selectedSearchProvider; }
            set
            {
                SetProperty<SearchProvider>(ref this.selectedSearchProvider, value);
                this.EditCommand.RaiseCanExecuteChanged();
                this.RemoveCommand.RaiseCanExecuteChanged();
            }
        }

        public bool CheckBoxDownloadArtistInformationChecked
        {
            get { return this.checkBoxDownloadArtistInformationChecked; }
            set
            {
                SettingsClient.Set<bool>("Lastfm", "DownloadArtistInformation", value);
                SetProperty<bool>(ref this.checkBoxDownloadArtistInformationChecked, value);
            }
        }

        public bool CheckBoxEnableDiscordRichPresence
        {
            get { return this.checkBoxEnableDiscordRichPresence; }
            set
            {
                SettingsClient.Set<bool>("Discord", "EnableDiscordRichPresence", value, true);
                SetProperty<bool>(ref this.checkBoxEnableDiscordRichPresence, value);
            }
        }

        public bool CheckBoxDownloadLyricsChecked
        {
            get { return this.checkBoxDownloadLyricsChecked; }
            set
            {
                SettingsClient.Set<bool>("Lyrics", "DownloadLyrics", value, true);
                SetProperty<bool>(ref this.checkBoxDownloadLyricsChecked, value);
            }
        }

        public bool IsLastFmSignedIn
        {
            get { return this.scrobblingService.SignInState == SignInState.SignedIn; }
        }

        public string LastFmUsername
        {
            get { return this.scrobblingService.Username; }
            set
            {
                this.scrobblingService.Username = value;
            }
        }

        public bool IsLastFmSigningIn
        {
            get { return this.isLastFmSignInInProgress; }
            set
            {
                SetProperty<bool>(ref this.isLastFmSignInInProgress, value);
            }
        }

        public bool IsLastFmSignInError
        {
            get { return this.scrobblingService.SignInState == SignInState.Error; }
        }

        public SettingsOnlineViewModel(IContainerProvider container, IProviderService providerService, IDialogService dialogService,
            IScrobblingService scrobblingService, IEventAggregator eventAggregator, INeteaseSessionService neteaseSessionService,
            II18nService i18nService)
        {
            AppLog.InfoAlways(
                "Settings Online ViewModel construction started. HasContainer={0}, HasProviderService={1}, HasDialogService={2}, HasScrobblingService={3}, HasEventAggregator={4}, HasNeteaseSessionService={5}, HasI18nService={6}",
                container != null,
                providerService != null,
                dialogService != null,
                scrobblingService != null,
                eventAggregator != null,
                neteaseSessionService != null,
                i18nService != null);

            this.container = container;
            this.providerService = providerService;
            this.dialogService = dialogService;
            this.scrobblingService = scrobblingService;
            this.eventAggregator = eventAggregator;
            this.neteaseSessionService = neteaseSessionService;
            this.i18nService = i18nService;

            this.RefreshNeteaseQrCommand = new DelegateCommand(
                () => this.BeginNeteaseQrLoginAsync(),
                () => !this.IsNeteaseSigningIn);
            this.NeteaseLogoutCommand = new DelegateCommand(
                () => this.LogoutNeteaseAsync(),
                () => !this.IsNeteaseSigningIn);

            this.neteaseSessionService.SessionChanged += (_, __) => this.DispatchNeteaseStateUpdate();
            this.i18nService.LanguageChanged += (_, __) => this.DispatchNeteaseStateUpdate();
            this.UpdateNeteaseState();

            this.scrobblingService.SignInStateChanged += (_) =>
            {
                this.IsLastFmSigningIn = false;
                RaisePropertyChanged(nameof(this.IsLastFmSignedIn));
                RaisePropertyChanged(nameof(this.LastFmUsername));
                RaisePropertyChanged(nameof(this.IsLastFmSignInError));
            };

            this.AddCommand = new DelegateCommand(() => this.AddSearchProvider());
            this.EditCommand = new DelegateCommand(() => { this.EditSearchProvider(); }, () => { return this.SelectedSearchProvider != null; });
            this.RemoveCommand = new DelegateCommand(() => { this.RemoveSearchProvider(); }, () => { return this.SelectedSearchProvider != null; });
            this.LastfmSignInCommand = new DelegateCommand(async () =>
            {
                this.IsLastFmSigningIn = true;
                await this.scrobblingService.SignIn();

            });
            this.LastfmSignOutCommand = new DelegateCommand(() => this.scrobblingService.SignOut());
            this.CreateLastFmAccountCommand = new DelegateCommand(() =>
            {
                try
                {
                    Actions.TryOpenLink(Constants.LastFmJoinLink);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Could not open the Last.fm web page. Exception: {0}", ex.Message);
                }
            });

            this.GetSearchProvidersAsync();

            this.providerService.SearchProvidersChanged += (_, __) => this.GetSearchProvidersAsync();

            this.GetCheckBoxesAsync();
            this.GetTimeoutsAsync();
            AppLog.InfoAlways("Settings Online ViewModel construction completed.");
        }

        public Task OnNeteaseLoadedAsync()
        {
            AppLog.InfoAlways(
                "Settings Online Netease load started. IsSignedIn={0}, LoginMethod={1}, HasActiveQrSession={2}",
                this.IsNeteaseSignedIn,
                this.SelectedNeteaseLoginMethod,
                this.activeNeteaseQrSession != null);
            this.isNeteasePageLoaded = true;
            this.UpdateNeteaseState();

            if (!this.IsNeteaseSignedIn && this.SelectedNeteaseLoginMethod == 0 && this.activeNeteaseQrSession == null)
            {
                this.BeginNeteaseQrLoginAsync();
            }

            AppLog.InfoAlways("Settings Online Netease load completed.");
            return Task.CompletedTask;
        }

        public void OnNeteaseUnloaded()
        {
            AppLog.InfoAlways("Settings Online Netease unload started.");
            this.isNeteasePageLoaded = false;
            this.CancelNeteaseQrLogin();
            AppLog.InfoAlways("Settings Online Netease unload completed.");
        }

        public async Task LoginWithNeteaseCookieAsync(SecureString cookie)
        {
            if (cookie == null || cookie.Length == 0 || this.IsNeteaseSigningIn)
            {
                return;
            }

            this.CancelNeteaseQrLogin();
            this.IsNeteaseSigningIn = true;
            this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Validating_Login");

            try
            {
                NeteaseLoginResult result = await this.neteaseSessionService.LoginWithCookieAsync(cookie, CancellationToken.None);

                if (!result.IsSuccess)
                {
                    this.NeteaseStatusText = this.GetNeteaseErrorText(result.Error);
                }
            }
            finally
            {
                this.IsNeteaseSigningIn = false;
                this.UpdateNeteaseState(false);
            }
        }

        private async void BeginNeteaseQrLoginAsync()
        {
            AppLog.InfoAlways(
                "Netease QR login request evaluated. IsSigningIn={0}, IsSignedIn={1}, IsPageLoaded={2}",
                this.IsNeteaseSigningIn,
                this.IsNeteaseSignedIn,
                this.isNeteasePageLoaded);

            if (this.IsNeteaseSigningIn || this.IsNeteaseSignedIn || !this.isNeteasePageLoaded)
            {
                AppLog.InfoAlways("Netease QR login request skipped because the page state does not allow a new request.");
                return;
            }

            this.CancelNeteaseQrLogin();
            this.IsNeteaseSigningIn = true;
            this.IsNeteaseQrExpired = false;
            this.NeteaseQrCodeImage = null;
            this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Creating_Qr_Code");
            var cancellationTokenSource = new CancellationTokenSource();
            Interlocked.Exchange(ref this.neteaseQrCancellationTokenSource, cancellationTokenSource);
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            try
            {
                NeteaseResult<NeteaseQrSession> result = await this.neteaseSessionService.BeginQrLoginAsync(cancellationToken);
                AppLog.InfoAlways(
                    "Netease QR session request completed. IsSuccess={0}, ErrorCode={1}",
                    result.IsSuccess,
                    result.Error?.Code.ToString() ?? "None");

                if (!result.IsSuccess || cancellationToken.IsCancellationRequested)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        this.NeteaseStatusText = this.GetNeteaseErrorText(result.Error);
                    }

                    return;
                }

                this.activeNeteaseQrSession = result.Value;
                this.NeteaseQrCodeImage = QrCodeImageFactory.Create(
                    "https://music.163.com/login?codekey=" + Uri.EscapeDataString(result.Value.Unikey));
                AppLog.InfoAlways("Netease QR image was created successfully.");
                this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Qr_Waiting_For_Scan");
                this.IsNeteaseSigningIn = false;
                await this.PollNeteaseQrLoginAsync(result.Value, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not run Netease QR sign-in. ErrorType={0}", ex.GetType().Name);
                this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
            }
            finally
            {
                this.IsNeteaseSigningIn = false;
                Interlocked.CompareExchange(ref this.neteaseQrCancellationTokenSource, null, cancellationTokenSource);
                cancellationTokenSource.Dispose();
            }
        }

        private async Task PollNeteaseQrLoginAsync(NeteaseQrSession session, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && this.isNeteasePageLoaded &&
                this.SelectedNeteaseLoginMethod == 0 && !this.IsNeteaseSignedIn)
            {
                NeteaseQrPollResult result = await this.neteaseSessionService.PollQrLoginAsync(session, cancellationToken);

                switch (result.State)
                {
                    case NeteaseQrState.WaitingForScan:
                        this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Qr_Waiting_For_Scan");
                        break;
                    case NeteaseQrState.WaitingForConfirm:
                        this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Qr_Waiting_For_Confirm");
                        break;
                    case NeteaseQrState.Authorized:
                        this.activeNeteaseQrSession = null;
                        this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Signed_In");
                        this.UpdateNeteaseState();
                        return;
                    case NeteaseQrState.Expired:
                        this.IsNeteaseQrExpired = true;
                        this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Qr_Expired");
                        return;
                    case NeteaseQrState.Cancelled:
                        return;
                    default:
                        this.NeteaseStatusText = this.GetNeteaseErrorText(result.Error);
                        return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        private async void LogoutNeteaseAsync()
        {
            if (!this.dialogService.ShowConfirmation(
                0xe11b,
                16,
                ResourceUtils.GetString("Language_Netease_Music"),
                ResourceUtils.GetString("Language_Netease_Confirm_Logout"),
                ResourceUtils.GetString("Language_Yes"),
                ResourceUtils.GetString("Language_No")))
            {
                return;
            }

            this.IsNeteaseSigningIn = true;
            bool shouldRestartQr = false;

            try
            {
                this.CancelNeteaseQrLogin();
                await this.neteaseSessionService.LogoutAsync();
                this.NeteaseQrCodeImage = null;
                this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Signed_Out");

                shouldRestartQr = this.isNeteasePageLoaded && this.SelectedNeteaseLoginMethod == 0;
            }
            finally
            {
                this.IsNeteaseSigningIn = false;
                this.UpdateNeteaseState(false);
            }

            if (shouldRestartQr)
            {
                this.BeginNeteaseQrLoginAsync();
            }
        }

        private void CancelNeteaseQrLogin()
        {
            NeteaseQrSession session = this.activeNeteaseQrSession;
            this.activeNeteaseQrSession = null;

            CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(
                ref this.neteaseQrCancellationTokenSource,
                null);
            cancellationTokenSource?.Cancel();

            if (session != null)
            {
                this.neteaseSessionService.CancelSignIn(session.LoginGeneration);
            }
        }

        private void DispatchNeteaseStateUpdate()
        {
            if (Application.Current == null || Application.Current.Dispatcher.CheckAccess())
            {
                this.UpdateNeteaseState();
                return;
            }

            Application.Current.Dispatcher.Invoke(new Action(this.UpdateNeteaseState));
        }

        private void UpdateNeteaseState()
        {
            this.UpdateNeteaseState(true);
        }

        private void UpdateNeteaseState(bool updateStatusText)
        {
            RaisePropertyChanged(nameof(this.IsNeteaseSignedIn));
            RaisePropertyChanged(nameof(this.NeteaseAccountDisplay));

            if (!updateStatusText)
            {
                return;
            }

            switch (this.neteaseSessionService.State)
            {
                case NeteaseSessionState.SignedIn:
                    this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Signed_In");
                    break;
                case NeteaseSessionState.Restoring:
                    this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Restoring_Session");
                    break;
                case NeteaseSessionState.OfflineUnknown:
                    this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Network_Error");
                    break;
                case NeteaseSessionState.Expired:
                    this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Login_Expired");
                    break;
                case NeteaseSessionState.Error:
                    this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Service_Unavailable");
                    break;
                default:
                    if (string.IsNullOrWhiteSpace(this.NeteaseStatusText))
                    {
                        this.NeteaseStatusText = ResourceUtils.GetString("Language_Netease_Not_Signed_In");
                    }
                    break;
            }
        }

        private string GetNeteaseErrorText(NeteaseError error)
        {
            return ResourceUtils.GetString(error?.MessageKey ?? "Language_Netease_Service_Unavailable");
        }

        private async void GetSearchProvidersAsync()
        {
            var providersList = await this.providerService.GetSearchProvidersAsync();
            var localProviders = new ObservableCollection<SearchProvider>();

            foreach (SearchProvider provider in providersList)
            {
                localProviders.Add(provider);
            }

            this.SearchProviders = localProviders;
        }

        private void AddSearchProvider()
        {
            SettingsOnlineAddEditSearchProvider view = this.container.Resolve<SettingsOnlineAddEditSearchProvider>();
            view.DataContext = this.container.Resolve<Func<SearchProvider, SettingsOnlineAddEditSearchProviderViewModel>>()(new SearchProvider());

            string dialogTitle = ResourceUtils.GetString("Language_Add");

            this.dialogService.ShowCustomDialog(
                0xe104,
                14,
                dialogTitle,
                view,
                450,
                0,
                false,
                true,
                true,
                true,
                ResourceUtils.GetString("Language_Ok"),
                ResourceUtils.GetString("Language_Cancel"),
                ((SettingsOnlineAddEditSearchProviderViewModel)view.DataContext).AddSearchProviderAsync);
        }

        private void EditSearchProvider()
        {
            SettingsOnlineAddEditSearchProvider view = this.container.Resolve<SettingsOnlineAddEditSearchProvider>();
            view.DataContext = this.container.Resolve<Func<SearchProvider, SettingsOnlineAddEditSearchProviderViewModel>>()(this.selectedSearchProvider);

            string dialogTitle = ResourceUtils.GetString("Language_Edit");

            this.dialogService.ShowCustomDialog(
                0xe104,
                14,
                dialogTitle,
                view,
                450,
                0,
                false,
                true,
                true,
                true,
                ResourceUtils.GetString("Language_Ok"),
                ResourceUtils.GetString("Language_Cancel"),
                ((SettingsOnlineAddEditSearchProviderViewModel)view.DataContext).UpdateSearchProviderAsync);
        }

        private void RemoveSearchProvider()
        {
            if (this.dialogService.ShowConfirmation(0xe11b, 16, ResourceUtils.GetString("Language_Remove"), ResourceUtils.GetString("Language_Confirm_Remove_Online_Search_Provider").Replace("{provider}", this.selectedSearchProvider.Name), ResourceUtils.GetString("Language_Yes"), ResourceUtils.GetString("Language_No")))
            {
                var isRemoveSuccess = this.providerService.RemoveSearchProvider(this.selectedSearchProvider);

                if (!isRemoveSuccess)
                {
                    this.dialogService.ShowNotification(
                        0xe711,
                        16,
                        ResourceUtils.GetString("Language_Error"),
                        ResourceUtils.GetString("Language_Error_Removing_Online_Search_Provider").Replace("{provider}", this.selectedSearchProvider.Name),
                        ResourceUtils.GetString("Language_Ok"),
                        true,
                        ResourceUtils.GetString("Language_Log_File"));
                }
            }
        }

        private async void GetCheckBoxesAsync()
        {
            await Task.Run(() =>
            {
                this.checkBoxDownloadArtistInformationChecked = SettingsClient.Get<bool>("Lastfm", "DownloadArtistInformation");
                this.checkBoxEnableDiscordRichPresence = SettingsClient.Get<bool>("Discord", "EnableDiscordRichPresence");
                this.checkBoxDownloadLyricsChecked = SettingsClient.Get<bool>("Lyrics", "DownloadLyrics");

                string lyricsProviders = SettingsClient.Get<string>("Lyrics", "Providers");

                this.checkBoxChartLyricsChecked = lyricsProviders.ToLower().Contains("chartlyrics");
                this.checkBoxLoloLyricsChecked = lyricsProviders.ToLower().Contains("lololyrics");
                this.checkBoxMetroLyricsChecked = lyricsProviders.ToLower().Contains("metrolyrics");
                this.checkBoxXiamiLyricsChecked = lyricsProviders.ToLower().Contains("xiamilyrics");
                this.checkBoxNeteaseLyricsChecked = lyricsProviders.ToLower().Contains("neteaselyrics");
            });
        }

        private void AddRemoveLyricsDownloadProvider(string provider, bool add)
        {
            try
            {
                string lyricsProviders = SettingsClient.Get<string>("Lyrics", "Providers");
                var lyricsProvidersList = new List<string>(lyricsProviders.ToLower().Split(';'));

                if (add)
                {
                    if (!lyricsProvidersList.Contains(provider)) lyricsProvidersList.Add(provider);
                }
                else
                {
                    if (lyricsProvidersList.Contains(provider)) lyricsProvidersList.Remove(provider);
                }

                string[] arr = lyricsProvidersList.ToArray();
                SettingsClient.Set<string>("Lyrics", "Providers", string.Join(";", arr));
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not add/remove lyrics download providers. Add = '{0}'. Exception: {1}", add.ToString(), ex.Message);
            }
        }

        private async void GetTimeoutsAsync()
        {
            var localTimeouts = new ObservableCollection<NameValue>();

            await Task.Run(() =>
            {
                localTimeouts.Add(new NameValue { Name = ResourceUtils.GetString("0"), Value = 0 });
                localTimeouts.Add(new NameValue { Name = "1", Value = 1 });
                localTimeouts.Add(new NameValue { Name = "2", Value = 2 });
                localTimeouts.Add(new NameValue { Name = "5", Value = 5 });
                localTimeouts.Add(new NameValue { Name = "10", Value = 10 });
                localTimeouts.Add(new NameValue { Name = "20", Value = 20 });
                localTimeouts.Add(new NameValue { Name = "30", Value = 30 });
                localTimeouts.Add(new NameValue { Name = "40", Value = 40 });
                localTimeouts.Add(new NameValue { Name = "50", Value = 50 });
                localTimeouts.Add(new NameValue { Name = "60", Value = 60 });
            });

            this.Timeouts = localTimeouts;

            NameValue localSelectedTimeout = null;
            await Task.Run(() => localSelectedTimeout = this.Timeouts.Where((svp) => svp.Value == SettingsClient.Get<int>("Lyrics", "TimeoutSeconds")).Select((svp) => svp).First());

            this.selectedTimeout = null;
            RaisePropertyChanged(nameof(this.SelectedTimeout));
            this.selectedTimeout = localSelectedTimeout;
            RaisePropertyChanged(nameof(this.SelectedTimeout));
        }
    }
}
