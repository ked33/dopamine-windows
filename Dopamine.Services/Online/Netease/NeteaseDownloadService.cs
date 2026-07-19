using Dopamine.Core.Logging;
using Dopamine.Data;
using Dopamine.Data.Metadata;
using Dopamine.Services.Entities;
using Dopamine.Services.Playback;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public sealed class NeteaseDownloadService : INeteaseDownloadService
    {
        private const long MaximumArtworkBytes = 10L * 1024L * 1024L;
        private const int MaximumFileNameLength = 180;
        private const int MaximumLegacyPathLength = 259;
        private const int MaximumSupportedExtensionLength = 5;
        private const int TemporaryFileNameLength = 43;
        internal static readonly string[] SupportedExtensions =
        {
            ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma"
        };

        private readonly object syncRoot = new object();
        private readonly Dictionary<string, Task<NeteaseDownloadResult>> activeDownloads =
            new Dictionary<string, Task<NeteaseDownloadResult>>(StringComparer.Ordinal);
        private readonly NeteaseAudioSourceResolver audioSourceResolver;
        private readonly NeteaseTemporaryAudioCache temporaryAudioCache;

        public NeteaseDownloadService(
            NeteaseAudioSourceResolver audioSourceResolver,
            NeteaseTemporaryAudioCache temporaryAudioCache)
        {
            this.audioSourceResolver = audioSourceResolver;
            this.temporaryAudioCache = temporaryAudioCache;
        }

        public event EventHandler<NeteaseDownloadStateChangedEventArgs> DownloadStateChanged = delegate { };

        public bool IsDownloading(string songId)
        {
            if (string.IsNullOrWhiteSpace(songId))
            {
                return false;
            }

            lock (this.syncRoot)
            {
                return this.activeDownloads.ContainsKey(songId);
            }
        }

        public Task<NeteaseDownloadResult> DownloadAsync(
            TrackViewModel track,
            CancellationToken cancellationToken)
        {
            string songId = track?.SourceInfo?.RemoteId;
            if (track?.SourceInfo == null || track.SourceInfo.Kind != TrackSourceKind.Netease ||
                string.IsNullOrWhiteSpace(songId))
            {
                return Task.FromResult(Failure(
                    GetDisplayName(track),
                    "Language_Netease_Download_Failed"));
            }

            Task<NeteaseDownloadResult> downloadTask;
            lock (this.syncRoot)
            {
                if (this.activeDownloads.TryGetValue(songId, out downloadTask))
                {
                    return downloadTask;
                }

                downloadTask = Task.Run(() => this.DownloadCoreAsync(track, cancellationToken));
                this.activeDownloads.Add(songId, downloadTask);
            }

            this.DownloadStateChanged(
                this,
                new NeteaseDownloadStateChangedEventArgs(songId, true));
            downloadTask.ContinueWith(
                _ => this.CompleteDownload(songId, downloadTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return downloadTask;
        }

        private async Task<NeteaseDownloadResult> DownloadCoreAsync(
            TrackViewModel track,
            CancellationToken cancellationToken)
        {
            string displayName = GetDisplayName(track);
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
                string fileNameBase = CreateSafeFileNameBase(track, downloadDirectory);
                if (string.IsNullOrWhiteSpace(fileNameBase))
                {
                    return Failure(displayName, "Language_Netease_Download_Failed");
                }

                string existingPath = FindExistingPath(downloadDirectory, fileNameBase);
                if (!string.IsNullOrWhiteSpace(existingPath))
                {
                    return AlreadyExists(displayName, existingPath);
                }

                OnlineAudioSourcePriority preferredPriority = NeteaseDownloadSettings.SourcePriority;
                OnlineAudioSourcePriority alternatePriority = preferredPriority == OnlineAudioSourcePriority.UnblockFirst
                    ? OnlineAudioSourcePriority.OfficialFirst
                    : OnlineAudioSourcePriority.UnblockFirst;
                var attemptedSources = new HashSet<string>(StringComparer.Ordinal);
                NeteaseAudioSourceResolution source = null;
                NeteaseResult<string> cached = null;
                string extension = null;
                string failureMessageKey = "Language_Netease_Download_Source_Unavailable";

                foreach (OnlineAudioSourcePriority priority in new[] { preferredPriority, alternatePriority })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    NeteaseAudioSourceResolution candidate = await this.audioSourceResolver.ResolveAsync(
                        track,
                        priority,
                        priority != preferredPriority,
                        cancellationToken);
                    if (candidate == null || !candidate.IsSuccess)
                    {
                        if (candidate?.Error?.Code == NeteaseErrorCode.Cancelled)
                        {
                            return Cancelled(displayName);
                        }

                        failureMessageKey = candidate?.Error?.MessageKey ?? failureMessageKey;
                        continue;
                    }

                    string candidateExtension = NeteaseTemporaryAudioCache.NormalizeExtension(
                        candidate.MediaType,
                        candidate.Url);
                    if (string.Equals(candidateExtension, ".audio", StringComparison.OrdinalIgnoreCase))
                    {
                        failureMessageKey = "Language_Netease_Download_Unsupported_Format";
                        continue;
                    }

                    string sourceIdentity = (candidate.CacheKey ?? string.Empty) + "\n" + candidate.Url;
                    if (string.IsNullOrWhiteSpace(candidate.CacheKey) ||
                        !attemptedSources.Add(sourceIdentity))
                    {
                        continue;
                    }

                    NeteaseResult<string> candidateCached = await this.temporaryAudioCache.GetOrDownloadAsync(
                        candidate.CacheKey,
                        candidate.Url,
                        candidate.MediaType,
                        null,
                        cancellationToken);
                    if (candidateCached.IsSuccess)
                    {
                        source = candidate;
                        cached = candidateCached;
                        extension = candidateExtension;
                        break;
                    }

                    if (candidateCached.Error?.Code == NeteaseErrorCode.Cancelled)
                    {
                        return Cancelled(displayName);
                    }

                    failureMessageKey = candidateCached.Error?.MessageKey ??
                        "Language_Netease_Download_Failed";
                }

                if (source == null || cached == null || !cached.IsSuccess ||
                    string.IsNullOrWhiteSpace(extension))
                {
                    return Failure(displayName, failureMessageKey);
                }

                string finalPath = Path.Combine(downloadDirectory, fileNameBase + extension);
                if (System.IO.File.Exists(finalPath))
                {
                    return AlreadyExists(displayName, finalPath);
                }

                partialPath = Path.Combine(
                    downloadDirectory,
                    "." + Guid.NewGuid().ToString("N") + ".part" + extension);
                await CopyFileAsync(cached.Value, partialPath, cancellationToken);

                bool metadataComplete = true;
                byte[] artwork = null;
                if (!string.IsNullOrWhiteSpace(track.SourceInfo.ArtworkUrl))
                {
                    NeteaseResult<byte[]> artworkResult = await this.temporaryAudioCache.DownloadBytesAsync(
                        track.SourceInfo.ArtworkUrl,
                        MaximumArtworkBytes,
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (artworkResult.IsSuccess)
                    {
                        artwork = artworkResult.Value;
                    }
                    else
                    {
                        metadataComplete = false;
                    }
                }
                else
                {
                    metadataComplete = false;
                }

                try
                {
                    WriteMetadata(partialPath, track, artwork);
                }
                catch (Exception ex)
                {
                    metadataComplete = false;
                    AppLog.Warning(
                        "Could not write metadata to a downloaded Netease track. ErrorType={0}",
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
                    ProviderId = source.ProviderId
                };
            }
            catch (OperationCanceledException)
            {
                return Cancelled(displayName);
            }
            catch (IOException ex)
            {
                AppLog.Warning(
                    "Could not save a downloaded Netease track. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Directory_Not_Writable");
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Warning(
                    "Could not write to the Netease download directory. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Directory_Not_Writable");
            }
            catch (SecurityException ex)
            {
                AppLog.Warning(
                    "Could not access the Netease download directory. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Directory_Not_Writable");
            }
            catch (Exception ex)
            {
                AppLog.Warning(
                    "Could not download a Netease track. ErrorType={0}",
                    ex.GetType().Name);
                return Failure(displayName, "Language_Netease_Download_Failed");
            }
            finally
            {
                TryDelete(partialPath);
            }
        }

        private void CompleteDownload(string songId, Task<NeteaseDownloadResult> completedTask)
        {
            bool removed = false;
            lock (this.syncRoot)
            {
                Task<NeteaseDownloadResult> current;
                if (this.activeDownloads.TryGetValue(songId, out current) &&
                    object.ReferenceEquals(current, completedTask))
                {
                    removed = this.activeDownloads.Remove(songId);
                }
            }

            if (removed)
            {
                this.DownloadStateChanged(
                    this,
                    new NeteaseDownloadStateChangedEventArgs(songId, false));
            }
        }

        public static string CreateSafeFileNameBase(TrackViewModel track, string directory)
        {
            string songId = track?.SourceInfo?.RemoteId ?? "netease";
            string artist = string.IsNullOrWhiteSpace(track?.ArtistName) ? songId : track.ArtistName;
            string title = string.IsNullOrWhiteSpace(track?.TrackTitle) ? songId : track.TrackTitle;
            return CreateSafeFileNameBase(artist, title, songId, directory);
        }

        public static string CreateSafeFileNameBase(
            string artist,
            string title,
            string songId,
            string directory)
        {
            songId = string.IsNullOrWhiteSpace(songId) ? "netease" : songId;
            artist = string.IsNullOrWhiteSpace(artist) ? songId : artist;
            title = string.IsNullOrWhiteSpace(title) ? songId : title;
            string value = SanitizeFileName(artist) + " - " + SanitizeFileName(title);
            value = value.Trim().TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(value) || value == "-")
            {
                value = SanitizeFileName(songId);
            }

            int pathAllowance = MaximumFileNameLength;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                string fullDirectory = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullDirectory.Length + 1 + TemporaryFileNameLength > MaximumLegacyPathLength)
                {
                    return string.Empty;
                }

                pathAllowance = Math.Min(
                    MaximumFileNameLength,
                    MaximumLegacyPathLength - fullDirectory.Length - 1 - MaximumSupportedExtensionLength);
            }

            if (pathAllowance < 24)
            {
                return string.Empty;
            }

            if (value.Length > pathAllowance)
            {
                value = value.Substring(0, pathAllowance).Trim().TrimEnd(' ', '.');
            }

            return value;
        }

        private static string SanitizeFileName(string value)
        {
            var invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string((value ?? string.Empty)
                .Select(character => invalidCharacters.Contains(character) || char.IsControl(character)
                    ? '_'
                    : character)
                .ToArray());
        }

        internal static string GetDisplayName(TrackViewModel track)
        {
            string songId = track?.SourceInfo?.RemoteId ?? "netease";
            string artist = string.IsNullOrWhiteSpace(track?.ArtistName) ? songId : track.ArtistName;
            string title = string.IsNullOrWhiteSpace(track?.TrackTitle) ? songId : track.TrackTitle;
            return artist + " - " + title;
        }

        internal static string FindExistingPath(string directory, string fileNameBase)
        {
            foreach (string extension in SupportedExtensions)
            {
                string path = Path.Combine(directory, fileNameBase + extension);
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        internal static async Task CopyFileAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                true))
            using (var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                true))
            {
                await source.CopyToAsync(destination, 81920, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
        }

        internal static void WriteMetadata(string path, TrackViewModel track, byte[] artwork)
        {
            var metadata = new FileMetadata(path);

            var title = new MetadataValue { Value = track.TrackTitle ?? string.Empty };
            metadata.Title = title;

            string[] artists = string.IsNullOrWhiteSpace(track.Track.Artists)
                ? new string[0]
                : DataUtils.SplitAndTrimColumnMultiValue(track.Track.Artists).ToArray();
            var artistValue = new MetadataValue { Values = artists };
            metadata.Artists = artistValue;

            string[] albumArtists = string.IsNullOrWhiteSpace(track.Track.AlbumArtists)
                ? artists
                : DataUtils.SplitAndTrimColumnMultiValue(track.Track.AlbumArtists).ToArray();
            var albumArtistValue = new MetadataValue { Values = albumArtists };
            metadata.AlbumArtists = albumArtistValue;

            var album = new MetadataValue { Value = track.Track.AlbumTitle ?? string.Empty };
            metadata.Album = album;

            if (artwork != null && artwork.Length > 0)
            {
                var artworkValue = new MetadataArtworkValue { Value = artwork };
                metadata.ArtworkData = artworkValue;
            }

            metadata.Save();
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

        internal static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
