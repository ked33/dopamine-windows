using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Base;
using Dopamine.Core.Utils;
using Dopamine.Data.Entities;
using Dopamine.Services.Entities;
using Dopamine.Services.Extensions;
using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dopamine.Services.Online.Netease
{
    public static class NeteaseTrackFactory
    {
        public static TrackViewModel Create(
            IContainerProvider container,
            NeteaseRecommendedSong song)
        {
            if (container == null || song == null || string.IsNullOrWhiteSpace(song.Id))
            {
                return null;
            }

            string path = "netease://song/" + song.Id;
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
                Duration = song.DurationMilliseconds,
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
                Kind = TrackSourceKind.Netease,
                ProviderId = "netease",
                RemoteId = song.Id,
                ArtworkUrl = song.ArtworkUrl
            };
            return viewModel;
        }
    }
}
