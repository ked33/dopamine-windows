using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts.Login;
using NUnit.Framework;
using System.Net.Http;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseDependencySmokeTests
    {
        [Test]
        public void CanConstructHandlerAndLocalResponseDto()
        {
            var handler = new NeteaseCloudMusicApiHandler(new HttpClient());
            handler.Option.Cookies["test"] = "value";

            var response = new LoginQrCodeUnikeyResponse
            {
                Code = 200,
                Unikey = "local-test-key"
            };

            Assert.That(handler.Option.Cookies.Count, Is.EqualTo(1));
            Assert.That(response.Code, Is.EqualTo(200));
            Assert.That(response.Unikey, Is.EqualTo("local-test-key"));
        }
    }
}
