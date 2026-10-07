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
        private readonly IAudioFallbackSettings settings;

        public NeteaseAudioSourceResolver(
            INeteaseMusicService musicService,
            IEnumerable<IOnlineAudioFallbackProvider> fallbackProviders,
            IAudioFallbackSettings settings)
        {
            this.musicService = musicService;
            this.settings = settings;
            this.fallbackProviders = (fallbackProviders ?? Enumerable.Empty<IOnlineAudioFallbackProvider>())
                .ToList();
        }

        public async Task<NeteaseAudioSourceResolution> ResolveAsync(
            TrackViewModel track,
            OnlineAudioSourcePriority priority,
            bool forceRefresh,
            CancellationToken cancellationToken,
            ISet<string> excludedSources = null)
        {
            excludedSources = excludedSources ?? new HashSet<string>(StringComparer.Ordinal);
            string songId = track?.SourceInfo?.RemoteId;
            if (track?.SourceInfo == null || track.SourceInfo.Kind != TrackSourceKind.Netease ||
                string.IsNullOrWhiteSpace(songId))
            {
                return NeteaseAudioSourceResolution.Failure(
                    songId,
                    new NeteaseError(NeteaseErrorCode.ApiChanged, "Language_Netease_Service_Unavailable"));
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool fallbackFirst = priority == OnlineAudioSourcePriority.FallbackFirst || track.SourceInfo.PreferFallbackAudio;
            if (fallbackFirst)
            {
                NeteaseAudioSourceResolution proactiveFallback = await this.TryFallbackAsync(
                    track,
                    null,
                    true,
                    forceRefresh,
                    cancellationToken, excludedSources);
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
            NeteaseAudioResolution official = excludedSources.Contains("official") ? null : await this.musicService.ResolveOfficialAudioAsync(
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
                    ConfiguredSourceId = "official",
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
                excludedSources.Contains("official") ? NeteaseErrorCode.EmptyUrl : NeteaseErrorCode.EmptyResponse,
                "Language_Netease_Service_Unavailable");

            if (officialError.Code == NeteaseErrorCode.Cancelled)
            {
                return NeteaseAudioSourceResolution.Failure(songId, officialError);
            }

            if (!fallbackFirst)
            {
                NeteaseAudioSourceResolution fallback = await this.TryFallbackAsync(
                    track,
                    officialError,
                    false,
                    forceRefresh,
                    cancellationToken, excludedSources);
                if (fallback.IsSuccess)
                {
                    return fallback;
                }

                if (fallback.Error?.Code == NeteaseErrorCode.Cancelled)
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
            CancellationToken cancellationToken,
            ISet<string> excludedSources)
        {
            string songId = track?.SourceInfo?.RemoteId;
            AudioFallbackConfiguration configuration = this.settings.Current;
            if (!configuration.Enabled) return NeteaseAudioSourceResolution.Failure(songId, null);
            bool gdRateLimited = false;
            foreach (AudioFallbackSource row in configuration.Sources.Where(x => x.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var beforeRequest = this.settings.Current;
                if (!beforeRequest.Enabled) break;
                if (!beforeRequest.Sources.Any(x => x.Id == row.Id && x.Enabled)) continue;
                var definition = AudioFallbackCatalog.Find(row.Id);
                if (definition == null || excludedSources.Contains(row.Id)) continue;
                if (gdRateLimited && definition.ProviderId == "gdstudio")
                {
                    excludedSources.Add(row.Id);
                    continue;
                }
                var provider = this.fallbackProviders.FirstOrDefault(x => x.Id == definition.ProviderId);
                if (provider == null) continue;
                if (!allowWithoutOfficialFailure &&
                    (officialFailure == null || !provider.CanHandle(officialFailure)))
                {
                    continue;
                }

                OnlineAudioFallbackResult fallback;
                try
                {
                    AppLog.Info("Trying online audio fallback. Provider={0}, SongId={1}, OfficialFailure={2}",
                        row.Id, songId, officialFailure?.Code.ToString() ?? "none");
                    fallback = await provider.TryResolveAsync(
                        new OnlineAudioFallbackRequest
                        {
                            Track = track,
                            Source = definition.Source,
                            GdQuality = configuration.GdQuality,
                            UnblockEnableFlac = configuration.UnblockEnableFlac,
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
                    excludedSources.Add(row.Id);
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var current = this.settings.Current;
                if (!current.Enabled || !current.Sources.Any(x => x.Id == row.Id && x.Enabled)) continue;
                if (fallback != null && fallback.IsSuccess && !string.IsNullOrWhiteSpace(fallback.Url))
                {
                    AppLog.Info("Online audio fallback succeeded. Provider={0}, SongId={1}", provider.Id, songId);
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
                        ConfiguredSourceId = row.Id,
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

                if (fallback?.ErrorCode == "gd_RateLimited") gdRateLimited = true;
                excludedSources.Add(row.Id);
                AppLog.Info("Online audio fallback did not resolve a source. Provider={0}, SongId={1}, Reason={2}",
                    row.Id, songId, fallback?.ErrorCode ?? "empty_response");
                cancellationToken.ThrowIfCancellationRequested();
            }

            return NeteaseAudioSourceResolution.Failure(songId, null);
        }
    }
}
