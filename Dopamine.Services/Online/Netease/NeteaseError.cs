namespace Dopamine.Services.Online.Netease
{
    public enum NeteaseErrorCode
    {
        None = 0,
        Cancelled = 1,
        NetworkUnavailable = 2,
        RateLimited = 3,
        AuthenticationRequired = 4,
        SessionExpired = 5,
        NoCopyright = 6,
        SubscriptionRequired = 7,
        TrialOnly = 8,
        EmptyUrl = 9,
        EmptyResponse = 10,
        ApiChanged = 11,
        InvalidCookie = 12,
        StorageFailed = 13,
        DecoderUnsupported = 14,
        TemporaryDownloadFailed = 15,
        Unknown = 16,
        RiskControlRequired = 17
    }

    public sealed class NeteaseError
    {
        public NeteaseErrorCode Code { get; private set; }

        public int ResponseCode { get; private set; }

        public string MessageKey { get; private set; }

        public NeteaseError(NeteaseErrorCode code, string messageKey, int responseCode = 0)
        {
            this.Code = code;
            this.MessageKey = messageKey;
            this.ResponseCode = responseCode;
        }
    }
}
