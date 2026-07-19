using Dopamine.Services.Entities;
using Dopamine.Services.Online.Netease;

namespace Dopamine.Services.Playback
{
    public sealed class OnlineAudioFallbackRequest
    {
        public TrackViewModel Track { get; set; }

        public NeteaseError OfficialFailure { get; set; }

        public bool ForceRefresh { get; set; }

        public bool AllowWithoutOfficialFailure { get; set; }
    }

    public sealed class OnlineAudioFallbackResult
    {
        public bool IsSuccess { get; set; }

        public string Url { get; set; }

        public string ProviderId { get; set; }

        public string MediaType { get; set; }

        public string CacheVariant { get; set; }

        public long Bitrate { get; set; }

        public long Size { get; set; }

        public string ErrorCode { get; set; }

        public static OnlineAudioFallbackResult Failure(string errorCode)
        {
            return new OnlineAudioFallbackResult
            {
                ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "unknown" : errorCode
            };
        }
    }
}
