using System;
using System.Collections.Generic;

namespace Dopamine.Services.Appearance
{
    public class ThemeColorProfile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, string> Colors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool TryGetColor(string tokenId, out string hex)
        {
            hex = null;

            if (this.Colors == null || string.IsNullOrEmpty(tokenId))
            {
                return false;
            }

            return this.Colors.TryGetValue(tokenId, out hex) && !string.IsNullOrWhiteSpace(hex);
        }

        public void SetColor(string tokenId, string hex)
        {
            if (string.IsNullOrEmpty(tokenId) || string.IsNullOrWhiteSpace(hex))
            {
                return;
            }

            if (this.Colors == null)
            {
                this.Colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            this.Colors[tokenId] = hex;
        }

        public bool RemoveColor(string tokenId)
        {
            if (this.Colors == null || string.IsNullOrEmpty(tokenId))
            {
                return false;
            }

            return this.Colors.Remove(tokenId);
        }

        public void Clear()
        {
            if (this.Colors != null)
            {
                this.Colors.Clear();
            }
        }

        public bool HasColor(string tokenId)
        {
            string hex;
            return this.TryGetColor(tokenId, out hex);
        }

        public bool HasAny
        {
            get { return this.Colors != null && this.Colors.Count > 0; }
        }
    }
}
