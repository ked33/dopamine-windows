using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.Bases;
using HyPlayer.NeteaseApi.Bases.ApiContractBases;
using HyPlayer.NeteaseApi.Bases.WeApiContractBases;
using HyPlayer.NeteaseApi.Extensions;
using System;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    internal static class NeteaseWebLoginConstants
    {
        internal const string DesktopUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36";

        internal static void ApplyCommonHeaders(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh-Hans;q=0.9");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            request.Headers.TryAddWithoutValidation("Origin", "https://music.163.com");
            request.Headers.TryAddWithoutValidation("x-os", "web");
            request.Headers.TryAddWithoutValidation("x-channelsource", "undefined");
            request.Headers.TryAddWithoutValidation("nm-gcore-status", "1");
        }
    }

    internal static class NeteaseWebLoginSerializer
    {
        internal static void EnableCustomContracts(ApiHandlerOption option)
        {
            if (option == null)
            {
                throw new ArgumentNullException(nameof(option));
            }

            option.JsonSerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(
                option.JsonSerializerOptions.TypeInfoResolver,
                new DefaultJsonTypeInfoResolver());
        }
    }

    internal sealed class NeteaseWebQrKeyApi : WeApiContractBase<NeteaseWebQrKeyRequest,
        NeteaseWebQrKeyResponse, ErrorResultBase, NeteaseWebQrKeyActualRequest>
    {
        public override string IdentifyRoute => "/login/qr/key";

        public override string Url { get; protected set; } = "https://music.163.com/api/login/qrcode/unikey";

        public override HttpMethod Method => HttpMethod.Post;

        public override string UserAgent => NeteaseWebLoginConstants.DesktopUserAgent;

        public override Task MapRequest(ApiHandlerOption option)
        {
            this.ActualRequest = new NeteaseWebQrKeyActualRequest
            {
                Type = 1,
                NoCheckToken = true
            };
            return Task.CompletedTask;
        }

        public override async Task<HttpRequestMessage> GenerateRequestMessageAsync(
            ApiHandlerOption option,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            HttpRequestMessage request = await base.GenerateRequestMessageAsync(option, cancellationToken);
            NeteaseWebLoginConstants.ApplyCommonHeaders(request);
            return request;
        }
    }

    internal sealed class NeteaseWebQrKeyRequest : RequestBase
    {
    }

    internal sealed class NeteaseWebQrKeyResponse : CodedResponseBase
    {
        [JsonPropertyName("unikey")]
        public string Unikey { get; set; }
    }

    internal sealed class NeteaseWebQrKeyActualRequest : WeApiActualRequestBase
    {
        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("noCheckToken")]
        public bool NoCheckToken { get; set; }
    }

    internal sealed class NeteaseWebQrCheckApi : WeApiContractBase<NeteaseWebQrCheckRequest,
        NeteaseWebQrCheckResponse, ErrorResultBase, NeteaseWebQrCheckActualRequest>
    {
        public override string IdentifyRoute => "/login/qr/check";

        public override string Url { get; protected set; } = "https://music.163.com/api/login/qrcode/client/login";

        public override HttpMethod Method => HttpMethod.Post;

        public override string UserAgent => NeteaseWebLoginConstants.DesktopUserAgent;

        public override Task MapRequest(ApiHandlerOption option)
        {
            this.ActualRequest = new NeteaseWebQrCheckActualRequest
            {
                Key = this.Request?.Unikey,
                Type = 1,
                NoCheckToken = true,
                YdDeviceToken = this.Request?.YdDeviceToken ?? string.Empty
            };
            return Task.CompletedTask;
        }

        public override async Task<HttpRequestMessage> GenerateRequestMessageAsync(
            ApiHandlerOption option,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            HttpRequestMessage request = await base.GenerateRequestMessageAsync(option, cancellationToken);
            NeteaseWebLoginConstants.ApplyCommonHeaders(request);
            request.Headers.TryAddWithoutValidation("x-loginmethod", "QrCode");

            if (!string.IsNullOrWhiteSpace(this.Request?.ChainId))
            {
                request.Headers.TryAddWithoutValidation("x-login-chain-id", this.Request.ChainId);
            }

            return request;
        }

        public override async Task<Results<TResponseModel, ErrorResultBase>> ProcessResponseAsync<TResponseModel>(
            HttpResponseMessage response,
            ApiHandlerOption option,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Results<TResponseModel, ErrorResultBase> result =
                await base.ProcessResponseAsync<TResponseModel>(response, option, cancellationToken);

            var qrResponse = result.Value as NeteaseWebQrCheckResponse;

            if (qrResponse != null && qrResponse.Code == 803 &&
                !option.Cookies.ContainsKey("MUSIC_U") &&
                response.Headers.TryGetValues("x-refresh-token", out var refreshTokens))
            {
                foreach (string refreshToken in refreshTokens)
                {
                    if (!string.IsNullOrWhiteSpace(refreshToken))
                    {
                        option.Cookies["MUSIC_U"] = refreshToken;
                        break;
                    }
                }
            }

            return result;
        }
    }

    internal sealed class NeteaseWebQrCheckRequest : RequestBase
    {
        public string Unikey { get; set; }

        public string ChainId { get; set; }

        public string YdDeviceToken { get; set; }
    }

    internal sealed class NeteaseWebQrCheckResponse : CodedResponseBase
    {
    }

    internal sealed class NeteaseWebQrCheckActualRequest : WeApiActualRequestBase
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("noCheckToken")]
        public bool NoCheckToken { get; set; }

        [JsonPropertyName("ydDeviceToken")]
        public string YdDeviceToken { get; set; }
    }

    internal sealed class NeteaseWebLoginStatusApi : WeApiContractBase<NeteaseWebLoginStatusRequest,
        NeteaseWebLoginStatusResponse, ErrorResultBase, WeApiActualRequestBase>
    {
        public override string IdentifyRoute => "/login/status";

        public override string Url { get; protected set; } = "https://music.163.com/api/w/nuser/account/get";

        public override HttpMethod Method => HttpMethod.Post;

        public override string UserAgent => NeteaseWebLoginConstants.DesktopUserAgent;

        public override Task MapRequest(ApiHandlerOption option)
        {
            this.ActualRequest = new WeApiActualRequestBase();
            return Task.CompletedTask;
        }

        public override async Task<HttpRequestMessage> GenerateRequestMessageAsync(
            ApiHandlerOption option,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            HttpRequestMessage request = await base.GenerateRequestMessageAsync(option, cancellationToken);
            NeteaseWebLoginConstants.ApplyCommonHeaders(request);
            return request;
        }
    }

    internal sealed class NeteaseWebLoginStatusRequest : RequestBase
    {
    }

    internal sealed class NeteaseWebLoginStatusResponse : CodedResponseBase
    {
        [JsonPropertyName("profile")]
        public NeteaseWebProfile Profile { get; set; }

        [JsonPropertyName("account")]
        public NeteaseWebAccount Account { get; set; }
    }

    internal sealed class NeteaseWebProfile
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("nickname")]
        public string Nickname { get; set; }

        [JsonPropertyName("vipType")]
        public int VipType { get; set; }
    }

    internal sealed class NeteaseWebAccount
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("userName")]
        public string UserName { get; set; }

        [JsonPropertyName("vipType")]
        public int VipType { get; set; }
    }
}
