using Dopamine.Services.Online.Netease;
using Dopamine.Services.Playback;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
        public async Task ExplicitUnblockFirstDownloadPolicyStillUsesUnblockFirst()
        {
            var official = new FakeMusic();
            var api = new GdMusicAudioFallbackTests.FakeGdApi();
            var unblock = new FakeUnblock();
            var result = await CreateResolver(official, api, unblock).ResolveAsync(
                GdMusicAudioFallbackTests.CreateRequest().Track, OnlineAudioSourcePriority.UnblockFirst, false, CancellationToken.None);
            Assert.That(result.ProviderId, Is.EqualTo("unblock-test"));
            Assert.That(official.Calls, Is.Zero);
            Assert.That(api.Calls, Is.Zero);
        }

        private static NeteaseAudioSourceResolver CreateResolver(FakeMusic official, GdMusicAudioFallbackTests.FakeGdApi api, FakeUnblock unblock)
        {
            // Deliberately reverse registration order to verify provider priorities.
            return new NeteaseAudioSourceResolver(official, new IOnlineAudioFallbackProvider[] { unblock, new GdMusicAudioFallbackProvider(api) });
        }

        private sealed class FakeUnblock : IOnlineAudioFallbackProvider
        {
            public string Id => "unblock-test";
            public int Order => 100;
            public int Calls;
            public OnlineAudioFallbackResult Response = new OnlineAudioFallbackResult { IsSuccess = true, Url = "https://unblock.test/song.mp3" };
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
