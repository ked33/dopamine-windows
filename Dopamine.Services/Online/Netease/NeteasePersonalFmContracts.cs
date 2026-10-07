using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using HyPlayer.NeteaseApi.Bases;
using HyPlayer.NeteaseApi.Bases.EApiContractBases;
using HyPlayer.NeteaseApi.Models.ResponseModels;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    // Keep the SDK's FM request unchanged; its response DTO drops fee/privilege.
    internal sealed class NeteasePersonalFmApi : EApiContractBase<PersonalFmRequest,
        NeteasePersonalFmResponse, ErrorResultBase, PersonalFmActualRequest>
    {
        public override string IdentifyRoute => "/personal/fm";
        public override string Url { get; protected set; } = "https://interface3.music.163.com/eapi/v1/radio/get";
        public override string ApiPath { get; protected set; } = "/api/v1/radio/get";
        public override HttpMethod Method => HttpMethod.Post;

        public override Task MapRequest(ApiHandlerOption option)
        {
            if (this.Request != null)
                this.ActualRequest = new PersonalFmActualRequest { Mode = this.Request.Mode, Limit = this.Request.Limit };
            return Task.CompletedTask;
        }
    }

    internal sealed class NeteasePersonalFmResponse : CodedResponseBase
    {
        [JsonPropertyName("data")]
        public NeteasePersonalFmSong[] Items { get; set; }
    }

    internal sealed class NeteasePersonalFmSong : SongDto
    {
        [JsonPropertyName("reason")]
        public string RecommendedReason { get; set; }

        [JsonPropertyName("fee")]
        public int? Fee { get; set; }

        [JsonPropertyName("privilege")]
        public NeteaseWebRecommendationPrivilege Privilege { get; set; }
    }
}
