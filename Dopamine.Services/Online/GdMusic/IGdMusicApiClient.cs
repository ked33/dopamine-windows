using Dopamine.Services.Online.Netease;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.GdMusic
{
    public interface IGdMusicApiClient
    {
        Task<NeteaseResult<IReadOnlyList<GdMusicSearchResult>>> SearchAsync(
            string source,
            string keyword,
            int count,
            int page,
            CancellationToken cancellationToken);

        Task<NeteaseResult<GdMusicTrackUrl>> GetTrackUrlAsync(
            string source,
            string trackId,
            int bitRate,
            CancellationToken cancellationToken);

        Task<NeteaseResult<string>> GetPictureUrlAsync(
            string source,
            string pictureId,
            CancellationToken cancellationToken);
    }
}
