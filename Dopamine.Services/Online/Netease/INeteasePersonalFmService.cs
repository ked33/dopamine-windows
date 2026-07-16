using Dopamine.Core.Base;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteasePersonalFmService
    {
        bool IsActive { get; }

        bool IsBusy { get; }

        int BufferedTrackCount { get; }

        TrackViewModel CurrentTrack { get; }

        NeteaseError Error { get; }

        Task<NeteaseResult<bool>> StartAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<bool>> SkipAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<bool>> DislikeCurrentAsync(CancellationToken cancellationToken);

        void Exit();

        event EventHandler StateChanged;
    }
}
