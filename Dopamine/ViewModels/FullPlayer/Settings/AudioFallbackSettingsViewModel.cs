using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.Playback;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Dopamine.ViewModels.FullPlayer.Settings
{
    public sealed class AudioFallbackSettingsViewModel : BindableBase
    {
        private readonly IAudioFallbackSettings settings;
        private AudioFallbackConfiguration configuration;

        public AudioFallbackSettingsViewModel(IAudioFallbackSettings settings)
        {
            this.settings = settings;
            this.configuration = settings.Current;
            this.Sources = new ObservableCollection<AudioFallbackSourceViewModel>(this.configuration.Sources.Select(
                x => new AudioFallbackSourceViewModel(this, x)));
            this.RefreshCommands();
        }

        public ObservableCollection<AudioFallbackSourceViewModel> Sources { get; }
        public IReadOnlyList<KeyValuePair<int, string>> GdQualities => new[] { 128, 192, 320, 740, 999 }
            .Select(x => new KeyValuePair<int, string>(x, ResourceUtils.GetString("Language_GdMusic_Quality_" + x))).ToList();
        public bool HasUnblockSources => this.configuration.HasUnblockSources;
        public string SaveError => string.IsNullOrEmpty(this.settings.Error) ? string.Empty :
            ResourceUtils.GetString("Language_Audio_Fallback_Save_Error") + " (" + this.settings.Error + ")";

        public bool Enabled
        {
            get { return this.configuration.Enabled; }
            set { var next = this.configuration.Normalize(); next.Enabled = value; this.Save(next); RaisePropertyChanged(); }
        }

        public int GdQuality
        {
            get { return this.configuration.GdQuality; }
            set { if (!this.GdQualities.Any(x => x.Key == value)) return; var next = this.configuration.Normalize(); next.GdQuality = value; this.Save(next); RaisePropertyChanged(); }
        }

        public bool UnblockEnableFlac
        {
            get { return this.configuration.UnblockEnableFlac; }
            set { var next = this.configuration.Normalize(); next.UnblockEnableFlac = value; this.Save(next); RaisePropertyChanged(); }
        }

        internal bool SetEnabled(string id, bool enabled)
        {
            var next = this.configuration.Normalize();
            next.Sources.Single(x => x.Id == id).Enabled = enabled;
            return this.Save(next);
        }

        public void RefreshLanguage()
        {
            RaisePropertyChanged(nameof(this.GdQualities));
            RaisePropertyChanged(nameof(this.SaveError));
        }

        internal bool CanMove(AudioFallbackSourceViewModel row, int delta)
        {
            int index = this.Sources.IndexOf(row);
            return index >= 0 && index + delta >= 0 && index + delta < this.Sources.Count;
        }

        internal void Move(AudioFallbackSourceViewModel row, int delta)
        {
            if (!this.CanMove(row, delta)) return;
            int index = this.Sources.IndexOf(row);
            var next = this.configuration.Normalize();
            var item = next.Sources[index];
            next.Sources.RemoveAt(index);
            next.Sources.Insert(index + delta, item);
            if (!this.Save(next)) return;
            this.Sources.Move(index, index + delta);
            this.RefreshCommands();
        }

        private bool Save(AudioFallbackConfiguration next)
        {
            bool saved = this.settings.TrySave(next);
            if (saved) this.configuration = this.settings.Current;
            RaisePropertyChanged(nameof(this.SaveError));
            RaisePropertyChanged(nameof(this.HasUnblockSources));
            return saved;
        }

        private void RefreshCommands()
        {
            foreach (var row in this.Sources)
            {
                row.MoveUpCommand.RaiseCanExecuteChanged();
                row.MoveDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public sealed class AudioFallbackSourceViewModel : BindableBase
    {
        private readonly AudioFallbackSettingsViewModel owner;
        private bool enabled;

        public AudioFallbackSourceViewModel(AudioFallbackSettingsViewModel owner, AudioFallbackSource source)
        {
            this.owner = owner;
            this.Id = source.Id;
            this.enabled = source.Enabled;
            this.MoveUpCommand = new DelegateCommand(() => owner.Move(this, -1), () => owner.CanMove(this, -1));
            this.MoveDownCommand = new DelegateCommand(() => owner.Move(this, 1), () => owner.CanMove(this, 1));
        }

        public string Id { get; }
        public string Name => AudioFallbackCatalog.Find(this.Id).Name;
        public DelegateCommand MoveUpCommand { get; }
        public DelegateCommand MoveDownCommand { get; }
        public bool Enabled
        {
            get { return this.enabled; }
            set
            {
                if (value == this.enabled) return;
                if (this.owner.SetEnabled(this.Id, value)) this.enabled = value;
                RaisePropertyChanged();
            }
        }
    }
}
