using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.IO;
using Dopamine.Core.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Dopamine.Services.Appearance
{
    public class ThemeColorService : IThemeColorService
    {
        private readonly string themeColorsDirectory;
        private ThemeColorProfile darkProfile;
        private ThemeColorProfile lightProfile;

        public event EventHandler ThemeColorsChanged = delegate { };

        public ThemeColorService()
        {
            this.themeColorsDirectory = Path.Combine(SettingsClient.ApplicationFolder(), ApplicationPaths.ThemeColorsFolder);

            if (!Directory.Exists(this.themeColorsDirectory))
            {
                Directory.CreateDirectory(this.themeColorsDirectory);
            }

            this.darkProfile = this.LoadProfile(false);
            this.lightProfile = this.LoadProfile(true);
        }

        public IReadOnlyList<SemanticColorToken> GetTokens()
        {
            return SemanticColorTokens.All;
        }

        public ThemeColorProfile GetProfile(bool useLightTheme)
        {
            return useLightTheme ? this.lightProfile : this.darkProfile;
        }

        public bool HasOverride(bool useLightTheme, string tokenId)
        {
            return this.GetProfile(useLightTheme).HasColor(tokenId);
        }

        public bool HasAccentOverride(bool useLightTheme)
        {
            return this.HasOverride(useLightTheme, "Accent");
        }

        public Color? GetOverrideColor(bool useLightTheme, string tokenId)
        {
            string hex;
            if (!this.GetProfile(useLightTheme).TryGetColor(tokenId, out hex))
            {
                return null;
            }

            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not parse theme color override '{0}' for token '{1}'. Exception: {2}", hex, tokenId, ex.Message);
                return null;
            }
        }

        public Color GetEffectiveColor(bool useLightTheme, SemanticColorToken token)
        {
            Nullable<Color> overrideColor = this.GetOverrideColor(useLightTheme, token.Id);
            if (overrideColor.HasValue)
            {
                return overrideColor.Value;
            }

            return this.ReadResourceColor(token);
        }

        public void ApplyToken(bool useLightTheme, string tokenId, Color color)
        {
            SemanticColorToken token = SemanticColorTokens.GetById(tokenId);
            if (token == null)
            {
                return;
            }

            string hex = color.ToString();
            ThemeColorProfile profile = this.GetProfile(useLightTheme);
            profile.SetColor(tokenId, hex);
            this.SaveProfile(useLightTheme, profile);
            this.ApplyTokenToResources(token, color);
            this.ThemeColorsChanged(this, EventArgs.Empty);
        }

        public void ClearToken(bool useLightTheme, string tokenId)
        {
            ThemeColorProfile profile = this.GetProfile(useLightTheme);
            if (!profile.RemoveColor(tokenId))
            {
                return;
            }

            this.SaveProfile(useLightTheme, profile);
            this.ThemeColorsChanged(this, EventArgs.Empty);
        }

        public void ClearAll(bool useLightTheme)
        {
            ThemeColorProfile profile = this.GetProfile(useLightTheme);
            if (!profile.HasAny)
            {
                return;
            }

            profile.Clear();
            this.SaveProfile(useLightTheme, profile);
            this.ThemeColorsChanged(this, EventArgs.Empty);
        }

        public void ReapplyOverrides(bool useLightTheme)
        {
            ThemeColorProfile profile = this.GetProfile(useLightTheme);
            if (profile == null || profile.Colors == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> pair in profile.Colors)
            {
                SemanticColorToken token = SemanticColorTokens.GetById(pair.Key);
                if (token == null)
                {
                    continue;
                }

                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(pair.Value);
                    this.ApplyTokenToResources(token, color);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Could not reapply theme color '{0}' for token '{1}'. Exception: {2}", pair.Value, pair.Key, ex.Message);
                }
            }
        }

        private void ApplyTokenToResources(SemanticColorToken token, Color color)
        {
            if (Application.Current == null)
            {
                return;
            }

            foreach (string key in token.ResourceKeys)
            {
                this.SetResourceColor(key, color);
            }
        }

        private void SetResourceColor(string key, Color color)
        {
            try
            {
                object existing = Application.Current.Resources[key];

                SolidColorBrush solidBrush = existing as SolidColorBrush;
                if (solidBrush != null)
                {
                    Color sourceColor = solidBrush.Color;
                    Color newColor = color;

                    // Preserve embedded alpha when the existing brush color is translucent
                    // and the incoming color is opaque (typical color-picker output).
                    if (sourceColor.A < 255 && color.A == 255)
                    {
                        newColor = Color.FromArgb(sourceColor.A, color.R, color.G, color.B);
                    }
                    else if (color.A == 255)
                    {
                        newColor = Color.FromRgb(color.R, color.G, color.B);
                    }

                    Application.Current.Resources[key] = new SolidColorBrush(newColor) { Opacity = solidBrush.Opacity };
                    return;
                }

                if (key.StartsWith("Color_", StringComparison.Ordinal) || existing is Color)
                {
                    Color newColor = color.A == 255 ? Color.FromRgb(color.R, color.G, color.B) : color;
                    Application.Current.Resources[key] = newColor;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not set theme resource '{0}'. Exception: {1}", key, ex.Message);
            }
        }

        private Color ReadResourceColor(SemanticColorToken token)
        {
            if (Application.Current == null || token.ResourceKeys.Count == 0)
            {
                return Colors.Transparent;
            }

            string firstKey = token.ResourceKeys[0];

            try
            {
                object resource = Application.Current.Resources[firstKey];

                SolidColorBrush brush = resource as SolidColorBrush;
                if (brush != null)
                {
                    return brush.Color;
                }

                if (resource is Color)
                {
                    return (Color)resource;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not read theme resource '{0}'. Exception: {1}", firstKey, ex.Message);
            }

            return Colors.Transparent;
        }

        private ThemeColorProfile LoadProfile(bool useLightTheme)
        {
            string path = this.GetProfilePath(useLightTheme);

            try
            {
                // Fully qualify System.IO.File: Dopamine.Services.File is a sibling namespace.
                if (!System.IO.File.Exists(path))
                {
                    return new ThemeColorProfile();
                }

                string json = System.IO.File.ReadAllText(path);
                ThemeColorProfile profile = JsonConvert.DeserializeObject<ThemeColorProfile>(json);

                if (profile == null)
                {
                    return new ThemeColorProfile();
                }

                if (profile.Colors == null)
                {
                    profile.Colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    // Normalize to case-insensitive dictionary
                    var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, string> pair in profile.Colors)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                        {
                            // Validate color string; skip invalid entries
                            try
                            {
                                ColorConverter.ConvertFromString(pair.Value);
                                normalized[pair.Key] = pair.Value;
                            }
                            catch (Exception)
                            {
                                AppLog.Error("Ignoring invalid theme color '{0}' for token '{1}'.", pair.Value, pair.Key);
                            }
                        }
                    }

                    profile.Colors = normalized;
                }

                return profile;
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not load theme color profile '{0}'. Exception: {1}", path, ex.Message);
                return new ThemeColorProfile();
            }
        }

        private void SaveProfile(bool useLightTheme, ThemeColorProfile profile)
        {
            string path = this.GetProfilePath(useLightTheme);

            try
            {
                if (!Directory.Exists(this.themeColorsDirectory))
                {
                    Directory.CreateDirectory(this.themeColorsDirectory);
                }

                if (profile == null || !profile.HasAny)
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }

                    return;
                }

                string json = JsonConvert.SerializeObject(profile, Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not save theme color profile '{0}'. Exception: {1}", path, ex.Message);
            }
        }

        private string GetProfilePath(bool useLightTheme)
        {
            return Path.Combine(this.themeColorsDirectory, useLightTheme ? "Light.json" : "Dark.json");
        }
    }
}
