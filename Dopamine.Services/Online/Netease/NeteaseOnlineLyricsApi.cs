using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Api.Lyrics;
using Dopamine.Core.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    // Netease lyrics provider for the automatic lyrics download setting.
    // Uses the encrypted EAPI endpoints (cloudsearch + song/lyric/v1) via
    // HyPlayer.NeteaseApi instead of the retired plain music.163.com/api routes.
    public class NeteaseOnlineLyricsApi : ILyricsApi
    {
        private readonly INeteaseApiClient apiClient;
        private readonly ILocalizationInfo info;
        private readonly bool enableTLyric;

        public NeteaseOnlineLyricsApi(INeteaseApiClient apiClient, ILocalizationInfo info)
        {
            this.apiClient = apiClient;
            this.info = info;
            this.enableTLyric = SettingsClient.Get<string>("Appearance", "Language") == "ZH-CN";
        }

        public string SourceName => this.info.NeteaseLyrics;

        public async Task<string> GetLyricsAsync(string artist, string title)
        {
            NeteaseResult<string> searchResult = await this.apiClient.SearchSongIdAsync(
                title + "\x20" + artist,
                CancellationToken.None);

            if (!searchResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Netease song search failed. ErrorCode={searchResult.Error?.Code}");
            }

            NeteaseLyricResult lyricResult = await this.apiClient.GetLyricsAsync(
                searchResult.Value,
                CancellationToken.None);

            if (!lyricResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Netease lyric request failed. ErrorCode={lyricResult.Error?.Code}");
            }

            if (string.IsNullOrEmpty(lyricResult.TranslationLyric) || !this.enableTLyric)
            {
                return lyricResult.Lyric;
            }

            return lyricResult.TranslationLyric;
        }
    }
}
