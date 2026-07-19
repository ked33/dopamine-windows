using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using NUnit.Framework;
using System;
using System.IO;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseDownloadTests
    {
        [TestCase("audio/mpeg", "https://example.test/song", ".mp3")]
        [TestCase("audio/flac; charset=binary", "https://example.test/song", ".flac")]
        [TestCase("audio/mp4", "https://example.test/song", ".m4a")]
        [TestCase("", "https://example.test/song.opus?token=redacted", ".opus")]
        [TestCase("application/octet-stream", "https://example.test/song", ".audio")]
        public void NormalizeExtensionMapsExpectedAudioTypes(
            string mediaType,
            string url,
            string expected)
        {
            Assert.That(
                NeteaseTemporaryAudioCache.NormalizeExtension(mediaType, url),
                Is.EqualTo(expected));
        }

        [Test]
        public void CreateSafeFileNameBaseReplacesWindowsInvalidCharacters()
        {
            string result = NeteaseDownloadService.CreateSafeFileNameBase(
                "Artist:Name",
                "Song/Name?",
                "123",
                Path.GetTempPath());

            Assert.That(result, Is.EqualTo("Artist_Name - Song_Name_"));
        }

        [Test]
        public void CreateSafeFileNameBaseUsesSongIdForMissingMetadata()
        {
            string result = NeteaseDownloadService.CreateSafeFileNameBase(
                string.Empty,
                string.Empty,
                "12345",
                Path.GetTempPath());

            Assert.That(result, Is.EqualTo("12345 - 12345"));
        }

        [Test]
        public void CreateSafeFileNameBaseRemovesTrailingDotsAndSpaces()
        {
            string result = NeteaseDownloadService.CreateSafeFileNameBase(
                "Artist",
                "Song. ",
                "12345",
                Path.GetTempPath());

            Assert.That(result, Is.EqualTo("Artist - Song"));
        }

        [Test]
        public void CreateSafeFileNameBaseHonorsPathAllowance()
        {
            string longArtist = new string('A', 200);
            string longTitle = new string('B', 200);
            string result = NeteaseDownloadService.CreateSafeFileNameBase(
                longArtist,
                longTitle,
                "12345",
                Path.GetTempPath());

            Assert.That(result.Length, Is.LessThanOrEqualTo(180));
            Assert.That(result.EndsWith("."), Is.False);
        }

        [Test]
        public void SourcePriorityValuesRemainStableForSettings()
        {
            Assert.That((int)OnlineAudioSourcePriority.OfficialFirst, Is.EqualTo(0));
            Assert.That((int)OnlineAudioSourcePriority.UnblockFirst, Is.EqualTo(1));
        }
    }
}
