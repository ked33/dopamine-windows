using System;
using System.Collections.Generic;

namespace Dopamine.Services.Playback
{
    public enum UnblockSidecarState
    {
        Disabled = 0,
        Stopped = 1,
        Starting = 2,
        Ready = 3,
        Unavailable = 4,
        Incompatible = 5
    }

    public sealed class UnblockSidecarMatchRequest
    {
        public string SongId { get; set; }

        public string Title { get; set; }

        public IReadOnlyList<string> Artists { get; set; }

        public string Album { get; set; }

        public long DurationMilliseconds { get; set; }

        public IReadOnlyList<string> Sources { get; set; }
    }

    public sealed class UnblockSidecarMatchResult
    {
        public bool IsSuccess { get; set; }

        public string Url { get; set; }

        public string Source { get; set; }

        public long Bitrate { get; set; }

        public long Size { get; set; }

        public string MediaType { get; set; }

        public string ErrorCode { get; set; }
    }

    internal sealed class UnblockSidecarHandshake
    {
        public int ProtocolVersion { get; set; }

        public string UnblockVersion { get; set; }

        public int Port { get; set; }
    }

    internal sealed class UnblockSidecarResponse
    {
        public int ProtocolVersion { get; set; }

        public string Url { get; set; }

        public string Source { get; set; }

        public long Bitrate { get; set; }

        public long Size { get; set; }

        public string MediaType { get; set; }

        public string Error { get; set; }
    }
}
