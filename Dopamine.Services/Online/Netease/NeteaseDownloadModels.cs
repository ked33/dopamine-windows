using System;

namespace Dopamine.Services.Online.Netease
{
    public enum NeteaseDownloadStatus
    {
        Success = 0,
        AlreadyExists = 1,
        PartialSuccess = 2,
        DirectoryNotConfigured = 3,
        Cancelled = 4,
        Failed = 5
    }

    public sealed class NeteaseDownloadResult
    {
        public NeteaseDownloadStatus Status { get; set; }

        public string DisplayName { get; set; }

        public string FilePath { get; set; }

        public string MessageKey { get; set; }

        public string ProviderId { get; set; }
    }

    public sealed class NeteaseDownloadStateChangedEventArgs : EventArgs
    {
        public NeteaseDownloadStateChangedEventArgs(string songId, bool isDownloading)
        {
            this.SongId = songId;
            this.IsDownloading = isDownloading;
        }

        public string SongId { get; private set; }

        public bool IsDownloading { get; private set; }
    }
}
