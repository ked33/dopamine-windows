using Digimezzo.Foundation.Core.IO;
using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Base;
using Dopamine.Core.IO;
using Dopamine.Services.Playback;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Threading.Tasks;

namespace Dopamine.ViewModels.FullPlayer.Settings
{
    public class SettingsAppearanceViewModel : BindableBase
    {
        private IPlaybackService playbackService;
        private bool checkBoxCheckBoxShowWindowBorderChecked;
        private bool checkBoxEnableTransparencyChecked;
        private bool checkBoxEnableAnimationsChecked;
        private IEventAggregator eventAggregator;

        public DelegateCommand<string> OpenColorSchemesDirectoryCommand { get; set; }

        public string ColorSchemesDirectory { get; set; }

        public bool IsWindows10 => Constants.IsWindows10;

        public bool CheckBoxCheckBoxShowWindowBorderChecked
        {
            get { return this.checkBoxCheckBoxShowWindowBorderChecked; }
            set
            {
                SettingsClient.Set<bool>("Appearance", "ShowWindowBorder", value, true);
                SetProperty<bool>(ref this.checkBoxCheckBoxShowWindowBorderChecked, value);
            }
        }

        public bool CheckBoxEnableTransparencyChecked
        {
            get { return this.checkBoxEnableTransparencyChecked; }
            set
            {
                SettingsClient.Set<bool>("Appearance", "EnableTransparency", value);
                SetProperty<bool>(ref this.checkBoxEnableTransparencyChecked, value);
            }
        }

        public bool CheckBoxEnableAnimationsChecked
        {
            get { return this.checkBoxEnableAnimationsChecked; }
            set
            {
                SettingsClient.Set<bool>("Appearance", "EnableAnimations", value, true);
                SetProperty<bool>(ref this.checkBoxEnableAnimationsChecked, value);
            }
        }

        public SettingsAppearanceViewModel(IPlaybackService playbackService, IEventAggregator eventAggregator)
        {
            this.playbackService = playbackService;
            this.eventAggregator = eventAggregator;

            this.ColorSchemesDirectory = System.IO.Path.Combine(SettingsClient.ApplicationFolder(), ApplicationPaths.ColorSchemesFolder);

            this.OpenColorSchemesDirectoryCommand = new DelegateCommand<string>((colorSchemesDirectory) =>
            {
                try
                {
                    Actions.TryOpenPath(colorSchemesDirectory);
                }
                catch (Exception ex)
                {
                    LogClient.Error("Could not open the ColorSchemes directory. Exception: {0}", ex.Message);
                }
            });

            this.GetCheckBoxesAsync();
        }

        public async void GetCheckBoxesAsync()
        {
            bool showWindowBorder = false;
            bool enableTransparency = false;
            bool enableAnimations = true;

            await Task.Run(() =>
            {
                showWindowBorder = SettingsClient.Get<bool>("Appearance", "ShowWindowBorder");
                enableTransparency = SettingsClient.Get<bool>("Appearance", "EnableTransparency");
                enableAnimations = SettingsClient.Get<bool>("Appearance", "EnableAnimations");
            });

            SetProperty<bool>(ref this.checkBoxCheckBoxShowWindowBorderChecked, showWindowBorder);
            SetProperty<bool>(ref this.checkBoxEnableTransparencyChecked, enableTransparency);
            SetProperty<bool>(ref this.checkBoxEnableAnimationsChecked, enableAnimations);
        }
    }
}
