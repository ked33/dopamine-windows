using Dopamine.Services.Online.Netease;
using NUnit.Framework;
using System;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseDailyRecommendationScheduleTests
    {
        [Test]
        public void RecommendationDayChangesAtSixChinaTime()
        {
            DateTime beforeBoundary = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                new DateTimeOffset(2026, 7, 14, 21, 59, 59, TimeSpan.Zero));
            DateTime afterBoundary = NeteaseDailyRecommendationSchedule.GetRecommendationDate(
                new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero));

            Assert.That(beforeBoundary, Is.EqualTo(new DateTime(2026, 7, 14)));
            Assert.That(afterBoundary, Is.EqualTo(new DateTime(2026, 7, 15)));
        }

        [Test]
        public void NextRefreshDelayIsPositiveAndWithinOneDay()
        {
            TimeSpan delay = NeteaseDailyRecommendationSchedule.GetDelayUntilNextRefresh(
                new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

            Assert.That(delay, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(delay, Is.LessThanOrEqualTo(TimeSpan.FromDays(1)));
        }
    }
}
