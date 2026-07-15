using Dopamine.Services.Online.Netease;
using HyPlayer.NeteaseApi;
using NUnit.Framework;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseInteractionContractTests
    {
        [Test]
        public async Task SongLikeUsesWebRequestShapeAndHeaders()
        {
            var api = new NeteaseWebSongLikeApi
            {
                Request = new NeteaseWebSongLikeRequest
                {
                    SongId = "123",
                    IsLiked = true
                }
            };
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);

            await api.MapRequest(option);

            Assert.That(api.ActualRequest.Alg, Is.EqualTo("itembased"));
            Assert.That(api.ActualRequest.TrackId, Is.EqualTo("123"));
            Assert.That(api.ActualRequest.Like, Is.True);
            Assert.That(api.ActualRequest.Time, Is.EqualTo("3"));

            using (HttpRequestMessage request = await api.GenerateRequestMessageAsync(
                option,
                CancellationToken.None))
            {
                Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/weapi/radio/like"));
                Assert.That(request.Headers.GetValues("Origin").Single(), Is.EqualTo("https://music.163.com"));
                Assert.That(request.Headers.GetValues("x-os").Single(), Is.EqualTo("web"));
            }
        }

        [Test]
        public async Task RecommendationDislikeUsesDailyRecommendationShape()
        {
            var api = new NeteaseWebRecommendationDislikeApi
            {
                Request = new NeteaseWebRecommendationDislikeRequest { SongId = "456" }
            };
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);

            await api.MapRequest(option);

            Assert.That(api.ActualRequest.ResourceId, Is.EqualTo("456"));
            Assert.That(api.ActualRequest.ResourceType, Is.EqualTo(4));
            Assert.That(api.ActualRequest.SceneType, Is.EqualTo(1));

            using (HttpRequestMessage request = await api.GenerateRequestMessageAsync(
                option,
                CancellationToken.None))
            {
                Assert.That(
                    request.RequestUri.AbsolutePath,
                    Is.EqualTo("/weapi/v2/discovery/recommend/dislike"));
            }
        }

        [Test]
        public async Task RecommendationDislikeDeserializesReplacementSong()
        {
            var api = new NeteaseWebRecommendationDislikeApi();
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);

            using (var response = new HttpResponseMessage(HttpStatusCode.OK))
            {
                response.Content = new StringContent(
                    "{\"code\":200,\"data\":{\"id\":\"789\",\"name\":\"replacement\"," +
                    "\"ar\":[{\"name\":\"artist\"}],\"al\":{\"id\":\"12\",\"name\":\"album\"," +
                    "\"picUrl\":\"https://example.invalid/cover.jpg\"},\"dt\":123000}}",
                    Encoding.UTF8,
                    "application/json");

                var result = await api.ProcessResponseAsync<NeteaseWebRecommendationDislikeResponse>(
                    response,
                    option,
                    CancellationToken.None);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Value.Data.Id, Is.EqualTo("789"));
                Assert.That(result.Value.Data.Artists[0].Name, Is.EqualTo("artist"));
                Assert.That(result.Value.Data.Album.Name, Is.EqualTo("album"));
                Assert.That(result.Value.Data.DurationMilliseconds, Is.EqualTo(123000));
            }
        }

        [Test]
        public async Task RecommendationDislikeDeserializesLegacyReplacementShape()
        {
            var api = new NeteaseWebRecommendationDislikeApi();
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);

            using (var response = new HttpResponseMessage(HttpStatusCode.OK))
            {
                response.Content = new StringContent(
                    "{\"code\":200,\"data\":{\"id\":789,\"name\":\"legacy\"," +
                    "\"artists\":[{\"name\":\"artist\"}],\"album\":{\"id\":12," +
                    "\"name\":\"album\",\"picUrl\":\"https://example.invalid/cover.jpg\"}," +
                    "\"duration\":456000}}",
                    Encoding.UTF8,
                    "application/json");

                var result = await api.ProcessResponseAsync<NeteaseWebRecommendationDislikeResponse>(
                    response,
                    option,
                    CancellationToken.None);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Value.Data.Id, Is.EqualTo("789"));
                Assert.That(result.Value.Data.LegacyArtists[0].Name, Is.EqualTo("artist"));
                Assert.That(result.Value.Data.LegacyAlbum.Id, Is.EqualTo("12"));
                Assert.That(result.Value.Data.LegacyDurationMilliseconds, Is.EqualTo(456000));
            }
        }
    }
}
