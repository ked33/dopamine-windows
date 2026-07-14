using Dopamine.Services.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public interface IPlaybackSourceResolver
    {
        Task<PlaybackSourceResolution> ResolveAsync(
            TrackViewModel track,
            PlaybackSourceRequest request,
            CancellationToken cancellationToken);
    }
}
