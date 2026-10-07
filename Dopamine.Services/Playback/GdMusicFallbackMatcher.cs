using Dopamine.Data;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.GdMusic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dopamine.Services.Playback
{
    public static class GdMusicFallbackMatcher
    {
        public static string SearchTerm(TrackViewModel track)
        {
            string artist = DataUtils.SplitAndTrimColumnMultiValue(track?.Track?.Artists ?? string.Empty).FirstOrDefault();
            return string.IsNullOrWhiteSpace(track?.TrackTitle) || string.IsNullOrWhiteSpace(artist)
                ? null : track.TrackTitle + " " + artist;
        }

        public static GdMusicSearchResult Match(TrackViewModel track, string source,
            IEnumerable<GdMusicSearchResult> candidates)
        {
            string title = Normalize(track?.TrackTitle);
            var artists = new HashSet<string>(DataUtils.SplitAndTrimColumnMultiValue(track?.Track?.Artists ?? string.Empty)
                .Select(Normalize).Where(x => x.Length > 0));
            if (title.Length == 0 || artists.Count == 0) return null;
            // Keep version words (live/remix/instrumental etc.). Do not strip bracket contents.
            // The API does not supply duration, so ambiguous matches are deliberately rejected.
            var matches = (candidates ?? Enumerable.Empty<GdMusicSearchResult>()).Where(x =>
                x != null && !string.IsNullOrWhiteSpace(x.Id) &&
                string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase) &&
                Normalize(x.Name) == title &&
                artists.SetEquals((x.Artists ?? Array.Empty<string>()).Select(Normalize).Where(a => a.Length > 0)))
                .GroupBy(x => x.Id).Select(x => x.First()).ToList();
            if (matches.Count == 1) return matches[0];
            string album = Normalize(track?.Track?.AlbumTitle);
            if (album.Length == 0) return null;
            var albumMatches = matches.Where(x => Normalize(x.AlbumName) == album).ToList();
            return albumMatches.Count == 1 ? albumMatches[0] : null;
        }

        private static string Normalize(string value) => new string((value ?? string.Empty)
            .Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
