using Digimezzo.Foundation.Core.Settings;
using System;
using System.IO;
using System.Text;

namespace Dopamine.Core.Settings
{
    public static class PlaybackFadeSettings
    {
        private const string SettingsNamespace = "Playback";
        private const string SettingName = "EnablePlaybackFade";
        private const string FileName = "PlaybackFade.settings";
        private static bool? runtimeValue;

        public static bool IsEnabled()
        {
            if (runtimeValue.HasValue)
            {
                return runtimeValue.Value;
            }

            bool fileValue;

            if (TryReadFileValue(out fileValue))
            {
                return fileValue;
            }

            try
            {
                return SettingsClient.Get<bool>(SettingsNamespace, SettingName);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void SetEnabled(bool isEnabled)
        {
            runtimeValue = isEnabled;

            try
            {
                WriteFileValue(isEnabled);
            }
            catch (Exception)
            {
                // Keep the runtime value even if the file cannot be written.
            }

            try
            {
                SettingsClient.Set<bool>(SettingsNamespace, SettingName, isEnabled);
            }
            catch (Exception)
            {
                // Older portable configurations may not contain this setting.
            }
        }

        private static bool TryReadFileValue(out bool value)
        {
            value = false;

            try
            {
                string path = GetSettingsFilePath();

                if (!File.Exists(path))
                {
                    return false;
                }

                return bool.TryParse(File.ReadAllText(path).Trim(), out value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void WriteFileValue(bool value)
        {
            string applicationFolder = SettingsClient.ApplicationFolder();

            if (!Directory.Exists(applicationFolder))
            {
                Directory.CreateDirectory(applicationFolder);
            }

            File.WriteAllText(GetSettingsFilePath(), value ? "True" : "False", Encoding.UTF8);
        }

        private static string GetSettingsFilePath()
        {
            return Path.Combine(SettingsClient.ApplicationFolder(), FileName);
        }
    }
}
