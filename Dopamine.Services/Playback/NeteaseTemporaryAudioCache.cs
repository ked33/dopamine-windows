using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Logging;
using Dopamine.Services.Online.Netease;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class NeteaseTemporaryAudioCache
    {
        private const long MaximumCacheBytes = 512L * 1024L * 1024L;
        private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);

        private readonly HttpClient httpClient = new HttpClient();
        private readonly string cacheDirectory;
        private long sessionGeneration;

        public NeteaseTemporaryAudioCache(INeteaseSessionService sessionService)
        {
            this.cacheDirectory = Path.Combine(SettingsClient.ApplicationFolder(), "Cache", "Temporary", "Netease");
            this.sessionGeneration = sessionService.SessionGeneration;
            this.TryCleanup();
            sessionService.SessionChanged += (_, __) =>
            {
                long currentGeneration = sessionService.SessionGeneration;
                bool generationChanged = currentGeneration != Interlocked.Read(ref this.sessionGeneration);

                if (generationChanged)
                {
                    Interlocked.Exchange(ref this.sessionGeneration, currentGeneration);
                }

                if (generationChanged || sessionService.State == NeteaseSessionState.SignedOut ||
                    sessionService.State == NeteaseSessionState.Expired)
                {
                    this.Clear();
                }
            };
        }

        public async Task<NeteaseResult<string>> GetOrDownloadAsync(
            string songId,
            string url,
            string mediaType,
            CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(this.cacheDirectory);
                string finalPath = this.GetFinalPath(songId, mediaType, url);

                if (File.Exists(finalPath))
                {
                    var existing = new FileInfo(finalPath);

                    if (existing.Length > 0 && DateTime.UtcNow - existing.LastWriteTimeUtc <= MaximumAge)
                    {
                        existing.LastAccessTimeUtc = DateTime.UtcNow;
                        return NeteaseResult<string>.Success(finalPath);
                    }

                    TryDelete(finalPath);
                }

                string partialPath = Path.Combine(this.cacheDirectory, Guid.NewGuid().ToString("N") + ".part");

                try
                {
                    using (HttpResponseMessage response = await this.httpClient.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        if (response.Content.Headers.ContentLength.HasValue &&
                            response.Content.Headers.ContentLength.Value > MaximumCacheBytes)
                        {
                            return Failure();
                        }

                        using (Stream source = await response.Content.ReadAsStreamAsync())
                        using (var destination = new FileStream(
                            partialPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            81920,
                            true))
                        {
                            var buffer = new byte[81920];
                            long totalBytes = 0;
                            int bytesRead;

                            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                totalBytes += bytesRead;

                                if (totalBytes > MaximumCacheBytes)
                                {
                                    return Failure();
                                }

                                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            }

                            await destination.FlushAsync(cancellationToken);
                        }
                    }

                    if (File.Exists(finalPath))
                    {
                        TryDelete(finalPath);
                    }

                    File.Move(partialPath, finalPath);
                    this.TryCleanup();
                    return NeteaseResult<string>.Success(finalPath);
                }
                finally
                {
                    TryDelete(partialPath);
                }
            }
            catch (OperationCanceledException)
            {
                return NeteaseResult<string>.Failure(new NeteaseError(
                    NeteaseErrorCode.Cancelled,
                    "Language_Netease_Cancelled"));
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not prepare a temporary Netease audio file. ErrorType={0}", ex.GetType().Name);
                return Failure();
            }
        }

        public void Invalidate(string songId)
        {
            if (!Directory.Exists(this.cacheDirectory))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(this.cacheDirectory, SanitizeSongId(songId) + ".*"))
            {
                TryDelete(path);
            }
        }

        public void Clear()
        {
            try
            {
                if (!Directory.Exists(this.cacheDirectory))
                {
                    return;
                }

                foreach (string path in Directory.GetFiles(this.cacheDirectory))
                {
                    TryDelete(path);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not clear the Netease temporary audio cache. ErrorType={0}", ex.GetType().Name);
            }
        }

        private string GetFinalPath(string songId, string mediaType, string url)
        {
            string extension = NormalizeExtension(mediaType, url);
            return Path.Combine(this.cacheDirectory, SanitizeSongId(songId) + extension);
        }

        private void TryCleanup()
        {
            try
            {
                if (!Directory.Exists(this.cacheDirectory))
                {
                    return;
                }

                DateTime cutoff = DateTime.UtcNow.Subtract(MaximumAge);
                FileInfo[] files = new DirectoryInfo(this.cacheDirectory)
                    .GetFiles()
                    .OrderBy(x => x.LastAccessTimeUtc)
                    .ToArray();

                foreach (FileInfo file in files.Where(x => x.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase) || x.LastWriteTimeUtc < cutoff))
                {
                    TryDelete(file.FullName);
                }

                files = new DirectoryInfo(this.cacheDirectory)
                    .GetFiles()
                    .Where(x => !x.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.LastAccessTimeUtc)
                    .ToArray();

                long totalBytes = files.Sum(x => x.Length);

                foreach (FileInfo file in files)
                {
                    if (totalBytes <= MaximumCacheBytes)
                    {
                        break;
                    }

                    totalBytes -= file.Length;
                    TryDelete(file.FullName);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("Could not clean the Netease temporary audio cache. ErrorType={0}", ex.GetType().Name);
            }
        }

        private static string NormalizeExtension(string mediaType, string url)
        {
            string extension = string.IsNullOrWhiteSpace(mediaType)
                ? string.Empty
                : "." + mediaType.Trim().TrimStart('.').ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
            {
                Uri uri;
                extension = Uri.TryCreate(url, UriKind.Absolute, out uri)
                    ? Path.GetExtension(uri.AbsolutePath).ToLowerInvariant()
                    : string.Empty;
            }

            switch (extension)
            {
                case ".mp3":
                case ".flac":
                case ".m4a":
                case ".aac":
                case ".wav":
                case ".ogg":
                case ".opus":
                case ".wma":
                    return extension;
                default:
                    return ".audio";
            }
        }

        private static string SanitizeSongId(string songId)
        {
            return new string((songId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        }

        private static NeteaseResult<string> Failure()
        {
            return NeteaseResult<string>.Failure(new NeteaseError(
                NeteaseErrorCode.TemporaryDownloadFailed,
                "Language_Netease_Temporary_Download_Failed"));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
