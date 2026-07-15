using Dopamine.Core.Audio;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class NeteasePlaybackSourceResolver
    {
        private readonly INeteaseMusicService musicService;
        private readonly NeteaseTemporaryAudioCache temporaryAudioCache;
        private readonly IList<IOnlineAudioFallbackProvider> fallbackProviders;

        public NeteasePlaybackSourceResolver(
            INeteaseMusicService musicService,
            NeteaseTemporaryAudioCache temporaryAudioCache,
            IEnumerable<IOnlineAudioFallbackProvider> fallbackProviders)
        {
            this.musicService = musicService;
            this.temporaryAudioCache = temporaryAudioCache;
            this.fallbackProviders = (fallbackProviders ?? Enumerable.Empty<IOnlineAudioFallbackProvider>())
                .OrderBy(x => x.Order)
                .ToList();
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

            if (request != null && request.ForceRefresh)
            {
                this.temporaryAudioCache.Invalidate(track.SourceInfo.RemoteId);
            }

            NeteaseAudioResolution official = await this.musicService.ResolveOfficialAudioAsync(
                track.SourceInfo.RemoteId,
                request != null && request.ForceRefresh,
                cancellationToken);

            if (!official.IsSuccess)
            {
                if (official.Error != null &&
                    (official.Error.Code == NeteaseErrorCode.NoCopyright || official.Error.Code == NeteaseErrorCode.EmptyUrl))
                {
                    foreach (IOnlineAudioFallbackProvider provider in this.fallbackProviders)
                    {
                        if (!provider.CanHandle(official.Error))
                        {
                            continue;
                        }

                        PlaybackSourceResolution fallback = await provider.TryResolveAsync(
                            track.SourceInfo,
                            official.Error,
                            cancellationToken);

                        if (fallback != null && fallback.IsSuccess)
                        {
                            return fallback;
                        }
                    }
                }

                return Failure(MapFailureReason(official.Error), official.Error?.MessageKey);
            }

            // Direct CSCore HTTPS playback has not passed the required runtime probe on this machine.
            // Use the documented temporary-file compatibility path until that probe is completed.
            NeteaseResult<string> cached = await this.temporaryAudioCache.GetOrDownloadAsync(
                official.SongId,
                official.Url,
                official.Type,
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
