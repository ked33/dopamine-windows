using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Utils;
using Dopamine.Data.Entities;
using Dopamine.Services.Entities;
using Dopamine.Services.Extensions;
using Dopamine.Services.Online.Netease;
using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dopamine.Services.Online.GdMusic
{
    public static class GdMusicTrackFactory
    {
        public static TrackViewModel Create(
            IContainerProvider container,
            GdMusicSearchResult song)
        {
            if (container == null || song == null || string.IsNullOrWhiteSpace(song.Id))
            {
                return null;
            }

            string source = GdMusicSettings.NormalizeSource(song.Source);

            if (string.Equals(source, "netease", StringComparison.Ordinal))
            {
                // Netease results reuse the native Netease pipeline (playback, song
                // information and lyrics), so they get the same source shape as the
                // recommendation tracks.
                var neteaseSong = new NeteaseRecommendedSong
                {
                    Id = song.Id,
                    Name = song.Name,
                    Artists = song.Artists ?? new List<string>(),
                    AlbumName = song.AlbumName,
                    DurationMilliseconds = 0,
                    ArtworkUrl = null
                };

                TrackViewModel neteaseViewModel = NeteaseTrackFactory.Create(container, neteaseSong);
                if (neteaseViewModel != null && neteaseViewModel.SourceInfo != null)
                {
                    neteaseViewModel.SourceInfo.PictureId = song.PictureId;
                }

                return neteaseViewModel;
            }

            string path = "gdmusic://" + source + "/" + song.Id;
            string artists = string.Join(
                string.Empty,
                (song.Artists ?? new List<string>()).Select(FormatUtils.DelimitValue));
            var track = new Track
            {
                Path = path,
                SafePath = path,
                FileName = song.Name ?? string.Empty,
                TrackTitle = song.Name ?? string.Empty,
                Artists = artists,
                AlbumArtists = artists,
                AlbumTitle = song.AlbumName ?? string.Empty,
                AlbumKey = FormatUtils.DelimitValue(song.AlbumName ?? string.Empty) + artists.ToLowerInvariant(),
                TrackNumber = 0,
                TrackCount = 0,
                DiscNumber = 0,
                DiscCount = 0,
                Duration = 0,
                Year = 0,
                HasLyrics = 0,
                FileSize = 0,
                BitRate = 0,
                SampleRate = 0,
                DateAdded = DateTime.Now.Ticks,
                DateFileCreated = 0,
                DateLastSynced = 0,
                DateFileModified = 0,
                Rating = 0,
                Love = 0,
                PlayCount = 0,
                SkipCount = 0
            };

            TrackViewModel viewModel = container.ResolveTrackViewModel(track);
            viewModel.SourceInfo = new TrackSourceInfo
            {
                Kind = TrackSourceKind.ExternalOnline,
                ProviderId = source,
                RemoteId = song.Id,
                ArtworkUrl = null,
                PictureId = song.PictureId
            };
            return viewModel;
        }
    }
}
