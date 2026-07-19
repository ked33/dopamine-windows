using Dopamine.Core.Logging;
using Dopamine.Services.Online.Netease;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.GdMusic
{
    public sealed class GdMusicApiClient : IGdMusicApiClient
    {
        private const string ApiBaseUrl = "https://music-api.gdstudio.xyz/api.php";
        private const int RequestTimeoutSeconds = 20;
        private const int MinimumRequestIntervalMilliseconds = 1200;

        private readonly HttpClient httpClient;
        private readonly SemaphoreSlim requestLock = new SemaphoreSlim(1, 1);
        private readonly Stopwatch requestStopwatch = Stopwatch.StartNew();
        private long lastRequestElapsedMilliseconds = -MinimumRequestIntervalMilliseconds;

        public GdMusicApiClient()
        {
            var httpHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            this.httpClient = new HttpClient(httpHandler)
            {
                Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
            };
            this.httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Dopamine");
        }

        public async Task<NeteaseResult<IReadOnlyList<GdMusicSearchResult>>> SearchAsync(
            string source,
            string keyword,
            int count,
            int page,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return NeteaseResult<IReadOnlyList<GdMusicSearchResult>>.Failure(
                    new NeteaseError(NeteaseErrorCode.EmptyResponse, "Language_GdMusic_Search_Failed"));
            }

            string requestUrl = BuildSearchUrl(source, keyword, count, page);
            NeteaseResult<string> response = await this.GetStringAsync(requestUrl, cancellationToken);
            if (!response.IsSuccess)
            {
                return NeteaseResult<IReadOnlyList<GdMusicSearchResult>>.Failure(response.Error);
            }

            IReadOnlyList<GdMusicSearchResult> results = ParseSearchResults(response.Value);
            if (results == null)
            {
                return NeteaseResult<IReadOnlyList<GdMusicSearchResult>>.Failure(
                    new NeteaseError(NeteaseErrorCode.ApiChanged, "Language_GdMusic_Search_Failed"));
            }

            return NeteaseResult<IReadOnlyList<GdMusicSearchResult>>.Success(results);
        }

        public async Task<NeteaseResult<GdMusicTrackUrl>> GetTrackUrlAsync(
            string source,
            string trackId,
            int bitRate,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                return NeteaseResult<GdMusicTrackUrl>.Failure(
                    new NeteaseError(NeteaseErrorCode.EmptyResponse, "Language_Netease_Download_Source_Unavailable"));
            }

            string requestUrl = BuildTrackUrlRequestUrl(source, trackId, bitRate);
            NeteaseResult<string> response = await this.GetStringAsync(requestUrl, cancellationToken);
            if (!response.IsSuccess)
            {
                return NeteaseResult<GdMusicTrackUrl>.Failure(response.Error);
            }

            GdMusicTrackUrl trackUrl = ParseTrackUrl(response.Value);
            if (trackUrl == null)
            {
                return NeteaseResult<GdMusicTrackUrl>.Failure(
                    new NeteaseError(NeteaseErrorCode.ApiChanged, "Language_Netease_Download_Source_Unavailable"));
            }

            if (string.IsNullOrWhiteSpace(trackUrl.Url))
            {
                return NeteaseResult<GdMusicTrackUrl>.Failure(
                    new NeteaseError(NeteaseErrorCode.EmptyUrl, "Language_Netease_Download_Source_Unavailable"));
            }

            return NeteaseResult<GdMusicTrackUrl>.Success(trackUrl);
        }

        public async Task<NeteaseResult<string>> GetPictureUrlAsync(
            string source,
            string pictureId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pictureId))
            {
                return NeteaseResult<string>.Failure(
                    new NeteaseError(NeteaseErrorCode.EmptyResponse, "Language_GdMusic_Search_Failed"));
            }

            string requestUrl = BuildPictureUrlRequestUrl(source, pictureId);
            NeteaseResult<string> response = await this.GetStringAsync(requestUrl, cancellationToken);
            if (!response.IsSuccess)
            {
                return NeteaseResult<string>.Failure(response.Error);
            }

            string pictureUrl = ParsePictureUrl(response.Value);
            if (string.IsNullOrWhiteSpace(pictureUrl))
            {
                return NeteaseResult<string>.Failure(
                    new NeteaseError(NeteaseErrorCode.EmptyUrl, "Language_GdMusic_Search_Failed"));
            }

            return NeteaseResult<string>.Success(pictureUrl);
        }

        public static string BuildSearchUrl(string source, string keyword, int count, int page)
        {
            return string.Format(
                "{0}?types=search&source={1}&name={2}&count={3}&pages={4}",
                ApiBaseUrl,
                Uri.EscapeDataString(GdMusicSettings.NormalizeSource(source)),
                Uri.EscapeDataString(keyword ?? string.Empty),
                Math.Max(1, count),
                Math.Max(1, page));
        }

        public static string BuildTrackUrlRequestUrl(string source, string trackId, int bitRate)
        {
            return string.Format(
                "{0}?types=url&source={1}&id={2}&br={3}",
                ApiBaseUrl,
                Uri.EscapeDataString(GdMusicSettings.NormalizeSource(source)),
                Uri.EscapeDataString(trackId ?? string.Empty),
                GdMusicSettings.NormalizeQuality(bitRate));
        }

        public static string BuildPictureUrlRequestUrl(string source, string pictureId)
        {
            return string.Format(
                "{0}?types=pic&source={1}&id={2}&size=500",
                ApiBaseUrl,
                Uri.EscapeDataString(GdMusicSettings.NormalizeSource(source)),
                Uri.EscapeDataString(pictureId ?? string.Empty));
        }

        public static IReadOnlyList<GdMusicSearchResult> ParseSearchResults(string json)
        {
            try
            {
                JToken root = JToken.Parse(json ?? string.Empty);
                if (root.Type != JTokenType.Array)
                {
                    return null;
                }

                var results = new List<GdMusicSearchResult>();
                foreach (JToken item in root)
                {
                    if (item == null || item.Type != JTokenType.Object)
                    {
                        continue;
                    }

                    string id = ReadString(item["id"]);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    results.Add(new GdMusicSearchResult
                    {
                        Id = id,
                        Name = ReadString(item["name"]),
                        Artists = ReadStringList(item["artist"]),
                        AlbumName = ReadString(item["album"]),
                        PictureId = ReadString(item["pic_id"]),
                        Source = ReadString(item["source"])
                    });
                }

                return results;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static GdMusicTrackUrl ParseTrackUrl(string json)
        {
            try
            {
                JToken root = JToken.Parse(json ?? string.Empty);
                if (root.Type != JTokenType.Object)
                {
                    return null;
                }

                return new GdMusicTrackUrl
                {
                    Url = ReadString(root["url"]),
                    BitRate = ReadInt32(root["br"]),
                    SizeKilobytes = ReadInt64(root["size"])
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string ParsePictureUrl(string json)
        {
            try
            {
                JToken root = JToken.Parse(json ?? string.Empty);
                if (root.Type != JTokenType.Object)
                {
                    return null;
                }

                return ReadString(root["url"]);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<NeteaseResult<string>> GetStringAsync(
            string requestUrl,
            CancellationToken cancellationToken)
        {
            await this.requestLock.WaitAsync(cancellationToken);
            try
            {
                long sinceLastRequest = this.requestStopwatch.ElapsedMilliseconds -
                    Interlocked.Read(ref this.lastRequestElapsedMilliseconds);
                if (sinceLastRequest >= 0 && sinceLastRequest < MinimumRequestIntervalMilliseconds)
                {
                    await Task.Delay(
                        (int)(MinimumRequestIntervalMilliseconds - sinceLastRequest),
                        cancellationToken);
                }

                Interlocked.Exchange(
                    ref this.lastRequestElapsedMilliseconds,
                    this.requestStopwatch.ElapsedMilliseconds);

                using (HttpResponseMessage response = await this.httpClient.GetAsync(requestUrl, cancellationToken))
                {
                    if ((int)response.StatusCode == 429)
                    {
                        return NeteaseResult<string>.Failure(
                            new NeteaseError(NeteaseErrorCode.RateLimited, "Language_GdMusic_Rate_Limited"));
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return NeteaseResult<string>.Failure(new NeteaseError(
                            NeteaseErrorCode.NetworkUnavailable,
                            "Language_GdMusic_Search_Failed",
                            (int)response.StatusCode));
                    }

                    string content = await response.Content.ReadAsStringAsync();
                    return NeteaseResult<string>.Success(content);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return NeteaseResult<string>.Failure(
                    new NeteaseError(NeteaseErrorCode.Cancelled, "Language_Netease_Cancelled"));
            }
            catch (OperationCanceledException)
            {
                // HttpClient timeout surfaces as TaskCanceledException without user cancellation.
                return NeteaseResult<string>.Failure(
                    new NeteaseError(NeteaseErrorCode.NetworkUnavailable, "Language_GdMusic_Search_Failed"));
            }
            catch (HttpRequestException ex)
            {
                AppLog.Warning("GD music API request failed. ErrorType={0}", ex.GetType().Name);
                return NeteaseResult<string>.Failure(
                    new NeteaseError(NeteaseErrorCode.NetworkUnavailable, "Language_GdMusic_Search_Failed"));
            }
            catch (Exception ex)
            {
                AppLog.Warning("GD music API request failed unexpectedly. ErrorType={0}", ex.GetType().Name);
                return NeteaseResult<string>.Failure(
                    new NeteaseError(NeteaseErrorCode.Unknown, "Language_GdMusic_Search_Failed"));
            }
            finally
            {
                this.requestLock.Release();
            }
        }

        private static string ReadString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            if (token.Type == JTokenType.Array)
            {
                IReadOnlyList<string> values = ReadStringList(token);
                return string.Join("、", values);
            }

            return token.ToString().Trim();
        }

        private static IReadOnlyList<string> ReadStringList(JToken token)
        {
            var values = new List<string>();
            if (token == null)
            {
                return values;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (JToken item in token)
                {
                    string value = item == null || item.Type == JTokenType.Null
                        ? string.Empty
                        : item.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }
            }
            else if (token.Type != JTokenType.Null)
            {
                string single = token.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(single))
                {
                    values.Add(single);
                }
            }

            return values;
        }

        private static int ReadInt32(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            int value;
            return int.TryParse(token.ToString().Trim(), out value) ? value : 0;
        }

        private static long ReadInt64(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            long value;
            return long.TryParse(token.ToString().Trim(), out value) ? value : 0;
        }
    }
}
