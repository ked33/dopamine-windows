using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public interface IUnblockSidecarService : IDisposable
    {
        UnblockSidecarState State { get; }

        string Version { get; }

        Task<UnblockSidecarMatchResult> MatchAsync(
            UnblockSidecarMatchRequest request,
            CancellationToken cancellationToken);

        Task<bool> RestartAsync(CancellationToken cancellationToken);

        void Stop();

        event EventHandler StateChanged;
    }
}
