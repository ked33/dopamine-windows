using System.Collections.Generic;

namespace Dopamine.Services.Appearance
{
    public enum ThemeColorGroup
    {
        Background,
        Controls,
        Text
    }

    public class SemanticColorToken
    {
        public string Id { get; set; }
        public string DisplayNameKey { get; set; }
        public ThemeColorGroup Group { get; set; }
        public IReadOnlyList<string> ResourceKeys { get; set; }
        public bool IsAccent { get; set; }

        public SemanticColorToken(string id, string displayNameKey, ThemeColorGroup group, bool isAccent, params string[] resourceKeys)
        {
            this.Id = id;
            this.DisplayNameKey = displayNameKey;
            this.Group = group;
            this.IsAccent = isAccent;
            this.ResourceKeys = resourceKeys ?? new string[0];
        }
    }

    public static class SemanticColorTokens
    {
        public static readonly IReadOnlyList<SemanticColorToken> All = new List<SemanticColorToken>
        {
            // Structure / background
            new SemanticColorToken("WindowHeader", "Theme_Color_WindowHeader", ThemeColorGroup.Background, false, "Brush_WindowHeaderBackground"),
            new SemanticColorToken("Sidebar", "Theme_Color_Sidebar", ThemeColorGroup.Background, false, "Brush_PaneBackground"),
            new SemanticColorToken("MainBackground", "Theme_Color_MainBackground", ThemeColorGroup.Background, false, "Brush_MainWindowBackground"),
            new SemanticColorToken("PlayerBackground", "Theme_Color_PlayerBackground", ThemeColorGroup.Background, false, "Brush_NowPlayingBackground"),
            new SemanticColorToken("MiniPlaylistBackground", "Theme_Color_MiniPlaylistBackground", ThemeColorGroup.Background, false, "Brush_MiniPlayerPlaylistBackground"),

            // Lists / controls
            new SemanticColorToken("ListBackground", "Theme_Color_ListBackground", ThemeColorGroup.Controls, false, "Brush_ListBoxBackground"),
            new SemanticColorToken("InputBackground", "Theme_Color_InputBackground", ThemeColorGroup.Controls, false, "Brush_TextBoxBackground"),
            new SemanticColorToken("ControlBackground", "Theme_Color_ControlBackground", ThemeColorGroup.Controls, false,
                "Brush_RegularButtonBackground", "Brush_HotKeyButtonBackground", "Brush_HotKeyBackground"),
            new SemanticColorToken("SliderTrack", "Theme_Color_SliderTrack", ThemeColorGroup.Controls, false,
                "Brush_SliderBackground", "Brush_SliderBackgroundTransparent"),
            new SemanticColorToken("Separator", "Theme_Color_Separator", ThemeColorGroup.Controls, false, "Brush_ItemSeparator"),
            new SemanticColorToken("Border", "Theme_Color_Border", ThemeColorGroup.Controls, false, "Brush_ControlBorder"),
            new SemanticColorToken("ItemHover", "Theme_Color_ItemHover", ThemeColorGroup.Controls, false, "Brush_ItemHovered", "Color_ItemHovered"),
            new SemanticColorToken("ItemSelected", "Theme_Color_ItemSelected", ThemeColorGroup.Controls, false, "Brush_ItemSelected"),

            // Text / accent
            new SemanticColorToken("PrimaryText", "Theme_Color_PrimaryText", ThemeColorGroup.Text, false, "Brush_PrimaryText"),
            new SemanticColorToken("SecondaryText", "Theme_Color_SecondaryText", ThemeColorGroup.Text, false, "Brush_SecondaryText"),
            new SemanticColorToken("Accent", "Theme_Color_Accent", ThemeColorGroup.Text, true, "Color_Accent", "Brush_Accent"),
        };

        public static SemanticColorToken GetById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (SemanticColorToken token in All)
            {
                if (string.Equals(token.Id, id, System.StringComparison.OrdinalIgnoreCase))
                {
                    return token;
                }
            }

            return null;
        }
    }
}
