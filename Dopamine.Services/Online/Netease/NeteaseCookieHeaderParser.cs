using System;
using System.Collections.Generic;

namespace Dopamine.Services.Online.Netease
{
    public static class NeteaseCookieHeaderParser
    {
        private const int MaximumLength = 32 * 1024;

        public static NeteaseResult<IReadOnlyDictionary<string, string>> Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumLength)
            {
                return Invalid();
            }

            string value = input.Trim();

            if (value.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring("Cookie:".Length).Trim();
            }

            if (ContainsControlCharacter(value))
            {
                return Invalid();
            }

            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawSegment in value.Split(';'))
            {
                string segment = rawSegment.Trim();

                if (segment.Length == 0)
                {
                    continue;
                }

                int separator = segment.IndexOf('=');

                if (separator <= 0)
                {
                    return Invalid();
                }

                string name = segment.Substring(0, separator).Trim();
                string cookieValue = segment.Substring(separator + 1).Trim();

                if (name.Length == 0 || ContainsControlCharacter(name) || ContainsControlCharacter(cookieValue))
                {
                    return Invalid();
                }

                cookies[name] = cookieValue;
            }

            if (cookies.Count == 0)
            {
                return Invalid();
            }

            return NeteaseResult<IReadOnlyDictionary<string, string>>.Success(cookies);
        }

        private static bool ContainsControlCharacter(string value)
        {
            foreach (char character in value)
            {
                if (char.IsControl(character))
                {
                    return true;
                }
            }

            return false;
        }

        private static NeteaseResult<IReadOnlyDictionary<string, string>> Invalid()
        {
            return NeteaseResult<IReadOnlyDictionary<string, string>>.Failure(new NeteaseError(
                NeteaseErrorCode.InvalidCookie,
                "Language_Netease_Invalid_Cookie"));
        }
    }
}
