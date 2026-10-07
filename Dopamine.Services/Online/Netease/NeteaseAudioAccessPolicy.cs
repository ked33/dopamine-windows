namespace Dopamine.Services.Online.Netease
{
    internal static class NeteaseAudioAccessPolicy
    {
        // These are routing hints, not an authoritative check of the current user's
        // entitlement. Always allow the official source if fallback sources fail.
        internal static bool PrefersFallback(int? songFee, int? privilegeFee, int? status)
        {
            return status < 0 || RequiresPayment(songFee) || RequiresPayment(privilegeFee);
        }

        private static bool RequiresPayment(int? fee)
        {
            // 1 = VIP track, 4 = paid album/single. 8 can still offer free playback
            // at a lower quality, so it does not on its own mean playback is blocked.
            // Do not infer restrictions from pl/cp == 0: the SDK also uses zero for
            // absent fields in partial privilege objects.
            return fee == 1 || fee == 4;
        }
    }
}
