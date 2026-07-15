using Dopamine.Services.Online.Netease;
using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseLoginContextTests
    {
        [Test]
        public void CreatesExpectedChainIdentifier()
        {
            string chainId = NeteaseLoginContext.CreateChainId("ABC123", 1720958400123);

            Assert.That(chainId, Is.EqualTo("v1_ABC123_web_login_1720958400123"));
        }

        [Test]
        public void BuildsScanLoginQrContent()
        {
            string qrContent = NeteaseLoginContext.BuildQrContent("key+/ ?&", "chain+/ ?&");

            Assert.That(qrContent, Does.StartWith("https://music.163.com/st/platform/scanlogin?"));
            Assert.That(qrContent, Does.Contain("codekey=key%2B%2F%20%3F%26"));
            Assert.That(qrContent, Does.Contain("chainId=chain%2B%2F%20%3F%26"));
            Assert.That(qrContent, Does.Contain("hdw_device=web"));
            Assert.That(qrContent, Does.Contain("hdw_appid=web"));
            Assert.That(qrContent, Does.Contain("hitExp=1"));
        }

        [Test]
        public void CreatesWebDeviceIdentifiersWithExpectedShape()
        {
            string sDeviceId = NeteaseLoginContext.CreateSDeviceId();
            string nmtid = NeteaseLoginContext.CreateNmtid();

            Assert.That(sDeviceId.Length, Is.EqualTo(52));
            Assert.That(nmtid.Length, Is.EqualTo(32));
            Assert.That(Regex.IsMatch(sDeviceId, "^[0-9A-F]{52}$"), Is.True);
            Assert.That(Regex.IsMatch(nmtid, "^[0-9A-F]{32}$"), Is.True);
        }

        [Test]
        public void NormalizesWebCookieDefaultsWithoutOverwritingExistingValues()
        {
            var input = new Dictionary<string, string>
            {
                ["MUSIC_U"] = "local-test-token",
                ["os"] = "custom-os",
                ["appver"] = "custom-version",
                ["empty"] = string.Empty
            };

            Dictionary<string, string> normalized = NeteaseLoginContext.NormalizeCookies(input);

            Assert.That(normalized.Count, Is.EqualTo(3));
            Assert.That(normalized["MUSIC_U"], Is.EqualTo("local-test-token"));
            Assert.That(normalized["os"], Is.EqualTo("custom-os"));
            Assert.That(normalized["appver"], Is.EqualTo("custom-version"));
        }

        [Test]
        public void AddsWebCookieDefaultsWhenCookieInputIsPresent()
        {
            var input = new Dictionary<string, string>
            {
                ["MUSIC_U"] = "local-test-token"
            };

            Dictionary<string, string> normalized = NeteaseLoginContext.NormalizeCookies(input);

            Assert.That(normalized["os"], Is.EqualTo("pc"));
            Assert.That(normalized["appver"], Is.EqualTo("8.10.35"));
        }

        [Test]
        public void LeavesEmptyCookieInputEmpty()
        {
            Dictionary<string, string> normalized = NeteaseLoginContext.NormalizeCookies(null);

            Assert.That(normalized, Is.Empty);
        }
    }
}
