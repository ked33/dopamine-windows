using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.Bases;
using HyPlayer.NeteaseApi.Bases.ApiContractBases;
using HyPlayer.NeteaseApi.Bases.WeApiContractBases;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    internal sealed class NeteaseWebSongLikeApi : WeApiContractBase<
        NeteaseWebSongLikeRequest,
        NeteaseWebActionResponse,
        ErrorResultBase,
        NeteaseWebSongLikeActualRequest>
    {
        public override string IdentifyRoute => "/song/like";

        public override string Url { get; protected set; } = "https://music.163.com/api/radio/like";

        public override HttpMethod Method => HttpMethod.Post;

        public override string UserAgent => NeteaseWebLoginConstants.DesktopUserAgent;

        public override Task MapRequest(ApiHandlerOption option)
        {
            this.ActualRequest = new NeteaseWebSongLikeActualRequest
            {
                Alg = "itembased",
                TrackId = this.Request?.SongId,
                Like = this.Request != null && this.Request.IsLiked,
                Time = "3"
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

    internal sealed class NeteaseWebSongLikeRequest : RequestBase
    {
        public string SongId { get; set; }

        public bool IsLiked { get; set; }
    }

    internal sealed class NeteaseWebSongLikeActualRequest : WeApiActualRequestBase
    {
        [JsonPropertyName("alg")]
        public string Alg { get; set; }

        [JsonPropertyName("trackId")]
        public string TrackId { get; set; }

        [JsonPropertyName("like")]
        public bool Like { get; set; }

        [JsonPropertyName("time")]
        public string Time { get; set; }
    }

    internal sealed class NeteaseWebRecommendationDislikeApi : WeApiContractBase<
        NeteaseWebRecommendationDislikeRequest,
        NeteaseWebRecommendationDislikeResponse,
        ErrorResultBase,
        NeteaseWebRecommendationDislikeActualRequest>
    {
        public override string IdentifyRoute => "/recommendation/dislike";

        public override string Url { get; protected set; } =
            "https://music.163.com/api/v2/discovery/recommend/dislike";

        public override HttpMethod Method => HttpMethod.Post;

        public override string UserAgent => NeteaseWebLoginConstants.DesktopUserAgent;

        public override Task MapRequest(ApiHandlerOption option)
        {
            this.ActualRequest = new NeteaseWebRecommendationDislikeActualRequest
            {
                ResourceId = this.Request?.SongId,
                ResourceType = 4,
                SceneType = 1
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

    internal sealed class NeteaseWebRecommendationDislikeRequest : RequestBase
    {
        public string SongId { get; set; }
    }

    internal sealed class NeteaseWebRecommendationDislikeActualRequest : WeApiActualRequestBase
    {
        [JsonPropertyName("resId")]
        public string ResourceId { get; set; }

        [JsonPropertyName("resType")]
        public int ResourceType { get; set; }

        [JsonPropertyName("sceneType")]
        public int SceneType { get; set; }
    }

    internal sealed class NeteaseWebActionResponse : CodedResponseBase
    {
    }

    internal sealed class NeteaseWebRecommendationDislikeResponse : CodedResponseBase
    {
        [JsonPropertyName("data")]
        public NeteaseWebRecommendationSong Data { get; set; }
    }

    internal sealed class NeteaseWebRecommendationSong
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("ar")]
        public NeteaseWebRecommendationArtist[] Artists { get; set; }

        [JsonPropertyName("artists")]
        public NeteaseWebRecommendationArtist[] LegacyArtists { get; set; }

        [JsonPropertyName("al")]
        public NeteaseWebRecommendationAlbum Album { get; set; }

        [JsonPropertyName("album")]
        public NeteaseWebRecommendationAlbum LegacyAlbum { get; set; }

        [JsonPropertyName("dt")]
        public long DurationMilliseconds { get; set; }

        [JsonPropertyName("duration")]
        public long LegacyDurationMilliseconds { get; set; }

        [JsonPropertyName("privilege")]
        public NeteaseWebRecommendationPrivilege Privilege { get; set; }
    }

    internal sealed class NeteaseWebRecommendationArtist
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    internal sealed class NeteaseWebRecommendationAlbum
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("picUrl")]
        public string ArtworkUrl { get; set; }
    }

    internal sealed class NeteaseWebRecommendationPrivilege
    {
        [JsonPropertyName("st")]
        public int Status { get; set; }
    }
}
