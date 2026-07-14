using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public interface IOnlineAudioFallbackProvider
    {
        string Id { get; }

        int Order { get; }

        bool CanHandle(NeteaseError officialFailure);

        Task<PlaybackSourceResolution> TryResolveAsync(
            TrackSourceInfo sourceInfo,
            NeteaseError officialFailure,
            CancellationToken cancellationToken);
    }
}
