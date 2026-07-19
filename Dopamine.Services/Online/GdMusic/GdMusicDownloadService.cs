using Dopamine.Core.Logging;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.GdMusic
{
    /// <summary>
    /// Downloads tracks surfaced by the online search tab. Unlike
    /// <see cref="NeteaseDownloadService"/>, the audio URL is always resolved
    /// through the GD music platform API, regardless of the track source.
    /// </summary>
    public sealed class GdMusicDownloadService
    {
        private const long MaximumArtworkBytes = 10L * 1024L * 1024L;

        private readonly object syncRoot = new object();
        private readonly Dictionary<string, Task<NeteaseDownloadResult>> activeDownloads =
            new Dictionary<string, Task<NeteaseDownloadResult>>(StringComparer.Ordinal);
        private readonly IGdMusicApiClient apiClient;
        private readonly NeteaseTemporaryAudioCache temporaryAudioCache;

        public GdMusicDownloadService(
            IGdMusicApiClient apiClient,
            NeteaseTemporaryAudioCache temporaryAudioCache)
        {
            this.apiClient = apiClient;
            this.temporaryAudioCache = temporaryAudioCache;
        }

        public event EventHandler<NeteaseDownloadStateChangedEventArgs> DownloadStateChanged = delegate { };

        public bool IsDownloading(TrackViewModel track)
        {
            string downloadKey = GetDownloadKey(track);
            if (string.IsNullOrWhiteSpace(downloadKey))
            {
                return false;
            }

            lock (this.syncRoot)
            {
                return this.activeDownloads.ContainsKey(downloadKey);
            }
        }

        public bool CanDownload(TrackViewModel track)
        {
            return !string.IsNullOrWhiteSpace(GetDownloadKey(track)) && !this.IsDownloading(track);
        }

        public Task<NeteaseDownloadResult> DownloadAsync(
            TrackViewModel track,
            CancellationToken cancellationToken)
        {
            string downloadKey = GetDownloadKey(track);
            if (string.IsNullOrWhiteSpace(downloadKey))
            {
                return Task.FromResult(Failure(
                    NeteaseDownloadService.GetDisplayName(track),
                    "Language_Netease_Download_Failed"));
            }

            Task<NeteaseDownloadResult> downloadTask;
            lock (this.syncRoot)
            {
                if (this.activeDownloads.TryGetValue(downloadKey, out downloadTask))
                {
                    return downloadTask;
                }

                downloadTask = Task.Run(() => this.DownloadCoreAsync(track, cancellationToken));
                this.activeDownloads.Add(downloadKey, downloadTask);
            }

            this.DownloadStateChanged(
                this,
                new NeteaseDownloadStateChangedEventArgs(downloadKey, true));
            downloadTask.ContinueWith(
                _ => this.CompleteDownload(downloadKey, downloadTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return downloadTask;
        }

        private async Task<NeteaseDownloadResult> DownloadCoreAsync(
            TrackViewModel track,
            CancellationToken cancellationToken)
        {
            string displayName = NeteaseDownloadService.GetDisplayName(track);
            string partialPath = null;

            try
            {
                string configuredDirectory = NeteaseDownloadSettings.DownloadDirectory;
                if (string.IsNullOrWhiteSpace(configuredDirectory))
                {
                    return new NeteaseDownloadResult
                    {
                        Status = NeteaseDownloadStatus.DirectoryNotConfigured,
                        DisplayName = displayName,
                        MessageKey = "Language_Netease_Download_Directory_Required"
                    };
                }

                if (!Path.IsPathRooted(configuredDirectory))
                {
                    return Failure(displayName, "Language_Netease_Download_Directory_Not_Writable");
                }

                string downloadDirectory = Path.GetFullPath(configuredDirectory);
                Directory.CreateDirectory(downloadDirectory);
                string fileNameBase = NeteaseDownloadService.CreateSafeFileNameBase(track, downloadDirectory);
                if (string.IsNullOrWhiteSpace(fileNameBase))
                {
                    return Failure(displayName, "Language_Netease_Download_Failed");
                }

                string existingPath = NeteaseDownloadService.FindExistingPath(downloadDirectory, fileNameBase);
                if (!string.IsNullOrWhiteSpace(existingPath))
                {
                    return AlreadyExists(displayName, existingPath);
                }

                string source = track.SourceInfo.ProviderId;
                string trackId = track.SourceInfo.RemoteId;
                int bitRate = GdMusicSettings.DownloadQuality;

                cancellationToken.ThrowIfCancellationRequested();
                NeteaseResult<GdMusicTrackUrl> urlResult = await this.apiClient.GetTrackUrlAsync(
                    source,
                    trackId,
                    bitRate,
                    cancellationToken);
                if (!urlResult.IsSuccess)
                {
                    if (urlResult.Error != null && urlResult.Error.Code == NeteaseErrorCode.Cancelled)
                    {
                        return Cancelled(displayName);
                    }

                    string messageKey = urlResult.Error == null || string.IsNullOrWhiteSpace(urlResult.Error.MessageKey)
                        ? "Language_Netease_Download_Source_Unavailable"
                        : urlResult.Error.MessageKey;
                    return Failure(displayName, messageKey);
                }

                string extension = NeteaseTemporaryAudioCache.NormalizeExtension(null, urlResult.Value.Url);
                if (string.Equals(extension, ".audio", StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(displayName, "Language_Netease_Download_Unsupported_Format");
                }

                string cacheKey = string.Format(
                    "gd-{0}-{1}-{2}",
                    GdMusicSettings.NormalizeSource(source),
                    urlResult.Value.BitRate > 0 ? urlResult.Value.BitRate : bitRate,
                    trackId);
                NeteaseResult<string> cached = await this.temporaryAudioCache.GetOrDownloadAsync(
                    cacheKey,
                    urlResult.Value.Url,
                    null,
                    null,
                    cancellationToken);
                if (!cached.IsSuccess)
                {
                    if (cached.Error != null && cached.Error.Code == NeteaseErrorCode.Cancelled)
                    {
                        return Cancelled(displayName);
                    }

                    string messageKey = cached.Error == null || string.IsNullOrWhiteSpace(cached.Error.MessageKey)
                        ? "Language_Netease_Download_Failed"
                        : cached.Error.MessageKey;
                    return Failure(displayName, messageKey);
                }

                string finalPath = Path.Combine(downloadDirectory, fileNameBase + extension);
                if (System.IO.File.Exists(finalPath))
                {
                    return AlreadyExists(displayName, finalPath);
                }

                partialPath = Path.Combine(
                    downloadDirectory,
                    "." + Guid.NewGuid().ToString("N") + ".part" + extension);
                await NeteaseDownloadService.CopyFileAsync(cached.Value, partialPath, cancellationToken);

                bool metadataComplete = true;
                byte[] artwork = await this.TryDownloadArtworkAsync(track, cancellationToken);
                if (artwork == null)
                {
                    metadataComplete = false;
                }

                try
                {
                    NeteaseDownloadService.WriteMetadata(partialPath, track, artwork);
                }
                catch (Exception ex)
                {
                    metadataComplete = false;
                    AppLog.Warning(
                        "Could not write metadata to a downloaded online search track. ErrorType={0}",
                        ex.GetType().Name);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (System.IO.File.Exists(finalPath))
                {
                    return AlreadyExists(displayName, finalPath);
                }

                try
                {
                    System.IO.File.Move(partialPath, finalPath);
                }
                catch (IOException)
                {
                    if (System.IO.File.Exists(finalPath))
                    {
                        return AlreadyExists(displayName, finalPath);
                    }

                    throw;
                }

                partialPath = null;
                return new NeteaseDownloadResult
                {
                    Status = metadataComplete
                        ? NeteaseDownloadStatus.Success
                        : NeteaseDownloadStatus.PartialSuccess,
                    DisplayName = displayName,
                    FilePath = finalPath,
                    ProviderId = "gdmusic"
                };
            }
            catch (OperationCanceledException)
            {
                return Cancelled(displayName);
            }
            catch (IOException ex)
            {
                AppLog.Warning(
                    "Could not save a downloaded online search track. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Directory_Not_Writable");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Warning(
                    "Could not write to the online search download directory. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Directory_Not_Writable");
            }
            catch (SecurityException ex)
            {
                AppLog.Warning(
                    "Could not access the online search download directory. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Directory_Not_Writable");
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not download an online search track. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Failed");
            }
            finally
            {
                NeteaseDownloadService.TryDelete(partialPath);
            }
        }

        private async Task<byte[]> TryDownloadArtworkAsync(
            TrackViewModel track,
            CancellationToken cancellationToken)
        {
            try
            {
                string artworkUrl = track.SourceInfo.ArtworkUrl;
                if (string.IsNullOrWhiteSpace(artworkUrl) &&
                    !string.IsNullOrWhiteSpace(track.SourceInfo.PictureId))
                {
                    NeteaseResult<string> picture = await this.apiClient.GetPictureUrlAsync(
                        track.SourceInfo.ProviderId,
                        track.SourceInfo.PictureId,
                        cancellationToken);
                    if (picture.IsSuccess && !string.IsNullOrWhiteSpace(picture.Value))
                    {
                        artworkUrl = picture.Value;
                        track.SourceInfo.ArtworkUrl = artworkUrl;
                    }
                }

                if (string.IsNullOrWhiteSpace(artworkUrl))
                {
                    return null;
                }

                NeteaseResult<byte[]> artworkResult = await this.temporaryAudioCache.DownloadBytesAsync(
                    artworkUrl,
                    MaximumArtworkBytes,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return artworkResult.IsSuccess ? artworkResult.Value : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not download artwork for an online search track. ErrorType={0}",
                    ex.GetType().Name);
                return null;
            }
        }

        private void CompleteDownload(string downloadKey, Task<NeteaseDownloadResult> completedTask)
        {
            bool removed = false;
            lock (this.syncRoot)
            {
                Task<NeteaseDownloadResult> current;
                if (this.activeDownloads.TryGetValue(downloadKey, out current) &&
                    object.ReferenceEquals(current, completedTask))
                {
                    removed = this.activeDownloads.Remove(downloadKey);
                }
            }

            if (removed)
            {
                this.DownloadStateChanged(
                    this,
                    new NeteaseDownloadStateChangedEventArgs(downloadKey, false));
            }
        }

        private static string GetDownloadKey(TrackViewModel track)
        {
            if (track == null || track.SourceInfo == null ||
                string.IsNullOrWhiteSpace(track.SourceInfo.RemoteId))
            {
                return null;
            }

            if (track.SourceInfo.Kind != TrackSourceKind.Netease &&
                track.SourceInfo.Kind != TrackSourceKind.ExternalOnline)
            {
                return null;
            }

            string source = string.IsNullOrWhiteSpace(track.SourceInfo.ProviderId)
                ? "netease"
                : track.SourceInfo.ProviderId;
            return source + "/" + track.SourceInfo.RemoteId;
        }

        private static NeteaseDownloadResult AlreadyExists(string displayName, string path)
        {
            return new NeteaseDownloadResult
            {
                Status = NeteaseDownloadStatus.AlreadyExists,
                DisplayName = displayName,
                FilePath = path
            };
        }

        private static NeteaseDownloadResult Cancelled(string displayName)
        {
            return new NeteaseDownloadResult
            {
                Status = NeteaseDownloadStatus.Cancelled,
                DisplayName = displayName,
                MessageKey = "Language_Netease_Cancelled"
            };
        }

        private static NeteaseDownloadResult Failure(string displayName, string messageKey)
        {
            return new NeteaseDownloadResult
            {
                Status = NeteaseDownloadStatus.Failed,
                DisplayName = displayName,
                MessageKey = messageKey
            };
        }
    }
}
