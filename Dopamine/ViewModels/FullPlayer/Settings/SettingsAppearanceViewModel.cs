using Digimezzo.Foundation.Core.IO;
using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Base;
using Dopamine.Core.IO;
using Dopamine.Core.Logging;
using Dopamine.Core.Settings;
using Dopamine.Services.Dialog;
using Dopamine.Services.Playback;
using Dopamine.Views.FullPlayer.Settings;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Mvvm;
using System;
using System.Threading.Tasks;

namespace Dopamine.ViewModels.FullPlayer.Settings
{
    public class SettingsAppearanceViewModel : BindableBase
    {
        private IPlaybackService playbackService;
        private IDialogService dialogService;
        private IContainerProvider container;
        private bool checkBoxCheckBoxShowWindowBorderChecked;
        private bool checkBoxEnableTransparencyChecked;
        private bool checkBoxEnableAnimationsChecked;
        private bool checkBoxEnableLoggingChecked;
        private IEventAggregator eventAggregator;

        public DelegateCommand<string> OpenColorSchemesDirectoryCommand { get; set; }
        public DelegateCommand OpenThemeColorsEditorCommand { get; set; }

        public string ColorSchemesDirectory { get; set; }

        public bool IsWindows10 => Constants.IsWindows10;

        public bool CheckBoxCheckBoxShowWindowBorderChecked
        {
            get { return this.checkBoxCheckBoxShowWindowBorderChecked; }
            set
            {
                SettingDefaults.SetSafe<bool>("Appearance", "ShowWindowBorder", value, true);
                SetProperty<bool>(ref this.checkBoxCheckBoxShowWindowBorderChecked, value);
            }
        }

        public bool CheckBoxEnableTransparencyChecked
        {
            get { return this.checkBoxEnableTransparencyChecked; }
            set
            {
                SettingDefaults.SetSafe<bool>("Appearance", "EnableTransparency", value);
                SetProperty<bool>(ref this.checkBoxEnableTransparencyChecked, value);
            }
        }

        public bool CheckBoxEnableAnimationsChecked
        {
            get { return this.checkBoxEnableAnimationsChecked; }
            set
            {
                UiAnimationSettings.SetAnimationsEnabled(value);
                SetProperty<bool>(ref this.checkBoxEnableAnimationsChecked, value);
            }
        }

        public bool CheckBoxEnableLoggingChecked
        {
            get { return this.checkBoxEnableLoggingChecked; }
            set
            {
                LoggingSettings.SetEnabled(value);
                SetProperty<bool>(ref this.checkBoxEnableLoggingChecked, value);
            }
        }

        public SettingsAppearanceViewModel(
            IPlaybackService playbackService,
            IEventAggregator eventAggregator,
            IDialogService dialogService,
            IContainerProvider container)
        {
            this.playbackService = playbackService;
            this.eventAggregator = eventAggregator;
            this.dialogService = dialogService;
            this.container = container;

            this.ColorSchemesDirectory = System.IO.Path.Combine(SettingsClient.ApplicationFolder(), ApplicationPaths.ColorSchemesFolder);

            this.OpenColorSchemesDirectoryCommand = new DelegateCommand<string>((colorSchemesDirectory) =>
            {
                try
                {
                    Actions.TryOpenPath(colorSchemesDirectory);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Could not open the ColorSchemes directory. Exception: {0}", ex.Message);
                }
            });

            this.OpenThemeColorsEditorCommand = new DelegateCommand(this.OpenThemeColorsEditor);

            this.GetCheckBoxesAsync();
        }

        private void OpenThemeColorsEditor()
        {
            var view = this.container.Resolve<ThemeColorsEditor>();
            var viewModel = this.container.Resolve<ThemeColorsEditorViewModel>();
            view.DataContext = viewModel;

            this.dialogService.ShowCustomDialog(
                0xe790,
                16,
                ResourceUtils.GetString("Language_Theme_Colors"),
                view,
                500,
                0,
                false,
                true,
                true,
                false,
                ResourceUtils.GetString("Language_Ok"),
                ResourceUtils.GetString("Language_Cancel"),
                viewModel.CloseAsync);
        }

        public async void GetCheckBoxesAsync()
        {
            bool showWindowBorder = false;
            bool enableTransparency = true;
            bool enableAnimations = true;
            bool enableLogging = true;

            try
            {
                await Task.Run(() =>
                {
                    showWindowBorder = SettingDefaults.GetOrAdd<bool>("Appearance", "ShowWindowBorder", false, true);
                    enableTransparency = SettingDefaults.GetOrAdd<bool>("Appearance", "EnableTransparency", true);
                    enableAnimations = SettingDefaults.GetOrAdd<bool>("Appearance", "EnableAnimations", true, true);
                    enableLogging = LoggingSettings.IsEnabled();
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("Appearance settings initialization failed. Exception: {0}", ex.Message);
            }

            SetProperty<bool>(ref this.checkBoxCheckBoxShowWindowBorderChecked, showWindowBorder);
            SetProperty<bool>(ref this.checkBoxEnableTransparencyChecked, enableTransparency);
            SetProperty<bool>(ref this.checkBoxEnableAnimationsChecked, enableAnimations);
            SetProperty<bool>(ref this.checkBoxEnableLoggingChecked, enableLogging);
            UiAnimationSettings.SyncWithSettingValue(enableAnimations);
        }
    }
}
