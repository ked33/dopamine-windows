using Dopamine.Services.Online.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using HyPlayer.NeteaseApi.Models.ResponseModels;
using NUnit.Framework;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseAudioAccessTests
    {
        [TestCase(null, null, null, false)]
        [TestCase(0, 0, 0, false)]
        [TestCase(1, null, null, true)]
        [TestCase(4, null, null, true)]
        [TestCase(0, 1, 0, true)]
        [TestCase(0, 4, 0, true)]
        [TestCase(8, 8, 0, false)]
        [TestCase(0, 0, -200, true)]
        [TestCase(8, 8, -200, true)]
        [TestCase(999, null, null, false)]
        public void OnlyExplicitRestrictionHintsSelectFallback(int? fee, int? privilegeFee, int? status, bool expected)
        {
            Assert.That(NeteaseAudioAccessPolicy.PrefersFallback(fee, privilegeFee, status), Is.EqualTo(expected));
        }

        [TestCase("\"fee\":1", true)]
        [TestCase("\"fee\":4", true)]
        [TestCase("\"privilege\":{\"fee\":1}", true)]
        [TestCase("\"privilege\":{\"st\":-200}", true)]
        [TestCase("\"fee\":8,\"privilege\":{\"pl\":128000,\"cp\":1}", false)]
        [TestCase("\"privilege\":{}", false)]
        [TestCase("\"privilege\":{\"pl\":0,\"cp\":0}", false)]
        public void DailyAndIntelligenceSongMappingPreservesHints(string fields, bool expected)
        {
            var song = Deserialize<EmittedSongDtoWithPrivilege>("{\"id\":\"123\",\"name\":\"Song\"," + fields + "}");
            Assert.That(NeteaseApiClient.MapSong(song).PreferFallbackAudio, Is.EqualTo(expected));
        }

        [TestCase("\"fee\":1", true)]
        [TestCase("\"privilege\":{\"fee\":4}", true)]
        [TestCase("\"privilege\":{\"st\":-200}", true)]
        [TestCase("\"fee\":8", false)]
        [TestCase("\"privilege\":{}", false)]
        public void ReplacementSongMappingPreservesHints(string fields, bool expected)
        {
            var song = Deserialize<NeteaseWebRecommendationSong>("{\"id\":\"123\",\"name\":\"Song\"," + fields + "}");
            Assert.That(NeteaseApiClient.MapReplacementRecommendation(song).PreferFallbackAudio, Is.EqualTo(expected));
        }

        [TestCase("\"fee\":1", true)]
        [TestCase("\"privilege\":{\"st\":-200}", true)]
        [TestCase("\"fee\":8", false)]
        [TestCase("\"privilege\":{}", false)]
        public void PersonalFmResponsePreservesAccessHintsAndMetadata(string fields, bool expected)
        {
            var response = Deserialize<NeteasePersonalFmResponse>("{\"code\":200,\"data\":[{" +
                "\"id\":123,\"name\":\"Song\",\"reason\":\"FM\",\"duration\":180000," +
                "\"artists\":[{\"name\":\"Artist\"}],\"album\":{\"name\":\"Album\"}," + fields + "}]}");
            var mapped = NeteaseApiClient.MapSong(response.Items[0]);
            Assert.That(mapped.PreferFallbackAudio, Is.EqualTo(expected));
            Assert.That(mapped.Id, Is.EqualTo("123"));
            Assert.That(mapped.Artists, Is.EqualTo(new[] { "Artist" }));
            Assert.That(mapped.AlbumName, Is.EqualTo("Album"));
            Assert.That(mapped.DurationMilliseconds, Is.EqualTo(180000));
            Assert.That(response.Items[0].RecommendedReason, Is.EqualTo("FM"));
        }

        [Test]
        public async Task PersonalFmRequestRetainsSdkEndpointAndRequestShape()
        {
            var request = new PersonalFmRequest { Mode = "FAMILIAR", Limit = 3 };
            var original = new PersonalFmApi { Request = request };
            var extended = new NeteasePersonalFmApi { Request = request };
            var option = new ApiHandlerOption();
            await original.MapRequest(option);
            await extended.MapRequest(option);
            Assert.That(extended.Url, Is.EqualTo(original.Url));
            Assert.That(extended.ApiPath, Is.EqualTo(original.ApiPath));
            Assert.That(extended.Method, Is.EqualTo(original.Method));
            Assert.That(extended.ActualRequest.Mode, Is.EqualTo(original.ActualRequest.Mode));
            Assert.That(extended.ActualRequest.Limit, Is.EqualTo(original.ActualRequest.Limit));
        }

        private static T Deserialize<T>(string json)
        {
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);
            return JsonSerializer.Deserialize<T>(json, option.JsonSerializerOptions);
        }
    }
}
