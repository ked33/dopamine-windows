using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Dopamine.Services.Appearance
{
    public interface IThemeColorService
    {
        event EventHandler ThemeColorsChanged;

        IReadOnlyList<SemanticColorToken> GetTokens();
        ThemeColorProfile GetProfile(bool useLightTheme);
        bool HasOverride(bool useLightTheme, string tokenId);
        bool HasAccentOverride(bool useLightTheme);
        Color? GetOverrideColor(bool useLightTheme, string tokenId);
        Color GetEffectiveColor(bool useLightTheme, SemanticColorToken token);
        void ApplyToken(bool useLightTheme, string tokenId, Color color);
        void ClearToken(bool useLightTheme, string tokenId);
        void ClearAll(bool useLightTheme);
        void ReapplyOverrides(bool useLightTheme);
    }
}
