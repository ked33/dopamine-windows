using Dopamine.Services.Online.Netease;
using NUnit.Framework;
using System.Linq;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseCookieHeaderParserTests
    {
        [Test]
        public void ParsesOptionalPrefixAndValueContainingEquals()
        {
            var result = NeteaseCookieHeaderParser.Parse("Cookie: first=value==; second=two");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value["first"], Is.EqualTo("value=="));
            Assert.That(result.Value["second"], Is.EqualTo("two"));
        }

        [Test]
        public void UsesLastDuplicateValue()
        {
            var result = NeteaseCookieHeaderParser.Parse("same=first; same=last");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Count, Is.EqualTo(1));
            Assert.That(result.Value["same"], Is.EqualTo("last"));
        }

        [TestCase("")]
        [TestCase("missing-equals")]
        [TestCase("=empty-name")]
        [TestCase("name=value\r\ninjected=true")]
        public void RejectsInvalidInput(string input)
        {
            var result = NeteaseCookieHeaderParser.Parse(input);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(NeteaseErrorCode.InvalidCookie));
        }

        [Test]
        public void RejectsOversizedInput()
        {
            string input = "name=" + new string('x', (32 * 1024) + 1);
            var result = NeteaseCookieHeaderParser.Parse(input);

            Assert.That(result.IsSuccess, Is.False);
        }
    }
}
