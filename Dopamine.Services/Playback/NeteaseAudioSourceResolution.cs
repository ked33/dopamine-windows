using Dopamine.Services.Online.Netease;

namespace Dopamine.Services.Playback
{
    public enum OnlineAudioSourcePriority
    {
        OfficialFirst = 0,
        FallbackFirst = 1,
        UnblockFirst = FallbackFirst
    }

    public sealed class NeteaseAudioSourceResolution
    {
        public bool IsSuccess { get; set; }

        public string SongId { get; set; }

        public string Url { get; set; }

        public string ConfiguredSourceId { get; set; }

        public string ProviderId { get; set; }

        public string MediaType { get; set; }

        public string CacheVariant { get; set; }

        public string CacheKey { get; set; }

        public string QualityLevel { get; set; }

        public long BitRate { get; set; }

        public long Size { get; set; }

        public NeteaseError Error { get; set; }

        public static NeteaseAudioSourceResolution Failure(string songId, NeteaseError error)
        {
            return new NeteaseAudioSourceResolution
            {
                IsSuccess = false,
                SongId = songId,
                Error = error ?? new NeteaseError(
                    NeteaseErrorCode.Unknown,
                    "Language_Netease_Service_Unavailable")
            };
        }
    }
}
