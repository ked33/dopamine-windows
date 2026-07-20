using Dopamine.Core.Settings;

namespace Dopamine.Services.Online.Netease
{
    public static class NeteasePersonalFmSettings
    {
        private const string SettingsNamespace = "Netease";
        private const string FilterLikedSongsSettingName = "PersonalFmFilterLikedSongs";

        public static bool FilterLikedSongs
        {
            get
            {
                return SettingDefaults.GetOrAdd<bool>(
                    SettingsNamespace,
                    FilterLikedSongsSettingName,
                    true);
            }
            set
            {
                SettingDefaults.SetSafe<bool>(
                    SettingsNamespace,
                    FilterLikedSongsSettingName,
                    value);
            }
        }
    }
}
