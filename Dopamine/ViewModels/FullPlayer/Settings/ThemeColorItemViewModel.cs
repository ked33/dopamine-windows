using Dopamine.Services.Appearance;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Windows.Media;

namespace Dopamine.ViewModels.FullPlayer.Settings
{
    public class ThemeColorItemViewModel : BindableBase
    {
        private Color color;
        private bool isCustomized;

        public ThemeColorItemViewModel(SemanticColorToken token, string displayName, Color color, bool isCustomized)
        {
            this.Token = token;
            this.DisplayName = displayName;
            this.color = color;
            this.isCustomized = isCustomized;
            this.PickColorCommand = new DelegateCommand(this.OnPickColor);
            this.ResetCommand = new DelegateCommand(this.OnReset, () => this.IsCustomized);
        }

        public SemanticColorToken Token { get; private set; }
        public string DisplayName { get; private set; }
        public string TokenId
        {
            get { return this.Token.Id; }
        }

        public Color Color
        {
            get { return this.color; }
            set
            {
                if (SetProperty(ref this.color, value))
                {
                    this.RaisePropertyChanged(nameof(this.ColorBrush));
                }
            }
        }

        public SolidColorBrush ColorBrush
        {
            get { return new SolidColorBrush(this.Color); }
        }

        public bool IsCustomized
        {
            get { return this.isCustomized; }
            set
            {
                if (SetProperty(ref this.isCustomized, value))
                {
                    this.ResetCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public DelegateCommand PickColorCommand { get; private set; }
        public DelegateCommand ResetCommand { get; private set; }

        public Action<ThemeColorItemViewModel> PickColorRequested { get; set; }
        public Action<ThemeColorItemViewModel> ResetRequested { get; set; }

        private void OnPickColor()
        {
            if (this.PickColorRequested != null)
            {
                this.PickColorRequested(this);
            }
        }

        private void OnReset()
        {
            if (this.ResetRequested != null)
            {
                this.ResetRequested(this);
            }
        }
    }
}
