using Dopamine.Core.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dopamine.Services.Playback
{
    public static class UnblockNeteaseMusicSettings
    {
        private const string SettingsNamespace = "Netease";
        private static readonly string[] AllowedSources = { "kugou", "bodian", "kuwo" };

        public static bool IsEnabled
        {
            get { return SettingDefaults.GetOrAdd(SettingsNamespace, "EnableUnblockNeteaseMusic", false); }
            set { SettingDefaults.SetSafe(SettingsNamespace, "EnableUnblockNeteaseMusic", value); }
        }

        public static bool EnableFlac
        {
            get { return SettingDefaults.GetOrAdd(SettingsNamespace, "UnblockEnableFlac", false); }
            set { SettingDefaults.SetSafe(SettingsNamespace, "UnblockEnableFlac", value); }
        }

        public static IReadOnlyList<string> Sources
        {
            get
            {
                string configured = SettingDefaults.GetOrAdd(
                    SettingsNamespace,
                    "UnblockSources",
                    "kugou;bodian;kuwo");
                List<string> sources = (configured ?? string.Empty)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Where(x => AllowedSources.Contains(x))
                    .Distinct()
                    .ToList();
                return sources.Count > 0 ? sources : AllowedSources;
            }
            set
            {
                string configured = string.Join(
                    ";",
                    (value ?? Array.Empty<string>())
                        .Select(x => (x ?? string.Empty).Trim().ToLowerInvariant())
                        .Where(x => AllowedSources.Contains(x))
                        .Distinct());
                SettingDefaults.SetSafe(
                    SettingsNamespace,
                    "UnblockSources",
                    string.IsNullOrWhiteSpace(configured) ? "kugou;bodian;kuwo" : configured);
            }
        }
    }
}
