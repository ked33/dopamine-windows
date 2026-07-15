using Dopamine.Core.Audio;
using System;

namespace Dopamine.Services.Playback
{
    public sealed class PlaybackSourceRequest
    {
        public bool ForceRefresh { get; set; }

        public IProgress<double> BufferingProgress { get; set; }
    }

    public sealed class PlaybackSourceResolution
    {
        public bool IsSuccess { get; set; }

        public AudioSource AudioSource { get; set; }

        public PlaybackFailureReason FailureReason { get; set; }

        public string MessageKey { get; set; }
    }
}
