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
    public class NeteaseWebLoginContractTests
    {
        [Test]
        public async Task QrCheckUsesWebLoginRequestShapeAndHeaders()
        {
            var api = new NeteaseWebQrCheckApi
            {
                Request = new NeteaseWebQrCheckRequest
                {
                    Unikey = "unit-test-key",
                    ChainId = "unit-test-chain",
                    YdDeviceToken = string.Empty
                }
            };
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);

            await api.MapRequest(option);

            Assert.That(api.ActualRequest.Key, Is.EqualTo("unit-test-key"));
            Assert.That(api.ActualRequest.Type, Is.EqualTo(1));
            Assert.That(api.ActualRequest.NoCheckToken, Is.True);

            using (HttpRequestMessage request = await api.GenerateRequestMessageAsync(option, CancellationToken.None))
            {
                Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/weapi/login/qrcode/client/login"));
                Assert.That(request.Headers.GetValues("Origin").Single(), Is.EqualTo("https://music.163.com"));
                Assert.That(request.Headers.GetValues("x-os").Single(), Is.EqualTo("web"));
                Assert.That(request.Headers.GetValues("x-loginmethod").Single(), Is.EqualTo("QrCode"));
                Assert.That(request.Headers.GetValues("x-login-chain-id").Single(), Is.EqualTo("unit-test-chain"));
            }
        }

        [Test]
        public async Task QrCheckCapturesRefreshTokenWhenMusicUIsMissing()
        {
            var api = new NeteaseWebQrCheckApi();
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);

            using (var response = new HttpResponseMessage(HttpStatusCode.OK))
            {
                response.Content = new StringContent("{\"code\":803}", Encoding.UTF8, "application/json");
                response.Headers.TryAddWithoutValidation("x-refresh-token", "unit-test-refresh");

                await api.ProcessResponseAsync<NeteaseWebQrCheckResponse>(
                    response,
                    option,
                    CancellationToken.None);
            }

            Assert.That(option.Cookies["MUSIC_U"], Is.EqualTo("unit-test-refresh"));
        }

        [Test]
        public async Task QrCheckDoesNotOverwriteExistingMusicU()
        {
            var api = new NeteaseWebQrCheckApi();
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);
            option.Cookies["MUSIC_U"] = "existing-unit-test-value";

            using (var response = new HttpResponseMessage(HttpStatusCode.OK))
            {
                response.Content = new StringContent("{\"code\":803}", Encoding.UTF8, "application/json");
                response.Headers.TryAddWithoutValidation("x-refresh-token", "replacement-unit-test-value");

                await api.ProcessResponseAsync<NeteaseWebQrCheckResponse>(
                    response,
                    option,
                    CancellationToken.None);
            }

            Assert.That(option.Cookies["MUSIC_U"], Is.EqualTo("existing-unit-test-value"));
        }

        [Test]
        public async Task LoginStatusDeserializesCustomWebResponse()
        {
            var api = new NeteaseWebLoginStatusApi();
            var option = new ApiHandlerOption();
            NeteaseWebLoginSerializer.EnableCustomContracts(option);

            using (var response = new HttpResponseMessage(HttpStatusCode.OK))
            {
                response.Content = new StringContent(
                    "{\"code\":200,\"profile\":{\"userId\":\"42\",\"nickname\":\"unit-test\",\"vipType\":1}}",
                    Encoding.UTF8,
                    "application/json");

                var result = await api.ProcessResponseAsync<NeteaseWebLoginStatusResponse>(
                    response,
                    option,
                    CancellationToken.None);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Value.Profile.UserId, Is.EqualTo("42"));
                Assert.That(result.Value.Profile.Nickname, Is.EqualTo("unit-test"));
                Assert.That(result.Value.Profile.VipType, Is.EqualTo(1));
            }
        }
    }
}
