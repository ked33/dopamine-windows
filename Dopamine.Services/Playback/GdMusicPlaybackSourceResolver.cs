using Dopamine.Core.Audio;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.GdMusic;
using Dopamine.Services.Online.Netease;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    /// <summary>
    /// Resolves playback audio for online search tracks whose source is not
    /// Netease. The audio URL is obtained from the GD music platform API and
    /// buffered through the shared temporary audio cache.
    /// </summary>
    public sealed class GdMusicPlaybackSourceResolver
    {
        private readonly IGdMusicApiClient apiClient;
        private readonly NeteaseTemporaryAudioCache temporaryAudioCache;

        public GdMusicPlaybackSourceResolver(
            IGdMusicApiClient apiClient,
            NeteaseTemporaryAudioCache temporaryAudioCache)
        {
            this.apiClient = apiClient;
            this.temporaryAudioCache = temporaryAudioCache;
        }

        public async Task<PlaybackSourceResolution> ResolveAsync(
            TrackViewModel track,
            PlaybackSourceRequest request,
            CancellationToken cancellationToken)
        {
            if (track == null || track.SourceInfo == null ||
                track.SourceInfo.Kind != TrackSourceKind.ExternalOnline ||
                string.IsNullOrWhiteSpace(track.SourceInfo.RemoteId))
            {
                return Failure(PlaybackFailureReason.ApiChanged, "Language_GdMusic_Search_Failed");
            }

            string source = track.SourceInfo.ProviderId;
            string trackId = track.SourceInfo.RemoteId;
            int bitRate = GdMusicSettings.DownloadQuality;

            bool forceRefresh = request != null && request.ForceRefresh;
            if (forceRefresh)
            {
                this.temporaryAudioCache.Invalidate(trackId);
            }

            NeteaseResult<GdMusicTrackUrl> urlResult = await this.apiClient.GetTrackUrlAsync(
                source,
                trackId,
                bitRate,
                cancellationToken);
            if (!urlResult.IsSuccess)
            {
                return Failure(MapFailureReason(urlResult.Error), GetMessageKey(urlResult.Error));
            }

            string cacheKey = string.Format(
                "gd-{0}-{1}-{2}",
                GdMusicSettings.NormalizeSource(source),
                urlResult.Value.BitRate > 0 ? urlResult.Value.BitRate : bitRate,
                trackId);
            NeteaseResult<string> cached = await this.temporaryAudioCache.GetOrDownloadAsync(
                cacheKey,
                urlResult.Value.Url,
                null,
                request == null ? null : request.BufferingProgress,
                cancellationToken);
            if (!cached.IsSuccess)
            {
                return Failure(MapFailureReason(cached.Error), GetMessageKey(cached.Error));
            }

            return new PlaybackSourceResolution
            {
                IsSuccess = true,
                AudioSource = AudioSource.FromLocalFile(cached.Value)
            };
        }

        private static string GetMessageKey(NeteaseError error)
        {
            return error == null || string.IsNullOrWhiteSpace(error.MessageKey)
                ? "Language_GdMusic_Search_Failed"
                : error.MessageKey;
        }

        private static PlaybackSourceResolution Failure(PlaybackFailureReason reason, string messageKey)
        {
            return new PlaybackSourceResolution
            {
                IsSuccess = false,
                FailureReason = reason,
                MessageKey = messageKey
            };
        }

        private static PlaybackFailureReason MapFailureReason(NeteaseError error)
        {
            if (error == null)
            {
                return PlaybackFailureReason.Unknown;
            }

            switch (error.Code)
            {
                case NeteaseErrorCode.NetworkUnavailable:
                    return PlaybackFailureReason.NetworkUnavailable;
                case NeteaseErrorCode.RateLimited:
                    return PlaybackFailureReason.RateLimited;
                case NeteaseErrorCode.EmptyUrl:
                    return PlaybackFailureReason.EmptyUrl;
                case NeteaseErrorCode.ApiChanged:
                case NeteaseErrorCode.EmptyResponse:
                    return PlaybackFailureReason.ApiChanged;
                case NeteaseErrorCode.TemporaryDownloadFailed:
                    return PlaybackFailureReason.TemporaryDownloadFailed;
                case NeteaseErrorCode.Cancelled:
                    return PlaybackFailureReason.Cancelled;
                default:
                    return PlaybackFailureReason.Unknown;
            }
        }
    }
}
