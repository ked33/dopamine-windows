using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseFallbackPipelineTests
    {
        [Test]
        public async Task OfficialSuccessSkipsBothFallbacks()
        {
            var official = new FakeMusic { Response = new NeteaseAudioResolution { IsSuccess = true, Url = "https://official.test/song.mp3" } };
            var api = new GdMusicAudioFallbackTests.FakeGdApi();
            var unblock = new FakeUnblock();
            var resolver = CreateResolver(official, api, unblock);
            var result = await resolver.ResolveAsync(GdMusicAudioFallbackTests.CreateRequest().Track,
                OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(result.ProviderId, Is.EqualTo("netease"));
            Assert.That(api.Calls, Is.Zero);
            Assert.That(unblock.Calls, Is.Zero);
        }

        [Test]
        public async Task TrialOnlyUsesGdBeforeUnblockAndKeepsCacheIdentitySeparate()
        {
            var api = new GdMusicAudioFallbackTests.FakeGdApi();
            var unblock = new FakeUnblock();
            var result = await CreateResolver(new FakeMusic(), api, unblock).ResolveAsync(
                GdMusicAudioFallbackTests.CreateRequest().Track, OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ProviderId, Is.EqualTo("gdstudio-netease"));
            Assert.That(result.CacheKey, Is.EqualTo("fallback-gdstudio-netease-quality-320-526081111"));
            Assert.That(unblock.Calls, Is.Zero);
        }

        [Test]
        public async Task GdFailureContinuesToExistingUnblockProvider()
        {
            var api = new GdMusicAudioFallbackTests.FakeGdApi
            {
                Response = NeteaseResult<Services.Online.GdMusic.GdMusicTrackUrl>.Failure(new NeteaseError(NeteaseErrorCode.EmptyUrl, "test"))
            };
            var unblock = new FakeUnblock();
            var result = await CreateResolver(new FakeMusic(), api, unblock).ResolveAsync(
                GdMusicAudioFallbackTests.CreateRequest().Track, OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ProviderId, Is.EqualTo("unblock-test"));
            Assert.That(api.Calls, Is.EqualTo(1));
            Assert.That(unblock.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task AllFallbacksFailPreservesOfficialReason()
        {
            var official = new FakeMusic();
            var api = new GdMusicAudioFallbackTests.FakeGdApi
            {
                Response = NeteaseResult<Services.Online.GdMusic.GdMusicTrackUrl>.Failure(new NeteaseError(NeteaseErrorCode.RateLimited, "test"))
            };
            var unblock = new FakeUnblock { Response = OnlineAudioFallbackResult.Failure("not_applicable") };
            var result = await CreateResolver(official, api, unblock).ResolveAsync(
                GdMusicAudioFallbackTests.CreateRequest().Track, OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.SameAs(official.Response.Error));
        }

        [Test]
        public async Task CancellationStopsBeforeTheNextProvider()
        {
            var api = new GdMusicAudioFallbackTests.FakeGdApi
            {
                Response = NeteaseResult<Services.Online.GdMusic.GdMusicTrackUrl>.Failure(new NeteaseError(NeteaseErrorCode.Cancelled, "test"))
            };
            var unblock = new FakeUnblock();
            var result = await CreateResolver(new FakeMusic(), api, unblock).ResolveAsync(
                GdMusicAudioFallbackTests.CreateRequest().Track, OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(result.Error.Code, Is.EqualTo(NeteaseErrorCode.Cancelled));
            Assert.That(unblock.Calls, Is.Zero);
        }

        [Test]
        public async Task FallbackFirstDownloadUsesTheConfiguredFirstSource()
        {
            var official = new FakeMusic();
            var api = new GdMusicAudioFallbackTests.FakeGdApi();
            var unblock = new FakeUnblock();
            var result = await CreateResolver(official, api, unblock).ResolveAsync(
                GdMusicAudioFallbackTests.CreateRequest().Track, OnlineAudioSourcePriority.UnblockFirst, false, CancellationToken.None);
            Assert.That(result.ProviderId, Is.EqualTo("gdstudio-netease"));
            Assert.That(official.Calls, Is.Zero);
            Assert.That(api.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task FiveMixedSourcesAreTriedInCustomOrderAndDisabledSourcesAreSkipped()
        {
            string[] order = { "gd:qobuz", "unblock:kuwo", "gd:netease", "unblock:kugou", "gd:joox" };
            var calls = new List<string>();
            var config = new AudioFallbackConfiguration { Enabled = true, Sources = order.Select(
                x => new AudioFallbackSource { Id = x, Enabled = true }).ToList() };
            config.Sources.Insert(1, new AudioFallbackSource { Id = "gd:spotify", Enabled = false });
            var resolver = new NeteaseAudioSourceResolver(new FakeMusic(), new IOnlineAudioFallbackProvider[] {
                new RecordingProvider("gdstudio", calls), new RecordingProvider("unblockneteasemusic", calls)
            }, new FakeFallbackSettings(config));
            await resolver.ResolveAsync(GdMusicAudioFallbackTests.CreateRequest().Track,
                OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(order));
        }

        [TestCase(OnlineAudioSourcePriority.OfficialFirst)]
        [TestCase(OnlineAudioSourcePriority.FallbackFirst)]
        public async Task MasterOffPreventsAllFallbackRequests(OnlineAudioSourcePriority priority)
        {
            var api = new GdMusicAudioFallbackTests.FakeGdApi();
            var unblock = new FakeUnblock();
            var resolver = new NeteaseAudioSourceResolver(new FakeMusic(), new IOnlineAudioFallbackProvider[] {
                new GdMusicAudioFallbackProvider(api), unblock
            }, new FakeFallbackSettings(AudioFallbackConfiguration.CreateDefault(false, new[] { "kugou" })));
            await resolver.ResolveAsync(GdMusicAudioFallbackTests.CreateRequest().Track, priority, false, CancellationToken.None);
            Assert.That(api.Calls + unblock.Calls, Is.Zero);
        }

        [Test]
        public async Task FailedAudioDownloadCanAdvanceToNextConfiguredSourceWithoutRetryingEarlierOnes()
        {
            var official = new FakeMusic { Response = new NeteaseAudioResolution { IsSuccess = true, Url = "https://official.test/song.mp3" } };
            var api = new GdMusicAudioFallbackTests.FakeGdApi();
            var resolver = CreateResolver(official, api, new FakeUnblock());
            var track = GdMusicAudioFallbackTests.CreateRequest().Track;
            var failed = new HashSet<string>();
            var first = await resolver.ResolveAsync(track, OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None, failed);
            failed.Add(first.ConfiguredSourceId);
            var second = await resolver.ResolveAsync(track, OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None, failed);
            failed.Add(second.ConfiguredSourceId);
            var third = await resolver.ResolveAsync(track, OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None, failed);
            Assert.That(new[] { first.ConfiguredSourceId, second.ConfiguredSourceId, third.ConfiguredSourceId },
                Is.EqualTo(new[] { "official", "gd:netease", "unblock:kugou" }));
            Assert.That(official.Calls, Is.EqualTo(1));
            Assert.That(api.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task SharedGdRateLimitSkipsOtherGdRowsButStillTriesUnblock()
        {
            var calls = new List<string>();
            var config = AudioFallbackConfiguration.CreateDefault(true, new[] { "kugou" });
            config.Sources.ForEach(x => x.Enabled = true);
            var resolver = new NeteaseAudioSourceResolver(new FakeMusic(), new IOnlineAudioFallbackProvider[] {
                new RecordingProvider("gdstudio", calls) { Result = OnlineAudioFallbackResult.Failure("gd_RateLimited") },
                new RecordingProvider("unblockneteasemusic", calls)
            }, new FakeFallbackSettings(config));
            await resolver.ResolveAsync(GdMusicAudioFallbackTests.CreateRequest().Track,
                OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(new[] { "gd:netease", "unblock:kugou", "unblock:bodian", "unblock:kuwo" }));
        }

        [Test]
        public async Task DisablingWhileResolvingPreventsTheResultFromBeingUsed()
        {
            var settings = new FakeFallbackSettings();
            var provider = new RecordingProvider("gdstudio", new List<string>())
            {
                Result = new OnlineAudioFallbackResult { IsSuccess = true, Url = "https://example.test/song.mp3" },
                OnResolve = () => settings.TrySave(AudioFallbackConfiguration.CreateDefault(false))
            };
            var resolver = new NeteaseAudioSourceResolver(new FakeMusic(), new[] { provider }, settings);
            var result = await resolver.ResolveAsync(GdMusicAudioFallbackTests.CreateRequest().Track,
                OnlineAudioSourcePriority.OfficialFirst, false, CancellationToken.None);
            Assert.That(result.IsSuccess, Is.False);
        }

        private sealed class RecordingProvider : IOnlineAudioFallbackProvider
        {
            private readonly List<string> calls;
            public RecordingProvider(string id, List<string> calls) { this.Id = id; this.calls = calls; }
            public string Id { get; }
            public int Order => 0;
            public Action OnResolve;
            public OnlineAudioFallbackResult Result = OnlineAudioFallbackResult.Failure("not_found");
            public bool CanHandle(NeteaseError error) => true;
            public Task<OnlineAudioFallbackResult> TryResolveAsync(OnlineAudioFallbackRequest request, CancellationToken cancellationToken)
            {
                this.calls.Add((this.Id == "gdstudio" ? "gd:" : "unblock:") + request.Source);
                this.OnResolve?.Invoke();
                return Task.FromResult(this.Result);
            }
        }

        private static NeteaseAudioSourceResolver CreateResolver(FakeMusic official, GdMusicAudioFallbackTests.FakeGdApi api, FakeUnblock unblock)
        {
            // Deliberately reverse registration order to verify provider priorities.
            return new NeteaseAudioSourceResolver(official, new IOnlineAudioFallbackProvider[] { unblock, new GdMusicAudioFallbackProvider(api) }, new FakeFallbackSettings());
        }

        private sealed class FakeUnblock : IOnlineAudioFallbackProvider
        {
            public string Id => "unblockneteasemusic";
            public int Order => 100;
            public int Calls;
            public OnlineAudioFallbackResult Response = new OnlineAudioFallbackResult { IsSuccess = true, ProviderId = "unblock-test", Url = "https://unblock.test/song.mp3" };
            public bool CanHandle(NeteaseError error) => true;
            public Task<OnlineAudioFallbackResult> TryResolveAsync(OnlineAudioFallbackRequest request, CancellationToken cancellationToken)
            { Calls++; return Task.FromResult(Response); }
        }

        private sealed class FakeMusic : INeteaseMusicService
        {
            public int Calls;
            public NeteaseAudioResolution Response = new NeteaseAudioResolution { Error = new NeteaseError(NeteaseErrorCode.TrialOnly, "test") };
            public Task<NeteaseAudioResolution> ResolveOfficialAudioAsync(string songId, bool forceRefresh, CancellationToken cancellationToken)
            { Calls++; return Task.FromResult(Response); }
            public Task<NeteaseResult<IReadOnlyList<NeteaseRecommendedSong>>> GetDailyRecommendationsAsync(CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<NeteaseLikedLibrary>> GetLikedLibraryAsync(CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<IReadOnlyList<NeteaseIntelligenceRecommendation>>> GetIntelligenceRecommendationsAsync(string p, string s, string start, int count, CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<bool>> DislikeIntelligenceRecommendationAsync(string id, CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<IReadOnlyList<NeteasePersonalFmItem>>> GetPersonalFmAsync(CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<bool>> DislikePersonalFmSongAsync(string id, CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<bool>> IsSongLikedAsync(string id, CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<IReadOnlyCollection<string>>> GetLikedSongIdsAsync(CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<bool>> SetSongLikedAsync(string id, bool liked, CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseResult<NeteaseRecommendationMutation>> DislikeDailyRecommendationAsync(string id, CancellationToken ct) => throw new NotSupportedException();
            public Task<NeteaseLyricResult> GetLyricsAsync(string id, CancellationToken ct) => throw new NotSupportedException();
            public void ClearSessionCaches() { }
        }
    }
}
