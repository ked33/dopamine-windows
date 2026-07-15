using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteaseApiClient
    {
        Task<NeteaseResult<NeteaseQrKey>> CreateQrKeyAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<NeteaseQrCheck>> CheckQrAsync(NeteaseQrSession session, CancellationToken cancellationToken);

        Task<NeteaseResult<NeteaseAccountProfile>> GetLoginStatusAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> GetDailyRecommendationsAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<IReadOnlyCollection<string>>> GetLikedSongIdsAsync(
            string userId,
            CancellationToken cancellationToken);

        Task<NeteaseResult<bool>> SetSongLikedAsync(
            string songId,
            bool isLiked,
            CancellationToken cancellationToken);

        Task<NeteaseResult<NeteaseRecommendedSong>> DislikeDailyRecommendationAsync(
            string songId,
            CancellationToken cancellationToken);

        Task<NeteaseAudioResolution> GetSongUrlAsync(string songId, string level, CancellationToken cancellationToken);

        Task<NeteaseLyricResult> GetLyricsAsync(string songId, CancellationToken cancellationToken);

        void ReplaceCookies(IReadOnlyDictionary<string, string> cookies);

        IReadOnlyDictionary<string, string> SnapshotCookies();

        void ClearCookies();
    }
}
