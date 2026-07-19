using Dopamine.Services.Online.GdMusic;
using NUnit.Framework;
using System.Collections.Generic;

namespace Dopamine.Tests
{
    [TestFixture]
    public class GdMusicApiMappingTests
    {
        [Test]
        public void ParsesSearchResultsWithArtistArraysAndNumericIds()
        {
            string json = "[{\"id\":2155423468,\"name\":\"Song A\",\"artist\":[\"Artist 1\",\"Artist 2\"]," +
                "\"album\":\"Album A\",\"pic_id\":\"109951169\",\"lyric_id\":2155423468,\"source\":\"netease\"}," +
                "{\"id\":\"str-id\",\"name\":\"Song B\",\"artist\":\"Single Artist\",\"album\":null," +
                "\"pic_id\":8881,\"source\":\"tencent\"}]";

            IReadOnlyList<GdMusicSearchResult> results = GdMusicApiClient.ParseSearchResults(json);

            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results[0].Id, Is.EqualTo("2155423468"));
            Assert.That(results[0].Name, Is.EqualTo("Song A"));
            Assert.That(results[0].Artists.Count, Is.EqualTo(2));
            Assert.That(results[0].Artists[0], Is.EqualTo("Artist 1"));
            Assert.That(results[0].AlbumName, Is.EqualTo("Album A"));
            Assert.That(results[0].PictureId, Is.EqualTo("109951169"));
            Assert.That(results[0].Source, Is.EqualTo("netease"));
            Assert.That(results[1].Id, Is.EqualTo("str-id"));
            Assert.That(results[1].Artists.Count, Is.EqualTo(1));
            Assert.That(results[1].Artists[0], Is.EqualTo("Single Artist"));
            Assert.That(results[1].AlbumName, Is.EqualTo(string.Empty));
            Assert.That(results[1].PictureId, Is.EqualTo("8881"));
        }

        [Test]
        public void SkipsSearchEntriesWithoutAnId()
        {
            string json = "[{\"name\":\"No id\"},{\"id\":\"1\",\"name\":\"Valid\"}]";

            IReadOnlyList<GdMusicSearchResult> results = GdMusicApiClient.ParseSearchResults(json);

            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Id, Is.EqualTo("1"));
        }

        [Test]
        public void ReturnsEmptyListForAnEmptySearchArray()
        {
            IReadOnlyList<GdMusicSearchResult> results = GdMusicApiClient.ParseSearchResults("[]");

            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count, Is.EqualTo(0));
        }

        [TestCase("{\"error\":\"denied\"}")]
        [TestCase("not json at all")]
        [TestCase("")]
        [TestCase(null)]
        public void ReturnsNullForInvalidSearchPayloads(string json)
        {
            Assert.That(GdMusicApiClient.ParseSearchResults(json), Is.Null);
        }

        [Test]
        public void ParsesTrackUrlWithNumericFields()
        {
            string json = "{\"url\":\"https://example.com/a.mp3\",\"br\":320,\"size\":10240}";

            GdMusicTrackUrl trackUrl = GdMusicApiClient.ParseTrackUrl(json);

            Assert.That(trackUrl, Is.Not.Null);
            Assert.That(trackUrl.Url, Is.EqualTo("https://example.com/a.mp3"));
            Assert.That(trackUrl.BitRate, Is.EqualTo(320));
            Assert.That(trackUrl.SizeKilobytes, Is.EqualTo(10240L));
        }

        [Test]
        public void ParsesTrackUrlWithStringFieldsAndMissingValues()
        {
            string json = "{\"url\":\"https://example.com/a.flac\",\"br\":\"999\"}";

            GdMusicTrackUrl trackUrl = GdMusicApiClient.ParseTrackUrl(json);

            Assert.That(trackUrl, Is.Not.Null);
            Assert.That(trackUrl.BitRate, Is.EqualTo(999));
            Assert.That(trackUrl.SizeKilobytes, Is.EqualTo(0L));
        }

        [TestCase("[1,2]")]
        [TestCase("broken")]
        [TestCase(null)]
        public void ReturnsNullForInvalidTrackUrlPayloads(string json)
        {
            Assert.That(GdMusicApiClient.ParseTrackUrl(json), Is.Null);
        }

        [Test]
        public void ParsesPictureUrl()
        {
            Assert.That(
                GdMusicApiClient.ParsePictureUrl("{\"url\":\"https://example.com/pic.jpg\"}"),
                Is.EqualTo("https://example.com/pic.jpg"));
        }

        [Test]
        public void ReturnsEmptyPictureUrlWhenMissing()
        {
            Assert.That(GdMusicApiClient.ParsePictureUrl("{}"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void BuildsSearchUrlWithEscapedKeyword()
        {
            string url = GdMusicApiClient.BuildSearchUrl("netease", "hello world & more", 30, 2);

            Assert.That(url, Does.StartWith("https://music-api.gdstudio.xyz/api.php?types=search"));
            Assert.That(url, Does.Contain("source=netease"));
            Assert.That(url, Does.Contain("name=hello%20world%20%26%20more"));
            Assert.That(url, Does.Contain("count=30"));
            Assert.That(url, Does.Contain("pages=2"));
        }

        [Test]
        public void NormalizesUnknownSourceToNetease()
        {
            string url = GdMusicApiClient.BuildTrackUrlRequestUrl("bad-source", "42", 320);

            Assert.That(url, Does.Contain("source=netease"));
            Assert.That(url, Does.Contain("id=42"));
            Assert.That(url, Does.Contain("br=320"));
        }

        [TestCase("netease", "netease")]
        [TestCase("JOOX", "joox")]
        [TestCase(" bilibili ", "bilibili")]
        [TestCase("unknown", "netease")]
        [TestCase("", "netease")]
        [TestCase(null, "netease")]
        public void NormalizesSearchSources(string input, string expected)
        {
            Assert.That(GdMusicSettings.NormalizeSource(input), Is.EqualTo(expected));
        }

        [TestCase(128, 128)]
        [TestCase(192, 192)]
        [TestCase(320, 320)]
        [TestCase(740, 740)]
        [TestCase(999, 999)]
        [TestCase(0, 999)]
        [TestCase(500, 999)]
        public void NormalizesDownloadQualities(int input, int expected)
        {
            Assert.That(GdMusicSettings.NormalizeQuality(input), Is.EqualTo(expected));
        }
    }
}
