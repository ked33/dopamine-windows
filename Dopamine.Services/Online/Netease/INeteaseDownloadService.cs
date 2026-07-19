using Dopamine.Services.Entities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteaseDownloadService
    {
        event EventHandler<NeteaseDownloadStateChangedEventArgs> DownloadStateChanged;

        bool IsDownloading(string songId);

        Task<NeteaseDownloadResult> DownloadAsync(
            TrackViewModel track,
            CancellationToken cancellationToken);
    }
}
