using Dopamine.Core.Settings;
using Dopamine.Services.Playback;

namespace Dopamine.Services.Online.Netease
{
    public static class NeteaseDownloadSettings
    {
        private const string SettingsNamespace = "Online";

        public static string DownloadDirectory
        {
            get
            {
                return SettingDefaults.GetOrAdd(
                    SettingsNamespace,
                    "DownloadDirectory",
                    string.Empty) ?? string.Empty;
            }
            set
            {
                SettingDefaults.SetSafe(
                    SettingsNamespace,
                    "DownloadDirectory",
                    (value ?? string.Empty).Trim());
            }
        }

        public static OnlineAudioSourcePriority SourcePriority
        {
            get
            {
                int value = SettingDefaults.GetOrAdd(
                    SettingsNamespace,
                    "DownloadSourcePriority",
                    (int)OnlineAudioSourcePriority.OfficialFirst);
                if (value == (int)OnlineAudioSourcePriority.OfficialFirst ||
                    value == (int)OnlineAudioSourcePriority.UnblockFirst)
                {
                    return (OnlineAudioSourcePriority)value;
                }

                SettingDefaults.SetSafe(
                    SettingsNamespace,
                    "DownloadSourcePriority",
                    (int)OnlineAudioSourcePriority.OfficialFirst);
                return OnlineAudioSourcePriority.OfficialFirst;
            }
            set
            {
                OnlineAudioSourcePriority safeValue = value == OnlineAudioSourcePriority.UnblockFirst
                    ? OnlineAudioSourcePriority.UnblockFirst
                    : OnlineAudioSourcePriority.OfficialFirst;
                SettingDefaults.SetSafe(
                    SettingsNamespace,
                    "DownloadSourcePriority",
                    (int)safeValue);
            }
        }
    }
}
