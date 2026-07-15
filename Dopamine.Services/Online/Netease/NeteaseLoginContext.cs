using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Dopamine.Services.Online.Netease
{
    internal static class NeteaseLoginContext
    {
        private const string DefaultAppVersion = "8.10.35";

        internal static string CreateSDeviceId()
        {
            return CreateRandomHex(26);
        }

        internal static string CreateNmtid()
        {
            return CreateRandomHex(16);
        }

        internal static string CreateChainId(string sDeviceId, long timestampMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(sDeviceId))
            {
                throw new ArgumentException("A device identifier is required.", nameof(sDeviceId));
            }

            return string.Format("v1_{0}_web_login_{1}", sDeviceId, timestampMilliseconds);
        }

        internal static string BuildQrContent(string unikey, string chainId)
        {
            if (string.IsNullOrWhiteSpace(unikey) || string.IsNullOrWhiteSpace(chainId))
            {
                throw new ArgumentException("A QR key and chain identifier are required.");
            }

            return "https://music.163.com/st/platform/scanlogin?codekey=" +
                Uri.EscapeDataString(unikey) +
                "&chainId=" + Uri.EscapeDataString(chainId) +
                "&hdw_device=web&hdw_appid=web&hitExp=1";
        }

        internal static Dictionary<string, string> NormalizeCookies(IReadOnlyDictionary<string, string> cookies)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (cookies != null)
            {
                foreach (var cookie in cookies)
                {
                    if (!string.IsNullOrWhiteSpace(cookie.Key) && !string.IsNullOrWhiteSpace(cookie.Value))
                    {
                        normalized[cookie.Key] = cookie.Value;
                    }
                }
            }

            if (normalized.Count > 0)
            {
                if (!normalized.ContainsKey("os"))
                {
                    normalized["os"] = "pc";
                }

                if (!normalized.ContainsKey("appver"))
                {
                    normalized["appver"] = DefaultAppVersion;
                }
            }

            return normalized;
        }

        private static string CreateRandomHex(int byteCount)
        {
            var bytes = new byte[byteCount];

            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}
