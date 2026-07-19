using Dopamine.Core.Settings;

namespace Dopamine.Services.Online.Netease
{
    public enum NeteaseAudioQuality
    {
        Standard = 0,
        Higher = 1,
        ExHigh = 2
    }

    public static class NeteaseAudioQualitySettings
    {
        private const string SettingsNamespace = "Online";
        private const string SettingName = "NeteaseAudioQuality";

        public static NeteaseAudioQuality Quality
        {
            get
            {
                int value = SettingDefaults.GetOrAdd<int>(
                    SettingsNamespace,
                    SettingName,
                    (int)NeteaseAudioQuality.ExHigh);
                if (IsValid(value))
                {
                    return (NeteaseAudioQuality)value;
                }

                SettingDefaults.SetSafe<int>(
                    SettingsNamespace,
                    SettingName,
                    (int)NeteaseAudioQuality.ExHigh);
                return NeteaseAudioQuality.ExHigh;
            }
            set
            {
                NeteaseAudioQuality safeValue = IsValid((int)value)
                    ? value
                    : NeteaseAudioQuality.ExHigh;
                SettingDefaults.SetSafe<int>(SettingsNamespace, SettingName, (int)safeValue);
            }
        }

        public static string Level
        {
            get
            {
                switch (Quality)
                {
                    case NeteaseAudioQuality.Standard:
                        return "standard";
                    case NeteaseAudioQuality.Higher:
                        return "higher";
                    default:
                        return "exhigh";
                }
            }
        }

        private static bool IsValid(int value)
        {
            return value >= (int)NeteaseAudioQuality.Standard &&
                value <= (int)NeteaseAudioQuality.ExHigh;
        }
    }
}
