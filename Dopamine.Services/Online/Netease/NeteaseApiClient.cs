using Digimezzo.Foundation.Core.Logging;
using Dopamine.Core.Logging;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.NeteaseApi.Bases;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public sealed class NeteaseApiClient : INeteaseApiClient
    {
        private readonly object cookieLock = new object();
        private readonly HttpClient httpClient;
        private readonly NeteaseCloudMusicApiHandler handler;

        public NeteaseApiClient()
        {
            var httpHandler = new HttpClientHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            this.httpClient = new HttpClient(httpHandler);
            this.handler = new NeteaseCloudMusicApiHandler(this.httpClient);
            NeteaseWebLoginSerializer.EnableCustomContracts(this.handler.Option);
        }

        public async Task<NeteaseResult<NeteaseQrKey>> CreateQrKeyAsync(CancellationToken cancellationToken)
        {
            const string method = "NeteaseWebQrKeyApi";

            try
            {
                string sDeviceId = NeteaseLoginContext.CreateSDeviceId();
                this.ReplaceCookies(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sDeviceId"] = sDeviceId,
                    ["NMTID"] = NeteaseLoginContext.CreateNmtid()
                });

                var result = await this.handler.RequestAsync(new NeteaseWebQrKeyApi(), cancellationToken);

                if (result.IsError)
                {
                    return NeteaseResult<NeteaseQrKey>.Failure(this.MapError(method, result.Error, 0, cancellationToken));
                }

                if (result.Value == null || result.Value.Code != 200 || string.IsNullOrWhiteSpace(result.Value.Unikey))
                {
                    return NeteaseResult<NeteaseQrKey>.Failure(this.MapResponseError(method, result.Value?.Code ?? 0));
                }

                string chainId = NeteaseLoginContext.CreateChainId(
                    sDeviceId,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                return NeteaseResult<NeteaseQrKey>.Success(new NeteaseQrKey
                {
                    Unikey = result.Value.Unikey,
                    ChainId = chainId,
                    QrContent = NeteaseLoginContext.BuildQrContent(result.Value.Unikey, chainId)
                });
            }
            catch (Exception ex)
            {
                return NeteaseResult<NeteaseQrKey>.Failure(this.MapError(method, ex, 0, cancellationToken));
            }
        }

        public async Task<NeteaseResult<NeteaseQrCheck>> CheckQrAsync(
            NeteaseQrSession session,
            CancellationToken cancellationToken)
        {
            const string method = "NeteaseWebQrCheckApi";

            if (session == null || string.IsNullOrWhiteSpace(session.Unikey) ||
                string.IsNullOrWhiteSpace(session.ChainId))
            {
                return NeteaseResult<NeteaseQrCheck>.Failure(new NeteaseError(
                    NeteaseErrorCode.ApiChanged,
                    "Language_Netease_Service_Unavailable"));
            }

            try
            {
                var request = new NeteaseWebQrCheckRequest
                {
                    Unikey = session.Unikey,
                    ChainId = session.ChainId,
                    YdDeviceToken = session.YdDeviceToken ?? string.Empty
                };
                var result = await this.handler.RequestAsync(new NeteaseWebQrCheckApi(), request, cancellationToken);

                if (result.IsError)
                {
                    if (NeteaseQrStatusMapper.TryGetStatusCode(result.Error, out int statusCode))
                    {
                        return NeteaseResult<NeteaseQrCheck>.Success(new NeteaseQrCheck { Code = statusCode });
                    }

                    return NeteaseResult<NeteaseQrCheck>.Failure(this.MapError(method, result.Error, 0, cancellationToken));
                }

                if (result.Value == null)
                {
                    return NeteaseResult<NeteaseQrCheck>.Failure(new NeteaseError(
                        NeteaseErrorCode.EmptyResponse,
                        "Language_Netease_Service_Unavailable"));
                }

                return NeteaseResult<NeteaseQrCheck>.Success(new NeteaseQrCheck { Code = result.Value.Code });
            }
            catch (Exception ex)
            {
                return NeteaseResult<NeteaseQrCheck>.Failure(this.MapError(method, ex, 0, cancellationToken));
            }
        }

        public async Task<NeteaseResult<NeteaseAccountProfile>> GetLoginStatusAsync(CancellationToken cancellationToken)
        {
            const string method = "NeteaseWebLoginStatusApi";

            try
            {
                var result = await this.handler.RequestAsync(new NeteaseWebLoginStatusApi(), cancellationToken);
                int responseCode = result.Value?.Code ?? result.Error?.ErrorCode ?? 0;
                bool hasProfile = result.Value?.Profile != null;
                bool hasAccount = result.Value?.Account != null;

                AppLog.InfoAlways(
                    "Netease login status completed. ResponseCode={0}, HasProfile={1}, HasAccount={2}",
                    responseCode,
                    hasProfile,
                    hasAccount);

                if (result.IsError)
                {
                    return NeteaseResult<NeteaseAccountProfile>.Failure(
                        this.MapError(method, result.Error, responseCode, cancellationToken));
                }

                if (result.Value == null)
                {
                    return NeteaseResult<NeteaseAccountProfile>.Failure(new NeteaseError(
                        NeteaseErrorCode.EmptyResponse,
                        "Language_Netease_Service_Unavailable"));
                }

                if (result.Value.Code == 301 || result.Value.Code == 401 ||
                    (result.Value.Profile == null && result.Value.Account == null))
                {
                    return NeteaseResult<NeteaseAccountProfile>.Failure(new NeteaseError(
                        NeteaseErrorCode.AuthenticationRequired,
                        "Language_Netease_Login_Expired",
                        result.Value.Code));
                }

                if (result.Value.Code != 200)
                {
                    return NeteaseResult<NeteaseAccountProfile>.Failure(this.MapResponseError(method, result.Value.Code));
                }

                return NeteaseResult<NeteaseAccountProfile>.Success(new NeteaseAccountProfile
                {
                    UserId = result.Value.Profile?.UserId ?? result.Value.Account?.Id ?? string.Empty,
                    Nickname = result.Value.Profile?.Nickname ?? result.Value.Account?.UserName ?? string.Empty,
                    VipType = result.Value.Profile?.VipType ?? result.Value.Account?.VipType ?? 0
                });
            }
            catch (Exception ex)
            {
                return NeteaseResult<NeteaseAccountProfile>.Failure(this.MapError(method, ex, 0, cancellationToken));
            }
        }

        public async Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> GetDailyRecommendationsAsync(CancellationToken cancellationToken)
        {
            const string method = "RecommendSongsApi";

            try
            {
                var result = await this.handler.RequestAsync(NeteaseApis.RecommendSongsApi, cancellationToken);

                if (result.IsError)
                {
                    return NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Failure(this.MapError(method, result.Error, 0, cancellationToken));
                }

                if (result.Value == null || result.Value.Code != 200)
                {
                    return NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Failure(this.MapResponseError(method, result.Value?.Code ?? 0));
                }

                var songs = new List<NeteaseRecommendedSong>();
                var sourceSongs = result.Value.Data?.DailySongs;

                if (sourceSongs != null)
                {
                    foreach (var song in sourceSongs.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id)))
                    {
                        songs.Add(new NeteaseRecommendedSong
                        {
                            Id = song.Id,
                            Name = song.Name ?? string.Empty,
                            Artists = (song.Artists ?? Array.Empty<HyPlayer.NeteaseApi.Models.ResponseModels.ArtistDto>())
                                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                                .Select(x => x.Name)
                                .ToList(),
                            AlbumId = song.Album?.Id ?? string.Empty,
                            AlbumName = song.Album?.Name ?? string.Empty,
                            DurationMilliseconds = song.Duration,
                            ArtworkUrl = song.Album?.PictureUrl,
                            IsKnownUnavailable = song.Privilege != null && song.Privilege.St < 0
                        });
                    }
                }

                return NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Success(songs);
            }
            catch (Exception ex)
            {
                return NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Failure(this.MapError(method, ex, 0, cancellationToken));
            }
        }

        public async Task<NeteaseAudioResolution> GetSongUrlAsync(string songId, string level, CancellationToken cancellationToken)
        {
            const string method = "SongUrlApi";

            if (string.IsNullOrWhiteSpace(songId))
            {
                return this.AudioFailure(songId, NeteaseErrorCode.ApiChanged, "Language_Netease_Service_Unavailable");
            }

            try
            {
                var request = new SongUrlRequest { Id = songId, Level = level };
                var result = await this.handler.RequestAsync(NeteaseApis.SongUrlApi, request, cancellationToken);

                if (result.IsError)
                {
                    return new NeteaseAudioResolution
                    {
                        SongId = songId,
                        Error = this.MapError(method, result.Error, 0, cancellationToken)
                    };
                }

                if (result.Value == null || result.Value.Code != 200)
                {
                    return new NeteaseAudioResolution
                    {
                        SongId = songId,
                        Error = this.MapResponseError(method, result.Value?.Code ?? 0)
                    };
                }

                var item = result.Value.SongUrls?.FirstOrDefault();

                if (item == null)
                {
                    return this.AudioFailure(songId, NeteaseErrorCode.EmptyResponse, "Language_Netease_Service_Unavailable");
                }

                if (item.FreeTrialInfo != null)
                {
                    return this.AudioFailure(songId, NeteaseErrorCode.TrialOnly, "Language_Netease_Trial_Not_Supported", item.Code);
                }

                if (item.Code == 401 || item.Code == 301)
                {
                    return this.AudioFailure(songId, NeteaseErrorCode.SessionExpired, "Language_Netease_Login_Expired", item.Code);
                }

                if (item.Code == 403)
                {
                    return this.AudioFailure(songId, NeteaseErrorCode.SubscriptionRequired, "Language_Netease_Subscription_Required", item.Code);
                }

                if (item.Code != 200)
                {
                    return this.AudioFailure(songId, NeteaseErrorCode.NoCopyright, "Language_Netease_No_Copyright", item.Code);
                }

                if (string.IsNullOrWhiteSpace(item.Url))
                {
                    return this.AudioFailure(songId, NeteaseErrorCode.EmptyUrl, "Language_Netease_No_Copyright", item.Code);
                }

                string url = this.PreferHttps(item.Url);
                long bitRate = 0;
                long.TryParse(item.BitRate, out bitRate);

                return new NeteaseAudioResolution
                {
                    IsSuccess = true,
                    SongId = songId,
                    Url = url,
                    Type = item.Type ?? item.EncodeType ?? string.Empty,
                    BitRate = bitRate,
                    Size = item.Size
                };
            }
            catch (Exception ex)
            {
                return new NeteaseAudioResolution
                {
                    SongId = songId,
                    Error = this.MapError(method, ex, 0, cancellationToken)
                };
            }
        }

        public async Task<NeteaseLyricResult> GetLyricsAsync(string songId, CancellationToken cancellationToken)
        {
            const string method = "LyricApi";

            if (string.IsNullOrWhiteSpace(songId))
            {
                return new NeteaseLyricResult
                {
                    Error = new NeteaseError(NeteaseErrorCode.ApiChanged, "Language_Netease_Service_Unavailable")
                };
            }

            try
            {
                var request = new LyricRequest { Id = songId };
                var result = await this.handler.RequestAsync(NeteaseApis.LyricApi, request, cancellationToken);

                if (result.IsError)
                {
                    return new NeteaseLyricResult { Error = this.MapError(method, result.Error, 0, cancellationToken) };
                }

                if (result.Value == null || result.Value.Code != 200)
                {
                    return new NeteaseLyricResult { Error = this.MapResponseError(method, result.Value?.Code ?? 0) };
                }

                return new NeteaseLyricResult
                {
                    IsSuccess = true,
                    Lyric = result.Value.Lyric?.Lyric ?? string.Empty,
                    TranslationLyric = result.Value.TranslationLyric?.Lyric ?? string.Empty,
                    RomajiLyric = result.Value.RomajiLyric?.Lyric ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                return new NeteaseLyricResult { Error = this.MapError(method, ex, 0, cancellationToken) };
            }
        }

        public void ReplaceCookies(IReadOnlyDictionary<string, string> cookies)
        {
            lock (this.cookieLock)
            {
                Dictionary<string, string> normalized = NeteaseLoginContext.NormalizeCookies(cookies);
                this.handler.Option.Cookies.Clear();

                foreach (var cookie in normalized)
                {
                    this.handler.Option.Cookies[cookie.Key] = cookie.Value;
                }
            }
        }

        public IReadOnlyDictionary<string, string> SnapshotCookies()
        {
            lock (this.cookieLock)
            {
                return new Dictionary<string, string>(this.handler.Option.Cookies, StringComparer.OrdinalIgnoreCase);
            }
        }

        public void ClearCookies()
        {
            lock (this.cookieLock)
            {
                this.handler.Option.Cookies.Clear();
            }
        }

        private NeteaseAudioResolution AudioFailure(string songId, NeteaseErrorCode code, string messageKey, int responseCode = 0)
        {
            return new NeteaseAudioResolution
            {
                SongId = songId,
                Error = new NeteaseError(code, messageKey, responseCode)
            };
        }

        private string PreferHttps(string url)
        {
            Uri uri;

            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return url;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = -1
            };

            return builder.Uri.AbsoluteUri;
        }

        private NeteaseError MapResponseError(string method, int responseCode)
        {
            AppLog.Warning("Netease request {0} returned response code {1}", method, responseCode);

            if (responseCode == 301 || responseCode == 401)
            {
                return new NeteaseError(NeteaseErrorCode.AuthenticationRequired, "Language_Netease_Login_Expired", responseCode);
            }

            if (responseCode == 429)
            {
                return new NeteaseError(NeteaseErrorCode.RateLimited, "Language_Netease_Rate_Limited", responseCode);
            }

            if (responseCode == 8821)
            {
                return new NeteaseError(
                    NeteaseErrorCode.RiskControlRequired,
                    "Language_Netease_Risk_Control_Required",
                    responseCode);
            }

            return new NeteaseError(NeteaseErrorCode.ApiChanged, "Language_Netease_Service_Unavailable", responseCode);
        }

        private NeteaseError MapError(string method, Exception exception, int responseCode, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || exception is OperationCanceledException || exception is TaskCanceledException)
            {
                return new NeteaseError(NeteaseErrorCode.Cancelled, "Language_Netease_Cancelled", responseCode);
            }

            var apiError = exception as ErrorResultBase;

            if (apiError != null && apiError.ErrorCode != 0)
            {
                responseCode = apiError.ErrorCode;
            }

            AppLog.Warning("Netease request {0} failed. ErrorType={1}, ResponseCode={2}",
                method,
                exception?.GetType().Name ?? "Unknown",
                responseCode);

            if (responseCode == 301 || responseCode == 401)
            {
                return new NeteaseError(NeteaseErrorCode.AuthenticationRequired, "Language_Netease_Login_Expired", responseCode);
            }

            if (responseCode == 429)
            {
                return new NeteaseError(NeteaseErrorCode.RateLimited, "Language_Netease_Rate_Limited", responseCode);
            }

            if (responseCode == 8821)
            {
                return new NeteaseError(
                    NeteaseErrorCode.RiskControlRequired,
                    "Language_Netease_Risk_Control_Required",
                    responseCode);
            }

            if (exception is HttpRequestException || exception is TimeoutException || apiError != null)
            {
                return new NeteaseError(NeteaseErrorCode.NetworkUnavailable, "Language_Netease_Network_Error", responseCode);
            }

            return new NeteaseError(NeteaseErrorCode.Unknown, "Language_Netease_Service_Unavailable", responseCode);
        }
    }
}
