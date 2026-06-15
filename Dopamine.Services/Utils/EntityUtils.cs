using Digimezzo.Foundation.Core.Utils;
using Dopamine.Core.Base;
using Dopamine.Core.Utils;
using Dopamine.Data;
using Dopamine.Data.Entities;
using Dopamine.Data.Metadata;
using Dopamine.Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dopamine.Services.Utils
{
    public static class EntityUtils
    {
        public static bool FilterAlbums(AlbumViewModel album, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => 
            album.AlbumTitle.ToLower().Contains(s.ToLower()) | 
            album.AlbumArtist.ToLower().Contains(s.ToLower()) | 
            album.Year.ToString().ToLower().Contains(s.ToLower()));
        }

        public static bool FilterArtists(ArtistViewModel artist, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => artist.ArtistName.ToLower().Contains(s.ToLower()));
        }

        public static bool FilterGenres(GenreViewModel genre, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => genre.GenreName.ToLower().Contains(s.ToLower()));
        }

        public static bool FilterTracks(TrackViewModel track, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => 
            track.TrackTitle.ToLower().Contains(s.ToLower()) | 
            track.ArtistName.ToLower().Contains(s.ToLower()) | 
            track.AlbumTitle.ToLower().Contains(s.ToLower()) | 
            track.FileName.ToLower().Contains(s.ToLower()) | 
            track.Year.ToString().Contains(s.ToLower()));
        }

        private static string GetTrackAlbumArtist(Track track)
        {
            if (track == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(track.AlbumArtists))
            {
                return DataUtils.GetCommaSeparatedColumnMultiValue(track.AlbumArtists);
            }

            if (!string.IsNullOrEmpty(track.Artists))
            {
                return DataUtils.GetCommaSeparatedColumnMultiValue(track.Artists);
            }

            return ResourceUtils.GetString("Language_Unknown_Artist");
        }

        private static string GetTrackAlbumTitle(Track track)
        {
            return track != null && !string.IsNullOrEmpty(track.AlbumTitle) ? track.AlbumTitle : ResourceUtils.GetString("Language_Unknown_Album");
        }

        private static string GetTrackFileName(Track track)
        {
            return track != null ? track.FileName : string.Empty;
        }

        private static string GetTrackSortDiscNumber(Track track)
        {
            return track != null && track.DiscNumber.HasValue && track.DiscNumber.Value > 0 ? track.DiscNumber.Value.ToString("0000") : string.Empty;
        }

        private static long GetTrackSortTrackNumber(Track track)
        {
            return track != null && track.TrackNumber.HasValue ? track.TrackNumber.Value : 0;
        }

        private static string GetTrackTitle(Track track)
        {
            return track != null && !string.IsNullOrEmpty(track.TrackTitle) ? track.TrackTitle : GetTrackFileName(track);
        }

        private static int GetTrackRating(Track track)
        {
            return track != null && track.Rating.HasValue ? Convert.ToInt32(track.Rating.Value) : 0;
        }

        public static async Task<List<Track>> OrderTrackEntitiesAsync(IList<Track> tracks, TrackOrder trackOrder)
        {
            var orderedTracks = new List<Track>();

            if (tracks == null)
            {
                return orderedTracks;
            }

            await Task.Run(() =>
            {
                switch (trackOrder)
                {
                    case TrackOrder.Alphabetical:
                        orderedTracks = tracks.OrderBy((t) => !string.IsNullOrEmpty(FormatUtils.GetSortableString(GetTrackTitle(t))) ? FormatUtils.GetSortableString(GetTrackTitle(t)) : FormatUtils.GetSortableString(GetTrackFileName(t))).ToList();
                        break;
                    case TrackOrder.ByAlbum:
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(GetTrackAlbumArtist(t))).ThenBy((t) => FormatUtils.GetSortableString(GetTrackAlbumTitle(t))).ThenBy((t) => GetTrackSortDiscNumber(t)).ThenBy((t) => GetTrackSortTrackNumber(t)).ToList();
                        break;
                    case TrackOrder.ByFileName:
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(GetTrackFileName(t))).ToList();
                        break;
                    case TrackOrder.ByRating:
                        orderedTracks = tracks.OrderByDescending((t) => GetTrackRating(t)).ToList();
                        break;
                    case TrackOrder.ReverseAlphabetical:
                        orderedTracks = tracks.OrderByDescending((t) => !string.IsNullOrEmpty(FormatUtils.GetSortableString(GetTrackTitle(t))) ? FormatUtils.GetSortableString(GetTrackTitle(t)) : FormatUtils.GetSortableString(GetTrackFileName(t))).ToList();
                        break;
                    case TrackOrder.None:
                        orderedTracks = tracks.ToList();
                        break;
                    default:
                        // By album
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(GetTrackAlbumTitle(t))).ThenBy((t) => GetTrackSortDiscNumber(t)).ThenBy((t) => GetTrackSortTrackNumber(t)).ToList();
                        break;
                }
            });

            return orderedTracks;
        }

        public static async Task<List<TrackViewModel>> OrderTracksAsync(IList<TrackViewModel> tracks, TrackOrder trackOrder)
        {
            var orderedTracks = new List<TrackViewModel>();

            await Task.Run(() =>
            {
                switch (trackOrder)
                {
                    case TrackOrder.Alphabetical:
                        orderedTracks = tracks.OrderBy((t) => !string.IsNullOrEmpty(FormatUtils.GetSortableString(t.TrackTitle)) ? FormatUtils.GetSortableString(t.TrackTitle) : FormatUtils.GetSortableString(t.FileName)).ToList();
                        break;
                    case TrackOrder.ByAlbum:
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(t.AlbumArtist)).ThenBy((t) => FormatUtils.GetSortableString(t.AlbumTitle)).ThenBy((t) => t.SortDiscNumber).ThenBy((t) => t.SortTrackNumber).ToList();
                        break;
                    case TrackOrder.ByFileName:
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(t.FileName)).ToList();
                        break;
                    case TrackOrder.ByRating:
                        orderedTracks = tracks.OrderByDescending((t) => t.Rating).ToList();
                        break;
                    case TrackOrder.ReverseAlphabetical:
                        orderedTracks = tracks.OrderByDescending((t) => !string.IsNullOrEmpty(FormatUtils.GetSortableString(t.TrackTitle)) ? FormatUtils.GetSortableString(t.TrackTitle) : FormatUtils.GetSortableString(t.FileName)).ToList();
                        break;
                    case TrackOrder.None:
                        orderedTracks = tracks.ToList();
                        break;
                    default:
                        // By album
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(t.AlbumTitle)).ThenBy((t) => t.SortDiscNumber).ThenBy((t) => t.SortTrackNumber).ToList();
                        break;
                }
            });

            return orderedTracks;
        }
    }
}
