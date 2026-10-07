using Dopamine.Services.Online.GdMusic;
using Dopamine.Services.Playback;
using NUnit.Framework;

namespace Dopamine.Tests
{
    [TestFixture]
    public class GdMusicFallbackMatcherTests
    {
        [TestCase("My Song", "Artist", "joox", true)]
        [TestCase("Ｍｙ Ｓｏｎｇ", "ARTIST", "joox", true)]
        [TestCase("My Song (Live)", "Artist", "joox", false)]
        [TestCase("My Song (Remix)", "Artist", "joox", false)]
        [TestCase("My Song", "Cover Artist", "joox", false)]
        [TestCase("My Song", "Artist", "netease", false)]
        public void RequiresCorrectPlatformTitleArtistAndVersion(string title, string artist, string source, bool expected)
        {
            var track = GdMusicAudioFallbackTests.CreateRequest().Track;
            track.Track.TrackTitle = "My Song";
            track.Track.Artists = "Artist";
            var match = GdMusicFallbackMatcher.Match(track, "joox", new[] {
                new GdMusicSearchResult { Id = "platform-id", Name = title, Artists = new[] { artist }, Source = source }
            });
            Assert.That(match != null, Is.EqualTo(expected));
        }

        [Test]
        public void AlbumBreaksTiesOtherwiseAmbiguousResultsAreRejected()
        {
            var track = GdMusicAudioFallbackTests.CreateRequest().Track;
            track.Track.TrackTitle = "Song";
            track.Track.Artists = "Artist";
            var candidates = new[] {
                new GdMusicSearchResult { Id = "a", Name = "Song", Artists = new[] { "Artist" }, Source = "joox", AlbumName = "Album A" },
                new GdMusicSearchResult { Id = "b", Name = "Song", Artists = new[] { "Artist" }, Source = "joox", AlbumName = "Album B" }
            };
            Assert.That(GdMusicFallbackMatcher.Match(track, "joox", candidates), Is.Null);
            track.Track.AlbumTitle = "Album B";
            Assert.That(GdMusicFallbackMatcher.Match(track, "joox", candidates).Id, Is.EqualTo("b"));
        }
    }
}
