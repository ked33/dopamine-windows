using System;

namespace Dopamine.Services.Online.Netease
{
    public static class NeteaseDailyRecommendationSchedule
    {
        private const int RefreshHour = 6;
        private static readonly TimeZoneInfo ChinaTimeZone = GetChinaTimeZone();

        public static DateTime GetRecommendationDate(DateTimeOffset utcNow)
        {
            DateTimeOffset chinaNow = TimeZoneInfo.ConvertTime(utcNow, ChinaTimeZone);
            return chinaNow.AddHours(-RefreshHour).Date;
        }

        public static TimeSpan GetDelayUntilNextRefresh(DateTimeOffset utcNow)
        {
            DateTimeOffset chinaNow = TimeZoneInfo.ConvertTime(utcNow, ChinaTimeZone);
            DateTime nextLocal = chinaNow.Date.AddHours(RefreshHour);

            if (chinaNow.DateTime >= nextLocal)
            {
                nextLocal = nextLocal.AddDays(1);
            }

            var next = new DateTimeOffset(nextLocal, chinaNow.Offset);
            TimeSpan delay = next.ToUniversalTime() - utcNow.ToUniversalTime();
            return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
        }

        private static TimeZoneInfo GetChinaTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Dopamine China Standard Time",
                    TimeSpan.FromHours(8),
                    "China Standard Time",
                    "China Standard Time");
            }
        }
    }
}
