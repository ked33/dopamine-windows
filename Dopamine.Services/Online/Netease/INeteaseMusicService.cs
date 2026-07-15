using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteaseMusicService
    {
        Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> GetDailyRecommendationsAsync(
            CancellationToken cancellationToken);

        Task<NeteaseResult<bool>> IsSongLikedAsync(
            string songId,
            CancellationToken cancellationToken);

        Task<NeteaseResult<bool>> SetSongLikedAsync(
            string songId,
            bool isLiked,
            CancellationToken cancellationToken);

        Task<NeteaseResult<NeteaseRecommendationMutation>> DislikeDailyRecommendationAsync(
            string songId,
            CancellationToken cancellationToken);

        Task<NeteaseAudioResolution> ResolveOfficialAudioAsync(
            string songId,
            bool forceRefresh,
            CancellationToken cancellationToken);

        Task<NeteaseLyricResult> GetLyricsAsync(string songId, CancellationToken cancellationToken);

        void ClearSessionCaches();
    }
}
