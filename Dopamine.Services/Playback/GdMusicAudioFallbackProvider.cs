using Dopamine.Services.Entities;
using Dopamine.Services.Online.GdMusic;
using Dopamine.Services.Online.Netease;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class GdMusicAudioFallbackProvider : IOnlineAudioFallbackProvider
    {
        private const int MaximumCacheEntries = 128;
        private readonly IGdMusicApiClient apiClient;
        private readonly SemaphoreSlim requestLock = new SemaphoreSlim(1, 1);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>();

        public GdMusicAudioFallbackProvider(IGdMusicApiClient apiClient)
        {
            this.apiClient = apiClient;
        }

        public string Id => "gdstudio";

        public int Order => 50;

        public bool CanHandle(NeteaseError officialFailure)
        {
            switch (officialFailure?.Code)
            {
                case NeteaseErrorCode.NoCopyright:
                case NeteaseErrorCode.EmptyUrl:
                case NeteaseErrorCode.SubscriptionRequired:
                case NeteaseErrorCode.TrialOnly:
                    return true;
                default:
                    return false;
            }
        }

        public async Task<OnlineAudioFallbackResult> TryResolveAsync(
            OnlineAudioFallbackRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request?.Track?.SourceInfo?.Kind != TrackSourceKind.Netease ||
                (!request.AllowWithoutOfficialFailure && !this.CanHandle(request.OfficialFailure)) ||
                AudioFallbackCatalog.Find("gd:" + request.Source) == null)
            {
                return OnlineAudioFallbackResult.Failure("not_applicable");
            }

            string songId = request.Track.SourceInfo.RemoteId;
            if (string.IsNullOrWhiteSpace(songId) || songId.Any(c => c < '0' || c > '9'))
            {
                return OnlineAudioFallbackResult.Failure("invalid_song_id");
            }

            string cacheKey = songId + ":" + request.Source + ":" + request.GdQuality;
            await this.requestLock.WaitAsync(cancellationToken);
            try
            {
                CacheEntry entry;
                if (!request.ForceRefresh && this.cache.TryGetValue(cacheKey, out entry) &&
                    entry.ExpiresAt > this.clock.ElapsedMilliseconds)
                {
                    return entry.Result;
                }

                // Remove the old URL before a forced retry, including when it is cancelled.
                this.cache.Remove(cacheKey);
                OnlineAudioFallbackResult result = await this.ResolveSourceAsync(request, songId, cancellationToken);
                if (result.ErrorCode != "gd_RateLimited")
                {
                    if (this.cache.Count >= MaximumCacheEntries)
                    {
                        this.cache.Remove(this.cache.OrderBy(x => x.Value.ExpiresAt).First().Key);
                    }

                    this.cache[cacheKey] = new CacheEntry
                    {
                        Result = result,
                        ExpiresAt = this.clock.ElapsedMilliseconds + (result.IsSuccess ? 60000 : 10000)
                    };
                }

                return result;
            }
            finally
            {
                this.requestLock.Release();
            }
        }

        private async Task<OnlineAudioFallbackResult> ResolveSourceAsync(
            OnlineAudioFallbackRequest request, string songId, CancellationToken cancellationToken)
        {
            string resolvedId = songId;
            if (request.Source != "netease")
            {
                string keyword = GdMusicFallbackMatcher.SearchTerm(request.Track);
                if (keyword == null) return OnlineAudioFallbackResult.Failure("gd_missing_metadata");
                var search = await this.apiClient.SearchAsync(request.Source, keyword, 10, 1, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (search?.Error?.Code == NeteaseErrorCode.Cancelled)
                    throw new OperationCanceledException(cancellationToken);
                if (search == null || !search.IsSuccess)
                    return OnlineAudioFallbackResult.Failure("gd_" + (search?.Error?.Code.ToString() ?? "empty_response"));
                var match = GdMusicFallbackMatcher.Match(request.Track, request.Source, search.Value);
                if (match == null) return OnlineAudioFallbackResult.Failure("gd_no_confident_match");
                resolvedId = match.Id;
            }
            var response = await this.apiClient.GetTrackUrlAsync(request.Source, resolvedId, request.GdQuality, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (response?.Error?.Code == NeteaseErrorCode.Cancelled)
                throw new OperationCanceledException(cancellationToken);
            return MapResponse(response, request.Source);
        }

        private static OnlineAudioFallbackResult MapResponse(NeteaseResult<GdMusicTrackUrl> response, string source)
        {
            if (response == null || !response.IsSuccess || response.Value == null)
            {
                return OnlineAudioFallbackResult.Failure("gd_" + (response?.Error?.Code.ToString() ?? "empty_response"));
            }

            GdMusicTrackUrl audio = response.Value;
            Uri uri;
            if (!Uri.TryCreate(audio.Url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return OnlineAudioFallbackResult.Failure("gd_invalid_url");
            }

            return new OnlineAudioFallbackResult
            {
                IsSuccess = true,
                Url = audio.Url,
                ProviderId = "gdstudio-" + source,
                CacheVariant = "quality-" + audio.BitRate,
                // GD uses 740/999 as quality selectors, not actual audio bitrates.
                Bitrate = audio.BitRate > 0 && audio.BitRate <= 320 ? audio.BitRate * 1000L : 0,
                // The live API's size matches HTTP Content-Length (bytes), despite
                // the API document describing it as KB. Do not multiply by 1024.
                Size = Math.Max(0, audio.SizeBytes)
            };
        }

        private sealed class CacheEntry
        {
            public OnlineAudioFallbackResult Result { get; set; }

            public long ExpiresAt { get; set; }
        }
    }
}
