using Dopamine.Core.Logging;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class NeteaseAudioSourceResolver
    {
        private readonly INeteaseMusicService musicService;
        private readonly IList<IOnlineAudioFallbackProvider> fallbackProviders;

        public NeteaseAudioSourceResolver(
            INeteaseMusicService musicService,
            IEnumerable<IOnlineAudioFallbackProvider> fallbackProviders)
        {
            this.musicService = musicService;
            this.fallbackProviders = (fallbackProviders ?? Enumerable.Empty<IOnlineAudioFallbackProvider>())
                .OrderBy(x => x.Order)
                .ToList();
        }

        public async Task<NeteaseAudioSourceResolution> ResolveAsync(
            TrackViewModel track,
            OnlineAudioSourcePriority priority,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            string songId = track?.SourceInfo?.RemoteId;
            if (track?.SourceInfo == null || track.SourceInfo.Kind != TrackSourceKind.Netease ||
                string.IsNullOrWhiteSpace(songId))
            {
                return NeteaseAudioSourceResolution.Failure(
                    songId,
                    new NeteaseError(NeteaseErrorCode.ApiChanged, "Language_Netease_Service_Unavailable"));
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (priority == OnlineAudioSourcePriority.UnblockFirst)
            {
                NeteaseAudioSourceResolution proactiveFallback = await this.TryFallbackAsync(
                    track,
                    null,
                    true,
                    forceRefresh,
                    cancellationToken);
                if (proactiveFallback.IsSuccess)
                {
                    return proactiveFallback;
                }

                if (proactiveFallback.Error?.Code == NeteaseErrorCode.Cancelled)
                {
                    return proactiveFallback;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            NeteaseAudioResolution official = await this.musicService.ResolveOfficialAudioAsync(
                songId,
                forceRefresh,
                cancellationToken);
            if (official != null && official.IsSuccess && !string.IsNullOrWhiteSpace(official.Url))
            {
                return new NeteaseAudioSourceResolution
                {
                    IsSuccess = true,
                    SongId = official.SongId ?? songId,
                    Url = official.Url,
                    ProviderId = "netease",
                    MediaType = official.Type,
                    CacheVariant = official.QualityLevel,
                    CacheKey = string.Format(
                        "official-{0}-{1}",
                        string.IsNullOrWhiteSpace(official.QualityLevel) ? "default" : official.QualityLevel,
                        official.SongId ?? songId),
                    QualityLevel = official.QualityLevel,
                    BitRate = official.BitRate,
                    Size = official.Size
                };
            }

            NeteaseError officialError = official?.Error ?? new NeteaseError(
                NeteaseErrorCode.EmptyResponse,
                "Language_Netease_Service_Unavailable");

            if (officialError.Code == NeteaseErrorCode.Cancelled)
            {
                return NeteaseAudioSourceResolution.Failure(songId, officialError);
            }

            if (priority != OnlineAudioSourcePriority.UnblockFirst)
            {
                NeteaseAudioSourceResolution fallback = await this.TryFallbackAsync(
                    track,
                    officialError,
                    false,
                    forceRefresh,
                    cancellationToken);
                if (fallback.IsSuccess)
                {
                    return fallback;
                }
            }

            return NeteaseAudioSourceResolution.Failure(songId, officialError);
        }

        private async Task<NeteaseAudioSourceResolution> TryFallbackAsync(
            TrackViewModel track,
            NeteaseError officialFailure,
            bool allowWithoutOfficialFailure,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            string songId = track?.SourceInfo?.RemoteId;
            foreach (IOnlineAudioFallbackProvider provider in this.fallbackProviders)
            {
                if (!allowWithoutOfficialFailure &&
                    (officialFailure == null || !provider.CanHandle(officialFailure)))
                {
                    continue;
                }

                OnlineAudioFallbackResult fallback;
                try
                {
                    fallback = await provider.TryResolveAsync(
                        new OnlineAudioFallbackRequest
                        {
                            Track = track,
                            OfficialFailure = officialFailure,
                            ForceRefresh = forceRefresh,
                            AllowWithoutOfficialFailure = allowWithoutOfficialFailure
                        },
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return NeteaseAudioSourceResolution.Failure(
                        songId,
                        new NeteaseError(NeteaseErrorCode.Cancelled, "Language_Netease_Cancelled"));
                }
                catch (Exception ex)
                {
                    AppLog.Warning(
                        "Online audio fallback failed. Provider={0}, ErrorType={1}",
                        provider.Id,
                        ex.GetType().Name);
                    continue;
                }

                if (fallback != null && fallback.IsSuccess && !string.IsNullOrWhiteSpace(fallback.Url))
                {
                    string providerId = string.IsNullOrWhiteSpace(fallback.ProviderId)
                        ? provider.Id
                        : fallback.ProviderId;
                    string cacheVariant = string.IsNullOrWhiteSpace(fallback.CacheVariant)
                        ? "default"
                        : fallback.CacheVariant;

                    return new NeteaseAudioSourceResolution
                    {
                        IsSuccess = true,
                        SongId = songId,
                        Url = fallback.Url,
                        ProviderId = providerId,
                        MediaType = fallback.MediaType,
                        CacheVariant = cacheVariant,
                        CacheKey = string.Format(
                            "fallback-{0}-{1}-{2}",
                            providerId,
                            cacheVariant,
                            songId),
                        BitRate = fallback.Bitrate,
                        Size = fallback.Size
                    };
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return NeteaseAudioSourceResolution.Failure(songId, null);
        }
    }
}
