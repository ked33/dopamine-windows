using HyPlayer.NeteaseApi.Bases;
using System;

namespace Dopamine.Services.Online.Netease
{
    internal static class NeteaseQrStatusMapper
    {
        internal static bool TryGetStatusCode(Exception exception, out int statusCode)
        {
            var apiError = exception as ErrorResultBase;
            statusCode = apiError?.ErrorCode ?? 0;

            switch (statusCode)
            {
                case 800:
                case 801:
                case 802:
                case 803:
                    return true;
                default:
                    statusCode = 0;
                    return false;
            }
        }
    }
}
