using Dopamine.Services.Online.Netease;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseMusicInteractionTests
    {
        [Test]
        public async Task LikeStatusUsesOneSessionCacheRequest()
        {
            var api = new FakeApiClient
            {
                LikedSongIds = new[] { "liked" }
            };
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore();
            var service = new NeteaseMusicService(api, session, store);

            NeteaseResult<bool> liked = await service.IsSongLikedAsync("liked", CancellationToken.None);
            NeteaseResult<bool> other = await service.IsSongLikedAsync("other", CancellationToken.None);

            Assert.That(liked.IsSuccess, Is.True);
            Assert.That(liked.Value, Is.True);
            Assert.That(other.IsSuccess, Is.True);
            Assert.That(other.Value, Is.False);
            Assert.That(api.LikedSongIdsCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task LikeMutationUpdatesTheSessionCacheWithoutRefetching()
        {
            var api = new FakeApiClient();
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore();
            var service = new NeteaseMusicService(api, session, store);

            NeteaseResult<bool> before = await service.IsSongLikedAsync(
                "new-liked-song",
                CancellationToken.None);
            NeteaseResult<bool> mutation = await service.SetSongLikedAsync(
                "new-liked-song",
                true,
                CancellationToken.None);
            NeteaseResult<bool> after = await service.IsSongLikedAsync(
                "new-liked-song",
                CancellationToken.None);

            Assert.That(before.Value, Is.False);
            Assert.That(mutation.IsSuccess, Is.True);
            Assert.That(after.Value, Is.True);
            Assert.That(api.LikedSongIdsCallCount, Is.EqualTo(1));
            Assert.That(api.SetSongLikedCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CancellingOneLikeLookupDoesNotCancelTheSharedRequest()
        {
            var completion = new TaskCompletionSource<NeteaseResult<IReadOnlyCollection<string>>>();
            var api = new FakeApiClient
            {
                LikedSongIdsTask = completion.Task
            };
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore();
            var service = new NeteaseMusicService(api, session, store);
            var cancellationTokenSource = new CancellationTokenSource();

            Task<NeteaseResult<bool>> cancelledLookup = service.IsSongLikedAsync(
                "liked",
                cancellationTokenSource.Token);
            Task<NeteaseResult<bool>> activeLookup = service.IsSongLikedAsync(
                "liked",
                CancellationToken.None);
            cancellationTokenSource.Cancel();
            completion.SetResult(NeteaseResult<IReadOnlyCollection<string>>.Success(
                new[] { "liked" }));

            NeteaseResult<bool> cancelled = await cancelledLookup;
            NeteaseResult<bool> active = await activeLookup;
            NeteaseResult<bool> cached = await service.IsSongLikedAsync(
                "liked",
                CancellationToken.None);

            Assert.That(cancelled.IsSuccess, Is.False);
            Assert.That(cancelled.Error.Code, Is.EqualTo(NeteaseErrorCode.Cancelled));
            Assert.That(active.Value, Is.True);
            Assert.That(cached.Value, Is.True);
            Assert.That(api.LikedSongIdsCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DislikeReplacesRecommendationAndPersistsSnapshot()
        {
            DateTime recommendationDate = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                DateTimeOffset.UtcNow);
            var api = new FakeApiClient
            {
                Replacement = new NeteaseRecommendedSong { Id = "replacement", Name = "Replacement" }
            };
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore
            {
                Snapshot = new NeteaseRecommendationSnapshot
                {
                    AccountUserId = session.Account.UserId,
                    RecommendationDate = recommendationDate,
                    Songs = new List<NeteaseRecommendedSong>
                    {
                        new NeteaseRecommendedSong { Id = "first" },
                        new NeteaseRecommendedSong { Id = "remove-me" },
                        new NeteaseRecommendedSong { Id = "last" }
                    }
                }
            };
            var service = new NeteaseMusicService(api, session, store);

            await service.GetDailyRecommendationsAsync(CancellationToken.None);
            NeteaseResult<NeteaseRecommendationMutation> result =
                await service.DislikeDailyRecommendationAsync("remove-me", CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Replacement.Id, Is.EqualTo("replacement"));
            Assert.That(result.Value.UpdatedRecommendations[1].Id, Is.EqualTo("replacement"));
            Assert.That(result.Value.PersistenceSucceeded, Is.True);
            Assert.That(store.Snapshot.Songs[1].Id, Is.EqualTo("replacement"));
        }

        [Test]
        public async Task DislikeWithoutReplacementRemovesRecommendation()
        {
            DateTime recommendationDate = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                DateTimeOffset.UtcNow);
            var api = new FakeApiClient();
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore
            {
                Snapshot = CreateSnapshot(session, recommendationDate, "first", "remove-me", "last")
            };
            var service = new NeteaseMusicService(api, session, store);

            NeteaseResult<NeteaseRecommendationMutation> result =
                await service.DislikeDailyRecommendationAsync("remove-me", CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Replacement, Is.Null);
            Assert.That(result.Value.UpdatedRecommendations.Count, Is.EqualTo(2));
            Assert.That(store.Snapshot.Songs.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task DislikeDoesNotInsertADuplicateReplacement()
        {
            DateTime recommendationDate = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                DateTimeOffset.UtcNow);
            var api = new FakeApiClient
            {
                Replacement = new NeteaseRecommendedSong { Id = "first" }
            };
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore
            {
                Snapshot = CreateSnapshot(session, recommendationDate, "first", "remove-me", "last")
            };
            var service = new NeteaseMusicService(api, session, store);

            NeteaseResult<NeteaseRecommendationMutation> result =
                await service.DislikeDailyRecommendationAsync("remove-me", CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Replacement, Is.Null);
            Assert.That(result.Value.UpdatedRecommendations.Count, Is.EqualTo(2));
            Assert.That(store.Snapshot.Songs[0].Id, Is.EqualTo("first"));
            Assert.That(store.Snapshot.Songs[1].Id, Is.EqualTo("last"));
        }

        [Test]
        public async Task SuccessfulDislikeWithMissingLocalSongInvalidatesTheSnapshot()
        {
            DateTime recommendationDate = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                DateTimeOffset.UtcNow);
            var api = new FakeApiClient
            {
                Replacement = new NeteaseRecommendedSong { Id = "replacement" }
            };
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore
            {
                Snapshot = CreateSnapshot(session, recommendationDate, "first", "last")
            };
            var service = new NeteaseMusicService(api, session, store);

            NeteaseResult<NeteaseRecommendationMutation> result =
                await service.DislikeDailyRecommendationAsync("missing", CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.RequiresRefresh, Is.True);
            Assert.That(result.Value.Replacement.Id, Is.EqualTo("replacement"));
            Assert.That(store.Snapshot, Is.Null);
            Assert.That(store.DeleteCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DislikeKeepsTheServerResultWhenSnapshotPersistenceFails()
        {
            DateTime recommendationDate = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                DateTimeOffset.UtcNow);
            var api = new FakeApiClient
            {
                Replacement = new NeteaseRecommendedSong { Id = "replacement" }
            };
            var session = new FakeSessionService();
            var store = new FakeRecommendationStore
            {
                Snapshot = CreateSnapshot(session, recommendationDate, "remove-me"),
                SaveSucceeds = false
            };
            var service = new NeteaseMusicService(api, session, store);

            NeteaseResult<NeteaseRecommendationMutation> result =
                await service.DislikeDailyRecommendationAsync("remove-me", CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Replacement.Id, Is.EqualTo("replacement"));
            Assert.That(result.Value.PersistenceSucceeded, Is.False);
            Assert.That(store.Snapshot, Is.Null);
            Assert.That(store.DeleteCallCount, Is.EqualTo(1));
        }

        private static NeteaseRecommendationSnapshot CreateSnapshot(
            FakeSessionService session,
            DateTime recommendationDate,
            params string[] songIds)
        {
            var songs = new List<NeteaseRecommendedSong>();

            foreach (string songId in songIds)
            {
                songs.Add(new NeteaseRecommendedSong { Id = songId });
            }

            return new NeteaseRecommendationSnapshot
            {
                AccountUserId = session.Account.UserId,
                RecommendationDate = recommendationDate,
                Songs = songs
            };
        }

        private sealed class FakeApiClient : INeteaseApiClient
        {
            public IReadOnlyCollection<string> LikedSongIds { get; set; } = new string[0];

            public Task<NeteaseResult<IReadOnlyCollection<string>>> LikedSongIdsTask { get; set; }

            public int LikedSongIdsCallCount { get; private set; }

            public int SetSongLikedCallCount { get; private set; }

            public NeteaseRecommendedSong Replacement { get; set; }

            public Task<NeteaseResult<IReadOnlyCollection<string>>> GetLikedSongIdsAsync(
                string userId,
                CancellationToken cancellationToken)
            {
                this.LikedSongIdsCallCount++;
                if (this.LikedSongIdsTask != null)
                {
                    return this.LikedSongIdsTask;
                }

                return Task.FromResult(NeteaseResult<IReadOnlyCollection<string>>.Success(this.LikedSongIds));
            }

            public Task<NeteaseResult<string>> GetLikedPlaylistIdAsync(
                string userId,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(NeteaseResult<string>.Success("liked-playlist"));
            }

            public Task<NeteaseResult<IReadOnlyList<NeteaseIntelligenceRecommendation>>> GetIntelligenceRecommendationsAsync(
                string playlistId,
                string songId,
                string startMusicId,
                int count,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    NeteaseResult<IReadOnlyList<NeteaseIntelligenceRecommendation>>.Success(
                        new NeteaseIntelligenceRecommendation[0]));
            }

            public Task<NeteaseResult<IReadOnlyList<NeteasePersonalFmItem>>> GetPersonalFmAsync(
                CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    NeteaseResult<IReadOnlyList<NeteasePersonalFmItem>>.Success(
                        new NeteasePersonalFmItem[0]));
            }

            public Task<NeteaseResult<bool>> DislikePersonalFmSongAsync(
                string songId,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(NeteaseResult<bool>.Success(true));
            }

            public Task<NeteaseResult<bool>> SetSongLikedAsync(
                string songId,
                bool isLiked,
                CancellationToken cancellationToken)
            {
                this.SetSongLikedCallCount++;
                return Task.FromResult(NeteaseResult<bool>.Success(true));
            }

            public Task<NeteaseResult<NeteaseRecommendedSong>> DislikeDailyRecommendationAsync(
                string songId,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(NeteaseResult<NeteaseRecommendedSong>.Success(this.Replacement));
            }

            public Task<NeteaseResult<NeteaseQrKey>> CreateQrKeyAsync(CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<NeteaseResult<NeteaseQrCheck>> CheckQrAsync(
                NeteaseQrSession session,
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<NeteaseResult<NeteaseAccountProfile>> GetLoginStatusAsync(
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> GetDailyRecommendationsAsync(
                CancellationToken cancellationToken)
            {
                return Task.FromResult(NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>.Success(
                    new NeteaseRecommendedSong[0]));
            }

            public Task<NeteaseAudioResolution> GetSongUrlAsync(
                string songId,
                string level,
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<NeteaseLyricResult> GetLyricsAsync(
                string songId,
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public void ReplaceCookies(IReadOnlyDictionary<string, string> cookies)
            {
            }

            public IReadOnlyDictionary<string, string> SnapshotCookies()
            {
                return new Dictionary<string, string>();
            }

            public void ClearCookies()
            {
            }
        }

        private sealed class FakeSessionService : INeteaseSessionService
        {
            public NeteaseSessionState State { get; private set; } = NeteaseSessionState.SignedIn;

            public NeteaseAccountProfile Account { get; private set; } =
                new NeteaseAccountProfile { UserId = "unit-test-user", Nickname = "tester" };

            public long SessionGeneration { get; private set; } = 1;

            public event EventHandler SessionChanged = delegate { };

            public Task ExpireAsync(long expectedSessionGeneration)
            {
                this.State = NeteaseSessionState.Expired;
                this.SessionChanged(this, EventArgs.Empty);
                return Task.CompletedTask;
            }

            public Task RestoreAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<NeteaseResult<NeteaseQrSession>> BeginQrLoginAsync(
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<NeteaseQrPollResult> PollQrLoginAsync(
                NeteaseQrSession session,
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<NeteaseLoginResult> LoginWithCookieAsync(
                SecureString cookie,
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public void CancelSignIn(long loginGeneration)
            {
            }

            public Task LogoutAsync()
            {
                return Task.CompletedTask;
            }
        }

        private sealed class FakeRecommendationStore : INeteaseRecommendationStore
        {
            public NeteaseRecommendationSnapshot Snapshot { get; set; }

            public bool SaveSucceeds { get; set; } = true;

            public int DeleteCallCount { get; private set; }

            public Task<NeteaseRecommendationLoadResult> LoadAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new NeteaseRecommendationLoadResult
                {
                    Exists = this.Snapshot != null,
                    IsSuccess = true,
                    Snapshot = this.Snapshot
                });
            }

            public Task<NeteaseResult<bool>> SaveAsync(
                NeteaseRecommendationSnapshot snapshot,
                CancellationToken cancellationToken)
            {
                if (!this.SaveSucceeds)
                {
                    return Task.FromResult(NeteaseResult<bool>.Failure(new NeteaseError(
                        NeteaseErrorCode.StorageFailed,
                        "Language_Netease_Service_Unavailable")));
                }

                this.Snapshot = snapshot;
                return Task.FromResult(NeteaseResult<bool>.Success(true));
            }

            public Task DeleteAsync()
            {
                this.DeleteCallCount++;
                this.Snapshot = null;
                return Task.CompletedTask;
            }
        }
    }
}
