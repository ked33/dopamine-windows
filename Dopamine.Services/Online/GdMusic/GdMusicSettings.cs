using Dopamine.Core.Settings;
using System.Collections.Generic;
using System.Linq;

namespace Dopamine.Services.Online.GdMusic
{
    public static class GdMusicSettings
    {
        private const string SettingsNamespace = "Online";

        public const string DefaultSearchSource = "netease";
        public const int DefaultDownloadQuality = 999;

        public static readonly IReadOnlyList<string> SupportedSearchSources = new[]
        {
            "netease", "joox", "bilibili", "tencent", "kuwo", "tidal", "qobuz", "apple", "ytmusic", "spotify"
        };

        public static readonly IReadOnlyList<int> SupportedDownloadQualities = new[]
        {
            128, 192, 320, 740, 999
        };

        public static string SearchSource
        {
            get
            {
                string value = SettingDefaults.GetOrAdd(
                    SettingsNamespace,
                    "GdSearchSource",
                    DefaultSearchSource);
                return NormalizeSource(value);
            }
            set
            {
                SettingDefaults.SetSafe(
                    SettingsNamespace,
                    "GdSearchSource",
                    NormalizeSource(value));
            }
        }

        public static int DownloadQuality
        {
            get
            {
                int value = SettingDefaults.GetOrAdd(
                    SettingsNamespace,
                    "GdDownloadQuality",
                    DefaultDownloadQuality);
                return NormalizeQuality(value);
            }
            set
            {
                SettingDefaults.SetSafe(
                    SettingsNamespace,
                    "GdDownloadQuality",
                    NormalizeQuality(value));
            }
        }

        public static string NormalizeSource(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return SupportedSearchSources.Contains(normalized) ? normalized : DefaultSearchSource;
        }

        public static int NormalizeQuality(int value)
        {
            return SupportedDownloadQualities.Contains(value) ? value : DefaultDownloadQuality;
        }
    }
}
