using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteaseMusicService
    {
        Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> GetDailyRecommendationsAsync(
            CancellationToken cancellationToken);

        Task<NeteaseAudioResolution> ResolveOfficialAudioAsync(
            string songId,
            bool forceRefresh,
            CancellationToken cancellationToken);

        Task<NeteaseLyricResult> GetLyricsAsync(string songId, CancellationToken cancellationToken);

        void ClearSessionCaches();
    }
}
