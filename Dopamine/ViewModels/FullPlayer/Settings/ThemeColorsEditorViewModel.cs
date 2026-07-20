using Digimezzo.Foundation.Core.Settings;
using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.Appearance;
using Prism.Commands;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Dopamine.ViewModels.FullPlayer.Settings
{
    public class ThemeColorsEditorViewModel : BindableBase
    {
        private readonly IThemeColorService themeColorService;
        private readonly IAppearanceService appearanceService;
        private ObservableCollection<ThemeColorItemViewModel> backgroundColors;
        private ObservableCollection<ThemeColorItemViewModel> controlColors;
        private ObservableCollection<ThemeColorItemViewModel> textColors;
        private bool hasAnyCustomizations;

        public ThemeColorsEditorViewModel(IThemeColorService themeColorService, IAppearanceService appearanceService)
        {
            this.themeColorService = themeColorService;
            this.appearanceService = appearanceService;

            this.ResetAllCommand = new DelegateCommand(this.ResetAll, () => this.HasAnyCustomizations);
            this.LoadItems();
        }

        public ObservableCollection<ThemeColorItemViewModel> BackgroundColors
        {
            get { return this.backgroundColors; }
            set { SetProperty(ref this.backgroundColors, value); }
        }

        public ObservableCollection<ThemeColorItemViewModel> ControlColors
        {
            get { return this.controlColors; }
            set { SetProperty(ref this.controlColors, value); }
        }

        public ObservableCollection<ThemeColorItemViewModel> TextColors
        {
            get { return this.textColors; }
            set { SetProperty(ref this.textColors, value); }
        }

        public bool HasAnyCustomizations
        {
            get { return this.hasAnyCustomizations; }
            set
            {
                if (SetProperty(ref this.hasAnyCustomizations, value))
                {
                    this.ResetAllCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public DelegateCommand ResetAllCommand { get; }

        public Task<bool> CloseAsync()
        {
            // Live-save model: nothing deferred on close.
            return Task.FromResult(true);
        }

        private bool UseLightTheme
        {
            get { return SettingsClient.Get<bool>("Appearance", "EnableLightTheme"); }
        }

        private void LoadItems()
        {
            bool useLight = this.UseLightTheme;
            var background = new ObservableCollection<ThemeColorItemViewModel>();
            var controls = new ObservableCollection<ThemeColorItemViewModel>();
            var text = new ObservableCollection<ThemeColorItemViewModel>();

            foreach (SemanticColorToken token in this.themeColorService.GetTokens())
            {
                Color color = this.themeColorService.GetEffectiveColor(useLight, token);
                bool customized = this.themeColorService.HasOverride(useLight, token.Id);
                string displayName = ResourceUtils.GetString("Language_" + token.DisplayNameKey);

                var item = new ThemeColorItemViewModel(token, displayName, color, customized)
                {
                    PickColorRequested = this.PickColor,
                    ResetRequested = this.ResetToken
                };

                switch (token.Group)
                {
                    case ThemeColorGroup.Background:
                        background.Add(item);
                        break;
                    case ThemeColorGroup.Controls:
                        controls.Add(item);
                        break;
                    default:
                        text.Add(item);
                        break;
                }
            }

            this.BackgroundColors = background;
            this.ControlColors = controls;
            this.TextColors = text;
            this.UpdateHasCustomizations();
        }

        private void PickColor(ThemeColorItemViewModel item)
        {
            if (item == null)
            {
                return;
            }

            using (var dialog = new System.Windows.Forms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.Color = System.Drawing.Color.FromArgb(item.Color.A, item.Color.R, item.Color.G, item.Color.B);

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                Color selected = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
                this.themeColorService.ApplyToken(this.UseLightTheme, item.TokenId, selected);
                item.Color = selected;
                item.IsCustomized = true;
                this.UpdateHasCustomizations();

                // Keep spectrum / other listeners in sync when accent changes
                if (item.Token.IsAccent)
                {
                    this.appearanceService.OnColorSchemeChanged(System.EventArgs.Empty);
                }
            }
        }

        private async void ResetToken(ThemeColorItemViewModel item)
        {
            if (item == null || !item.IsCustomized)
            {
                return;
            }

            bool useLight = this.UseLightTheme;
            this.themeColorService.ClearToken(useLight, item.TokenId);
            await this.appearanceService.RefreshAppliedColorsAsync();

            item.IsCustomized = false;
            item.Color = this.themeColorService.GetEffectiveColor(useLight, item.Token);
            this.UpdateHasCustomizations();
        }

        private async void ResetAll()
        {
            bool useLight = this.UseLightTheme;
            this.themeColorService.ClearAll(useLight);
            await this.appearanceService.RefreshAppliedColorsAsync();

            foreach (ThemeColorItemViewModel item in this.AllItems())
            {
                item.IsCustomized = false;
                item.Color = this.themeColorService.GetEffectiveColor(useLight, item.Token);
            }

            this.UpdateHasCustomizations();
        }

        private void UpdateHasCustomizations()
        {
            this.HasAnyCustomizations = this.AllItems().Any(i => i.IsCustomized);
        }

        private System.Collections.Generic.IEnumerable<ThemeColorItemViewModel> AllItems()
        {
            if (this.BackgroundColors != null)
            {
                foreach (var item in this.BackgroundColors)
                {
                    yield return item;
                }
            }

            if (this.ControlColors != null)
            {
                foreach (var item in this.ControlColors)
                {
                    yield return item;
                }
            }

            if (this.TextColors != null)
            {
                foreach (var item in this.TextColors)
                {
                    yield return item;
                }
            }
        }
    }
}
