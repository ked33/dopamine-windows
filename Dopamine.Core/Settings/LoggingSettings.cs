namespace Dopamine.Core.Settings
{
    public static class LoggingSettings
    {
        private const string SettingsNamespace = "Appearance";
        private const string SettingName = "EnableLogging";
        private static readonly object SyncRoot = new object();
        private static bool? isEnabled;

        public static bool IsEnabled()
        {
            lock (SyncRoot)
            {
                if (!isEnabled.HasValue)
                {
                    // Read once per process; logging must not reload Settings.xml for
                    // every entry or replace a live preference with a stale disk value.
                    isEnabled = SettingDefaults.GetOrAdd(SettingsNamespace, SettingName, true);
                }

                return isEnabled.Value;
            }
        }

        public static void SetEnabled(bool isEnabled)
        {
            lock (SyncRoot)
            {
                // Apply immediately, even if persisting the setting fails.
                LoggingSettings.isEnabled = isEnabled;
                SettingDefaults.SetSafe(SettingsNamespace, SettingName, isEnabled);
            }
        }
    }
}
