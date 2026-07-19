using Dopamine.Core.Audio;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class NeteasePlaybackSourceResolver
    {
        private readonly NeteaseAudioSourceResolver audioSourceResolver;
        private readonly NeteaseTemporaryAudioCache temporaryAudioCache;

        public NeteasePlaybackSourceResolver(
            NeteaseAudioSourceResolver audioSourceResolver,
            NeteaseTemporaryAudioCache temporaryAudioCache)
        {
            this.audioSourceResolver = audioSourceResolver;
            this.temporaryAudioCache = temporaryAudioCache;
        }

        public async Task<PlaybackSourceResolution> ResolveAsync(
            TrackViewModel track,
            PlaybackSourceRequest request,
            CancellationToken cancellationToken)
        {
            if (track?.SourceInfo == null || string.IsNullOrWhiteSpace(track.SourceInfo.RemoteId))
            {
                return Failure(PlaybackFailureReason.ApiChanged, "Language_Netease_Service_Unavailable");
            }

            bool forceRefresh = request != null && request.ForceRefresh;
            if (forceRefresh)
            {
                this.temporaryAudioCache.Invalidate(track.SourceInfo.RemoteId);
            }

            NeteaseAudioSourceResolution source = await this.audioSourceResolver.ResolveAsync(
                track,
                OnlineAudioSourcePriority.OfficialFirst,
                forceRefresh,
                cancellationToken);
            if (source == null || !source.IsSuccess)
            {
                return Failure(MapFailureReason(source?.Error), source?.Error?.MessageKey);
            }

            NeteaseResult<string> cached = await this.temporaryAudioCache.GetOrDownloadAsync(
                source.CacheKey,
                source.Url,
                source.MediaType,
                request?.BufferingProgress,
                cancellationToken);
            if (!cached.IsSuccess)
            {
                return Failure(MapFailureReason(cached.Error), cached.Error?.MessageKey);
            }

            return new PlaybackSourceResolution
            {
                IsSuccess = true,
                AudioSource = AudioSource.FromLocalFile(cached.Value)
            };
        }

        private static PlaybackSourceResolution Failure(PlaybackFailureReason reason, string messageKey)
        {
            return new PlaybackSourceResolution
            {
                IsSuccess = false,
                FailureReason = reason,
                MessageKey = messageKey ?? "Language_Netease_Service_Unavailable"
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
                case NeteaseErrorCode.AuthenticationRequired:
                    return PlaybackFailureReason.AuthenticationRequired;
                case NeteaseErrorCode.SessionExpired:
                    return PlaybackFailureReason.SessionExpired;
                case NeteaseErrorCode.NetworkUnavailable:
                    return PlaybackFailureReason.NetworkUnavailable;
                case NeteaseErrorCode.RateLimited:
                    return PlaybackFailureReason.RateLimited;
                case NeteaseErrorCode.NoCopyright:
                    return PlaybackFailureReason.NoCopyright;
                case NeteaseErrorCode.SubscriptionRequired:
                    return PlaybackFailureReason.SubscriptionRequired;
                case NeteaseErrorCode.TrialOnly:
                    return PlaybackFailureReason.TrialOnly;
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
