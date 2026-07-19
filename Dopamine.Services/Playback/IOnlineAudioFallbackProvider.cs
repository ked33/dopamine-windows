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

        Task<OnlineAudioFallbackResult> TryResolveAsync(
            OnlineAudioFallbackRequest request,
            CancellationToken cancellationToken);
    }
}
