using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Logging;
using Dopamine.Services.Entities;
using Dopamine.Services.Notification;
using Dopamine.Services.Online.GdMusic;
using Dopamine.Services.Online.Netease;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online
{
    /// <summary>
    /// Coordinates online search downloads (GD music platform API) and shows
    /// the same toast feedback as <see cref="OnlineTrackDownloadCoordinator"/>.
    /// </summary>
    public sealed class GdMusicDownloadCoordinator
    {
        private readonly GdMusicDownloadService downloadService;
        private readonly IToastService toastService;

        public GdMusicDownloadCoordinator(
            GdMusicDownloadService downloadService,
            IToastService toastService)
        {
            this.downloadService = downloadService;
            this.toastService = toastService;
            this.downloadService.DownloadStateChanged += (sender, args) =>
                this.DownloadStateChanged(sender, args);
        }

        public event EventHandler<NeteaseDownloadStateChangedEventArgs> DownloadStateChanged = delegate { };

        public bool CanDownload(TrackViewModel track)
        {
            return this.downloadService.CanDownload(track);
        }

        public async Task DownloadAsync(TrackViewModel track)
        {
            if (!this.CanDownload(track))
            {
                return;
            }

            NeteaseDownloadResult result;
            try
            {
                result = await this.downloadService.DownloadAsync(
                    track,
                    CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                result = CreateFailureResult(track, NeteaseDownloadStatus.Cancelled);
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Unexpected online search download failure. ErrorType={0}",
                    ex.GetType().Name);
                result = CreateFailureResult(track, NeteaseDownloadStatus.Failed);
            }

            if (result == null)
            {
                result = CreateFailureResult(track, NeteaseDownloadStatus.Failed);
            }

            string resourceKey;
            switch (result.Status)
            {
                case NeteaseDownloadStatus.Success:
                    resourceKey = "Language_Netease_Download_Toast_Success";
                    break;
                case NeteaseDownloadStatus.AlreadyExists:
                    resourceKey = "Language_Netease_Download_Toast_Already_Exists";
                    break;
                case NeteaseDownloadStatus.PartialSuccess:
                    resourceKey = "Language_Netease_Download_Toast_Partial_Success";
                    break;
                case NeteaseDownloadStatus.DirectoryNotConfigured:
                    resourceKey = "Language_Netease_Download_Toast_Directory_Required";
                    break;
                case NeteaseDownloadStatus.Cancelled:
                    resourceKey = "Language_Netease_Download_Toast_Cancelled";
                    break;
                default:
                    resourceKey = "Language_Netease_Download_Toast_Failed";
                    break;
            }

            string template = ResourceUtils.GetString(resourceKey);
            this.toastService.Show(string.IsNullOrWhiteSpace(template)
                ? result.DisplayName
                : template.Replace("{song}", result.DisplayName ?? string.Empty));
        }

        private static NeteaseDownloadResult CreateFailureResult(
            TrackViewModel track,
            NeteaseDownloadStatus status)
        {
            string songId = track == null || track.SourceInfo == null || string.IsNullOrWhiteSpace(track.SourceInfo.RemoteId)
                ? "gdmusic"
                : track.SourceInfo.RemoteId;
            string artist = string.IsNullOrWhiteSpace(track == null ? null : track.ArtistName) ? songId : track.ArtistName;
            string title = string.IsNullOrWhiteSpace(track == null ? null : track.TrackTitle) ? songId : track.TrackTitle;
            return new NeteaseDownloadResult
            {
                Status = status,
                DisplayName = artist + " - " + title
            };
        }
    }
}
