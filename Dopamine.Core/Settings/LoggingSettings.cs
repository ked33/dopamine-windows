namespace Dopamine.Core.Settings
{
    public static class LoggingSettings
    {
        private const string SettingsNamespace = "Appearance";
        private const string SettingName = "EnableLogging";

        public static bool IsEnabled()
        {
            return SettingDefaults.GetOrAdd(SettingsNamespace, SettingName, true);
        }

        public static void SetEnabled(bool isEnabled)
        {
            SettingDefaults.SetSafe(SettingsNamespace, SettingName, isEnabled);
        }
    }
}
