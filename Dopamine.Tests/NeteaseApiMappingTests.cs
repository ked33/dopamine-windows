using Dopamine.Services.Online.Netease;
using HyPlayer.NeteaseApi.Bases;
using NUnit.Framework;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseApiMappingTests
    {
        [TestCase(800)]
        [TestCase(801)]
        [TestCase(802)]
        [TestCase(803)]
        public void RecognizesQrBusinessStatusCodes(int statusCode)
        {
            var exception = new ErrorResultBase(statusCode, "local-test-status");

            bool isQrStatus = NeteaseQrStatusMapper.TryGetStatusCode(exception, out int actualStatusCode);

            Assert.That(isQrStatus, Is.True);
            Assert.That(actualStatusCode, Is.EqualTo(statusCode));
        }

        [TestCase(0)]
        [TestCase(200)]
        [TestCase(500)]
        [TestCase(804)]
        public void RejectsNonQrStatusCodes(int statusCode)
        {
            var exception = new ErrorResultBase(statusCode, "local-test-error");

            bool isQrStatus = NeteaseQrStatusMapper.TryGetStatusCode(exception, out int actualStatusCode);

            Assert.That(isQrStatus, Is.False);
            Assert.That(actualStatusCode, Is.EqualTo(0));
        }

        [Test]
        public void RejectsNullException()
        {
            bool isQrStatus = NeteaseQrStatusMapper.TryGetStatusCode(null, out int actualStatusCode);

            Assert.That(isQrStatus, Is.False);
            Assert.That(actualStatusCode, Is.EqualTo(0));
        }
    }
}
