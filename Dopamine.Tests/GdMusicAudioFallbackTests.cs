using Dopamine.Data.Entities;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.GdMusic;
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
    public class GdMusicAudioFallbackTests
    {
        [TestCase(NeteaseErrorCode.TrialOnly, true)]
        [TestCase(NeteaseErrorCode.NoCopyright, true)]
        [TestCase(NeteaseErrorCode.SubscriptionRequired, true)]
        [TestCase(NeteaseErrorCode.EmptyUrl, true)]
        [TestCase(NeteaseErrorCode.AuthenticationRequired, false)]
        [TestCase(NeteaseErrorCode.SessionExpired, false)]
        [TestCase(NeteaseErrorCode.NetworkUnavailable, false)]
        [TestCase(NeteaseErrorCode.RateLimited, false)]
        [TestCase(NeteaseErrorCode.Cancelled, false)]
        public void OnlyHandlesAudioAvailabilityFailures(NeteaseErrorCode code, bool expected)
        {
            Assert.That(new GdMusicAudioFallbackProvider(new FakeGdApi()).CanHandle(
                new NeteaseError(code, "test")), Is.EqualTo(expected));
        }

        [Test]
        public async Task UsesExactNeteaseIdEvenWhenSearchResultHasNoDuration()
        {
            var api = new FakeGdApi();
            var provider = new GdMusicAudioFallbackProvider(api);
            var request = CreateRequest();
            var result = await provider.TryResolveAsync(request, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(api.Source, Is.EqualTo("netease"));
            Assert.That(api.TrackId, Is.EqualTo("526081111"));
            Assert.That(api.Bitrate, Is.EqualTo(320));
            Assert.That(result.ProviderId, Is.EqualTo("gdstudio-netease"));
            Assert.That(result.Bitrate, Is.EqualTo(320000));
            Assert.That(result.Size, Is.EqualTo(8837805));
            Assert.That(request.Track.SourceInfo.Kind, Is.EqualTo(TrackSourceKind.Netease));
        }

        [Test]
        public async Task CacheAvoidsDuplicateRequestsAndForceRefreshFetchesNewUrl()
        {
            var api = new FakeGdApi();
            var provider = new GdMusicAudioFallbackProvider(api);
            var request = CreateRequest();
            await provider.TryResolveAsync(request, CancellationToken.None);
            await provider.TryResolveAsync(request, CancellationToken.None);
            Assert.That(api.Calls, Is.EqualTo(1));

            request.ForceRefresh = true;
            api.Response = Success("https://example.test/refreshed.mp3");
            var refreshed = await provider.TryResolveAsync(request, CancellationToken.None);
            Assert.That(api.Calls, Is.EqualTo(2));
            Assert.That(refreshed.Url, Does.EndWith("refreshed.mp3"));
        }

        [Test]
        public async Task ConcurrentRequestsForSameSongShareCachedResult()
        {
            var api = new FakeGdApi();
            var provider = new GdMusicAudioFallbackProvider(api);
            var tasks = new List<Task<OnlineAudioFallbackResult>>();
            for (int i = 0; i < 12; i++)
                tasks.Add(provider.TryResolveAsync(CreateRequest(), CancellationToken.None));
            await Task.WhenAll(tasks);
            Assert.That(api.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task EmptyUrlFailuresAreCachedButCancellationIsNot()
        {
            var api = new FakeGdApi { Response = NeteaseResult<GdMusicTrackUrl>.Failure(
                new NeteaseError(NeteaseErrorCode.EmptyUrl, "test")) };
            var provider = new GdMusicAudioFallbackProvider(api);
            var request = CreateRequest();
            Assert.That((await provider.TryResolveAsync(request, CancellationToken.None)).ErrorCode,
                Is.EqualTo("gd_EmptyUrl"));
            await provider.TryResolveAsync(request, CancellationToken.None);
            Assert.That(api.Calls, Is.EqualTo(1));

            request.ForceRefresh = true;
            api.Response = NeteaseResult<GdMusicTrackUrl>.Failure(new NeteaseError(NeteaseErrorCode.Cancelled, "test"));
            Assert.ThrowsAsync<OperationCanceledException>(() => provider.TryResolveAsync(request, CancellationToken.None));
            request.ForceRefresh = false;
            api.Response = Success();
            Assert.That((await provider.TryResolveAsync(request, CancellationToken.None)).IsSuccess, Is.True);
            Assert.That(api.Calls, Is.EqualTo(3));
        }

        [TestCase("file:///C:/song.mp3")]
        [TestCase("javascript:alert(1)")]
        [TestCase("")]
        public async Task RejectsUnusableAudioUrls(string url)
        {
            var api = new FakeGdApi { Response = Success(url) };
            var result = await new GdMusicAudioFallbackProvider(api).TryResolveAsync(CreateRequest(), CancellationToken.None);
            Assert.That(result.ErrorCode, Is.EqualTo("gd_invalid_url"));
        }

        [Test]
        public async Task DoesNotReplaceExplicitUnblockFirstOrResolveOtherPlatforms()
        {
            var api = new FakeGdApi();
            var provider = new GdMusicAudioFallbackProvider(api);
            var request = CreateRequest();
            request.OfficialFailure = null;
            request.AllowWithoutOfficialFailure = true;
            Assert.That((await provider.TryResolveAsync(request, CancellationToken.None)).IsSuccess, Is.False);
            request = CreateRequest();
            request.Track.SourceInfo.Kind = TrackSourceKind.ExternalOnline;
            Assert.That((await provider.TryResolveAsync(request, CancellationToken.None)).IsSuccess, Is.False);
            Assert.That(api.Calls, Is.Zero);
        }

        [Test]
        public void CancelledPlaybackDoesNotSendARequest()
        {
            var api = new FakeGdApi();
            var provider = new GdMusicAudioFallbackProvider(api);
            Assert.ThrowsAsync<OperationCanceledException>(() => provider.TryResolveAsync(CreateRequest(), new CancellationToken(true)));
            Assert.That(api.Calls, Is.Zero);
        }

        internal static OnlineAudioFallbackRequest CreateRequest()
        {
            return new OnlineAudioFallbackRequest
            {
                Track = new TrackViewModel(null, null, new Track { Path = "netease://song/526081111", TrackNumber = 0, Duration = 0 })
                {
                    SourceInfo = new TrackSourceInfo { Kind = TrackSourceKind.Netease, RemoteId = "526081111", ProviderId = "netease" }
                },
                OfficialFailure = new NeteaseError(NeteaseErrorCode.TrialOnly, "test")
            };
        }

        internal static NeteaseResult<GdMusicTrackUrl> Success(string url = "https://example.test/song.mp3")
        {
            return NeteaseResult<GdMusicTrackUrl>.Success(new GdMusicTrackUrl { Url = url, BitRate = 320, SizeBytes = 8837805 });
        }

        internal sealed class FakeGdApi : IGdMusicApiClient
        {
            public int Calls;
            public string Source;
            public string TrackId;
            public int Bitrate;
            public NeteaseResult<GdMusicTrackUrl> Response = Success();
            public async Task<NeteaseResult<GdMusicTrackUrl>> GetTrackUrlAsync(string source, string trackId, int bitRate, CancellationToken cancellationToken)
            {
                Calls++;
                Source = source;
                TrackId = trackId;
                Bitrate = bitRate;
                await Task.Yield();
                return Response;
            }
            public Task<NeteaseResult<IReadOnlyList<GdMusicSearchResult>>> SearchAsync(string source, string keyword, int count, int page, CancellationToken cancellationToken)
            { throw new InvalidOperationException("Fallback must not search for a potentially different song."); }
            public Task<NeteaseResult<string>> GetPictureUrlAsync(string source, string pictureId, CancellationToken cancellationToken)
            { throw new NotSupportedException(); }
        }
    }
}
