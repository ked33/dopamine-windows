using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public sealed class NeteaseMusicService : INeteaseMusicService
    {
        private sealed class CacheEntry<T>
        {
            public DateTime ExpiresAtUtc { get; set; }

            public T Value { get; set; }
        }

        private readonly object cacheLock = new object();
        private readonly INeteaseApiClient apiClient;
        private readonly INeteaseSessionService sessionService;
        private readonly INeteaseRecommendationStore recommendationStore;

        private long recommendationsGeneration = -1;
        private DateTime recommendationsDate;
        private IReadOnlyList<NeteaseRecommendedSong> recommendations;
        private readonly Dictionary<string, Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>>>
            recommendationsInFlight =
                new Dictionary<string, Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>>>(
                    StringComparer.Ordinal);

        private readonly Dictionary<string, CacheEntry<NeteaseAudioResolution>> audioCache =
            new Dictionary<string, CacheEntry<NeteaseAudioResolution>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<NeteaseAudioResolution>> audioInFlight =
            new Dictionary<string, Task<NeteaseAudioResolution>>(StringComparer.Ordinal);

        private readonly Dictionary<string, CacheEntry<NeteaseLyricResult>> lyricCache =
            new Dictionary<string, CacheEntry<NeteaseLyricResult>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Task<NeteaseLyricResult>> lyricInFlight =
            new Dictionary<string, Task<NeteaseLyricResult>>(StringComparer.Ordinal);

        public NeteaseMusicService(
            INeteaseApiClient apiClient,
            INeteaseSessionService sessionService,
            INeteaseRecommendationStore recommendationStore)
        {
            this.apiClient = apiClient;
            this.sessionService = sessionService;
            this.recommendationStore = recommendationStore;
            this.sessionService.SessionChanged += (_, __) => this.HandleSessionChanged();
        }

        public async Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> GetDailyRecommendationsAsync(
            CancellationToken cancellationToken)
        {
            NeteaseError sessionError = this.GetSessionError();

            if (sessionError != null)
            {
                return NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Failure(sessionError);
            }

            long generation = this.sessionService.SessionGeneration;
            DateTime recommendationDate = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                DateTimeOffset.UtcNow);
            string accountUserId = this.sessionService.Account?.UserId ?? string.Empty;
            string recommendationKey = generation + "|" + recommendationDate.ToString("yyyyMMdd") + "|" + accountUserId;
            Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> task;

            lock (this.cacheLock)
            {
                if (this.recommendations != null && this.recommendationsGeneration == generation &&
                    this.recommendationsDate == recommendationDate)
                {
                    return NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Success(this.recommendations);
                }

                if (!this.recommendationsInFlight.TryGetValue(recommendationKey, out task))
                {
                    task = this.LoadOrFetchRecommendationsAsync(
                        generation,
                        recommendationDate,
                        accountUserId,
                        cancellationToken);
                    this.recommendationsInFlight[recommendationKey] = task;
                }
            }

            try
            {
                NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>> result = await task;

                if (!result.IsSuccess && IsAuthenticationError(result.Error))
                {
                    await this.sessionService.ExpireAsync(generation);
                }

                if (result.IsSuccess && generation == this.sessionService.SessionGeneration &&
                    recommendationDate == NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                        DateTimeOffset.UtcNow))
                {
                    lock (this.cacheLock)
                    {
                        this.recommendations = result.Value;
                        this.recommendationsGeneration = generation;
                        this.recommendationsDate = recommendationDate;
                    }
                }

                return result;
            }
            finally
            {
                lock (this.cacheLock)
                {
                    Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> current;

                    if (this.recommendationsInFlight.TryGetValue(recommendationKey, out current) &&
                        object.ReferenceEquals(current, task))
                    {
                        this.recommendationsInFlight.Remove(recommendationKey);
                    }
                }
            }
        }

        public async Task<NeteaseAudioResolution> ResolveOfficialAudioAsync(
            string songId,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            NeteaseError sessionError = this.GetSessionError();

            if (sessionError != null)
            {
                return new NeteaseAudioResolution { SongId = songId, Error = sessionError };
            }

            long generation = this.sessionService.SessionGeneration;
            string key = generation + "|standard|" + songId;
            Task<NeteaseAudioResolution> task;

            lock (this.cacheLock)
            {
                CacheEntry<NeteaseAudioResolution> cached;

                if (!forceRefresh && this.audioCache.TryGetValue(key, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
                {
                    return cached.Value;
                }

                if (forceRefresh)
                {
                    this.audioCache.Remove(key);
                }

                if (!this.audioInFlight.TryGetValue(key, out task))
                {
                    task = this.apiClient.GetSongUrlAsync(songId, "standard", cancellationToken);
                    this.audioInFlight[key] = task;
                }
            }

            try
            {
                NeteaseAudioResolution result = await task;

                if (!result.IsSuccess && IsAuthenticationError(result.Error))
                {
                    await this.sessionService.ExpireAsync(generation);
                }

                if (result.IsSuccess && generation == this.sessionService.SessionGeneration)
                {
                    lock (this.cacheLock)
                    {
                        this.audioCache[key] = new CacheEntry<NeteaseAudioResolution>
                        {
                            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                            Value = result
                        };
                    }
                }

                return result;
            }
            finally
            {
                lock (this.cacheLock)
                {
                    Task<NeteaseAudioResolution> current;

                    if (this.audioInFlight.TryGetValue(key, out current) && object.ReferenceEquals(current, task))
                    {
                        this.audioInFlight.Remove(key);
                    }
                }
            }
        }

        public async Task<NeteaseLyricResult> GetLyricsAsync(string songId, CancellationToken cancellationToken)
        {
            NeteaseError sessionError = this.GetSessionError();

            if (sessionError != null)
            {
                return new NeteaseLyricResult { Error = sessionError };
            }

            long generation = this.sessionService.SessionGeneration;
            string key = generation + "|" + songId;
            Task<NeteaseLyricResult> task;

            lock (this.cacheLock)
            {
                CacheEntry<NeteaseLyricResult> cached;

                if (this.lyricCache.TryGetValue(key, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
                {
                    return cached.Value;
                }

                if (!this.lyricInFlight.TryGetValue(key, out task))
                {
                    task = this.apiClient.GetLyricsAsync(songId, cancellationToken);
                    this.lyricInFlight[key] = task;
                }
            }

            try
            {
                NeteaseLyricResult result = await task;

                if (!result.IsSuccess && IsAuthenticationError(result.Error))
                {
                    await this.sessionService.ExpireAsync(generation);
                }

                if (result.IsSuccess && generation == this.sessionService.SessionGeneration)
                {
                    lock (this.cacheLock)
                    {
                        this.lyricCache[key] = new CacheEntry<NeteaseLyricResult>
                        {
                            ExpiresAtUtc = DateTime.UtcNow.AddHours(24),
                            Value = result
                        };
                    }
                }

                return result;
            }
            finally
            {
                lock (this.cacheLock)
                {
                    Task<NeteaseLyricResult> current;

                    if (this.lyricInFlight.TryGetValue(key, out current) && object.ReferenceEquals(current, task))
                    {
                        this.lyricInFlight.Remove(key);
                    }
                }
            }
        }

        public void ClearSessionCaches()
        {
            lock (this.cacheLock)
            {
                this.recommendations = null;
                this.recommendationsGeneration = -1;
                this.recommendationsInFlight.Clear();
                this.audioCache.Clear();
                this.audioInFlight.Clear();
                this.lyricCache.Clear();
                this.lyricInFlight.Clear();
            }
        }

        private async Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> LoadOrFetchRecommendationsAsync(
            long generation,
            DateTime recommendationDate,
            string accountUserId,
            CancellationToken cancellationToken)
        {
            NeteaseRecommendationLoadResult stored = await this.recommendationStore.LoadAsync(cancellationToken);

            if (stored.IsSuccess && stored.Exists && stored.Snapshot != null &&
                stored.Snapshot.RecommendationDate == recommendationDate &&
                string.Equals(stored.Snapshot.AccountUserId, accountUserId, StringComparison.Ordinal) &&
                stored.Snapshot.Songs != null)
            {
                return NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Success(stored.Snapshot.Songs);
            }

            if (stored.Exists && !stored.IsSuccess)
            {
                await this.recommendationStore.DeleteAsync();
            }

            NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>> result =
                await this.apiClient.GetDailyRecommendationsAsync(cancellationToken);

            if (result.IsSuccess && generation == this.sessionService.SessionGeneration &&
                recommendationDate == NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                    DateTimeOffset.UtcNow) &&
                !string.IsNullOrWhiteSpace(accountUserId))
            {
                await this.recommendationStore.SaveAsync(
                    new NeteaseRecommendationSnapshot
                    {
                        AccountUserId = accountUserId,
                        RecommendationDate = recommendationDate,
                        Songs = new List<NeteaseRecommendedSong>(
                            result.Value ?? Array.Empty<NeteaseRecommendedSong>())
                    },
                    cancellationToken);
            }

            return result;
        }

        private void HandleSessionChanged()
        {
            this.ClearSessionCaches();

            if (this.sessionService.State == NeteaseSessionState.SignedOut ||
                this.sessionService.State == NeteaseSessionState.Expired)
            {
                this.recommendationStore.DeleteAsync();
            }
        }

        private NeteaseError GetSessionError()
        {
            if (this.sessionService.State == NeteaseSessionState.SignedIn)
            {
                return null;
            }

            if (this.sessionService.State == NeteaseSessionState.OfflineUnknown)
            {
                return new NeteaseError(NeteaseErrorCode.NetworkUnavailable, "Language_Netease_Network_Error");
            }

            return new NeteaseError(NeteaseErrorCode.AuthenticationRequired, "Language_Netease_Sign_In_Required");
        }

        private static bool IsAuthenticationError(NeteaseError error)
        {
            return error != null &&
                (error.Code == NeteaseErrorCode.AuthenticationRequired || error.Code == NeteaseErrorCode.SessionExpired);
        }
    }
}
