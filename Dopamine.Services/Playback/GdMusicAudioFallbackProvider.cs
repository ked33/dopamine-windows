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
        private const int RequestedBitrate = 320;
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
            // Only follow an eligible official failure. In particular, do not change
            // the explicit Unblock-first download policy or mask login/network errors.
            if (request?.Track?.SourceInfo?.Kind != TrackSourceKind.Netease ||
                !this.CanHandle(request.OfficialFailure))
            {
                return OnlineAudioFallbackResult.Failure("not_applicable");
            }

            string songId = request.Track.SourceInfo.RemoteId;
            if (string.IsNullOrWhiteSpace(songId) || songId.Any(c => c < '0' || c > '9'))
            {
                return OnlineAudioFallbackResult.Failure("invalid_song_id");
            }

            await this.requestLock.WaitAsync(cancellationToken);
            try
            {
                CacheEntry entry;
                if (!request.ForceRefresh && this.cache.TryGetValue(songId, out entry) &&
                    entry.ExpiresAt > this.clock.ElapsedMilliseconds)
                {
                    return entry.Result;
                }

                // Remove the old URL before a forced retry, including when it is cancelled.
                this.cache.Remove(songId);
                var response = await this.apiClient.GetTrackUrlAsync(
                    "netease", songId, RequestedBitrate, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (response?.Error?.Code == NeteaseErrorCode.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                OnlineAudioFallbackResult result = MapResponse(response);
                if (response?.Error?.Code != NeteaseErrorCode.RateLimited)
                {
                    if (this.cache.Count >= MaximumCacheEntries)
                    {
                        this.cache.Remove(this.cache.OrderBy(x => x.Value.ExpiresAt).First().Key);
                    }

                    this.cache[songId] = new CacheEntry
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

        private static OnlineAudioFallbackResult MapResponse(NeteaseResult<GdMusicTrackUrl> response)
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
                ProviderId = "gdstudio-netease",
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
