namespace Dopamine.Services.Playback
{
    public enum PlaybackFailureReason
    {
        Unknown = 0,
        FileNotFound = 1,
        AuthenticationRequired = 2,
        SessionExpired = 3,
        NetworkUnavailable = 4,
        RateLimited = 5,
        NoCopyright = 6,
        SubscriptionRequired = 7,
        TrialOnly = 8,
        EmptyUrl = 9,
        ApiChanged = 10,
        DecoderUnsupported = 11,
        TemporaryDownloadFailed = 12,
        Cancelled = 13
    }
}
