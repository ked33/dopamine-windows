using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Logging;
using Dopamine.Services.Online.Netease;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class NeteaseTemporaryAudioCache
    {
        private const long MaximumCacheBytes = 512L * 1024L * 1024L;
        private const int MaximumRedirects = 5;
        private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);

        private readonly HttpClient httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });
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
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(this.cacheDirectory);
                string finalPath = this.GetFinalPath(songId, mediaType, url);

                if (System.IO.File.Exists(finalPath))
                {
                    var existing = new FileInfo(finalPath);

                    if (existing.Length > 0 && DateTime.UtcNow - existing.LastWriteTimeUtc <= MaximumAge)
                    {
                        existing.LastAccessTimeUtc = DateTime.UtcNow;
                        progress?.Report(1.0);
                        return NeteaseResult<string>.Success(finalPath);
                    }

                    TryDelete(finalPath);
                }

                string partialPath = Path.Combine(this.cacheDirectory, Guid.NewGuid().ToString("N") + ".part");

                try
                {
                    using (HttpResponseMessage response = await this.GetValidatedResponseAsync(url, cancellationToken))
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
                            long? contentLength = response.Content.Headers.ContentLength;
                            double lastReportedProgress = -1.0;
                            int bytesRead;

                            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                totalBytes += bytesRead;

                                if (totalBytes > MaximumCacheBytes)
                                {
                                    return Failure();
                                }

                                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                                if (contentLength.HasValue && contentLength.Value > 0)
                                {
                                    double currentProgress = Math.Min(
                                        1.0,
                                        (double)totalBytes / contentLength.Value);

                                    if (currentProgress >= 1.0 || currentProgress - lastReportedProgress >= 0.005)
                                    {
                                        lastReportedProgress = currentProgress;
                                        progress?.Report(currentProgress);
                                    }
                                }
                            }

                            await destination.FlushAsync(cancellationToken);
                        }
                    }

                    if (!System.IO.File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
                    {
                        return Failure();
                    }

                    if (System.IO.File.Exists(finalPath))
                    {
                        TryDelete(finalPath);
                    }

                    System.IO.File.Move(partialPath, finalPath);
                    progress?.Report(1.0);
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

            string sanitizedSongId = SanitizeSongId(songId);
            if (string.IsNullOrEmpty(sanitizedSongId))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(this.cacheDirectory)
                .Where(path =>
                {
                    string cacheKey = Path.GetFileNameWithoutExtension(path);
                    return cacheKey.Equals(sanitizedSongId, StringComparison.OrdinalIgnoreCase) ||
                        cacheKey.EndsWith("-" + sanitizedSongId, StringComparison.OrdinalIgnoreCase);
                }))
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

        private async Task<HttpResponseMessage> GetValidatedResponseAsync(
            string url,
            CancellationToken cancellationToken)
        {
            Uri currentUri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out currentUri))
            {
                throw new HttpRequestException("Invalid audio URL.");
            }

            for (int redirectCount = 0; redirectCount <= MaximumRedirects; redirectCount++)
            {
                await ValidatePublicHttpUriAsync(currentUri, cancellationToken);
                HttpResponseMessage response = await this.httpClient.GetAsync(
                    currentUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!IsRedirect(response.StatusCode))
                {
                    try
                    {
                        response.EnsureSuccessStatusCode();
                        return response;
                    }
                    catch
                    {
                        response.Dispose();
                        throw;
                    }
                }

                Uri location = response.Headers.Location;
                response.Dispose();
                if (location == null || redirectCount == MaximumRedirects)
                {
                    throw new HttpRequestException("Invalid audio redirect.");
                }

                currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            }

            throw new HttpRequestException("Too many audio redirects.");
        }

        private static async Task ValidatePublicHttpUriAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (uri == null ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                uri.IsLoopback)
            {
                throw new HttpRequestException("Unsafe audio URL.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            IPAddress literalAddress;
            IPAddress[] addresses = IPAddress.TryParse(uri.Host, out literalAddress)
                ? new[] { literalAddress }
                : await Dns.GetHostAddressesAsync(uri.DnsSafeHost);
            cancellationToken.ThrowIfCancellationRequested();

            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            {
                throw new HttpRequestException("Audio URL resolved to a non-public address.");
            }
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            if (address == null || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
                address.Equals(IPAddress.IPv6None))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return !(
                    bytes[0] == 0 ||
                    bytes[0] == 10 ||
                    bytes[0] == 127 ||
                    (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                    (bytes[0] == 169 && bytes[1] == 254) ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) ||
                    (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                    (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) ||
                    (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) ||
                    bytes[0] >= 224);
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return !address.IsIPv6LinkLocal &&
                    !address.IsIPv6Multicast &&
                    !address.IsIPv6SiteLocal &&
                    (bytes[0] & 0xfe) != 0xfc;
            }

            return false;
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
            return new string((songId ?? string.Empty)
                .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
                .ToArray());
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
                if (System.IO.File.Exists(path))
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
