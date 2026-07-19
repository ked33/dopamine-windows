using Digimezzo.Foundation.Core.Utils;
using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Playback
{
    public sealed class UnblockNeteaseMusicFallbackProvider : IOnlineAudioFallbackProvider
    {
        private readonly IUnblockSidecarService sidecarService;

        public UnblockNeteaseMusicFallbackProvider(IUnblockSidecarService sidecarService)
        {
            this.sidecarService = sidecarService;
        }

        public string Id => "unblockneteasemusic";

        public int Order => 100;

        public bool CanHandle(NeteaseError officialFailure)
        {
            if (officialFailure == null)
            {
                return false;
            }

            switch (officialFailure.Code)
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
            if (!UnblockNeteaseMusicSettings.IsEnabled || request?.Track?.SourceInfo == null ||
                !this.CanHandle(request.OfficialFailure))
            {
                return OnlineAudioFallbackResult.Failure("not_applicable");
            }

            TrackViewModel track = request.Track;
            IReadOnlyList<string> artists = string.IsNullOrWhiteSpace(track.Track.Artists)
                ? new List<string>()
                : DataUtils.SplitAndTrimColumnMultiValue(track.Track.Artists).ToList();
            long duration = track.Track.Duration ?? 0;

            if (string.IsNullOrWhiteSpace(track.SourceInfo.RemoteId) || string.IsNullOrWhiteSpace(track.TrackTitle) ||
                duration <= 0 || UnblockNeteaseMusicSettings.Sources.Count == 0)
            {
                return OnlineAudioFallbackResult.Failure("invalid_track_metadata");
            }

            UnblockSidecarMatchResult result = await this.sidecarService.MatchAsync(
                new UnblockSidecarMatchRequest
                {
                    SongId = track.SourceInfo.RemoteId,
                    Title = track.TrackTitle,
                    Artists = artists,
                    Album = track.Track.AlbumTitle ?? string.Empty,
                    DurationMilliseconds = duration,
                    Sources = UnblockNeteaseMusicSettings.Sources
                },
                cancellationToken);

            if (result == null || !result.IsSuccess || string.IsNullOrWhiteSpace(result.Url))
            {
                return OnlineAudioFallbackResult.Failure(result?.ErrorCode);
            }

            return new OnlineAudioFallbackResult
            {
                IsSuccess = true,
                Url = result.Url,
                ProviderId = string.IsNullOrWhiteSpace(result.Source) ? this.Id : result.Source,
                MediaType = result.MediaType,
                CacheVariant = UnblockNeteaseMusicSettings.EnableFlac ? "flac" : "standard",
                Bitrate = result.Bitrate,
                Size = result.Size
            };
        }
    }
}
