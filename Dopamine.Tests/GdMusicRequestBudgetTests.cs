using Dopamine.Services.Online.GdMusic;
using NUnit.Framework;
using System;

namespace Dopamine.Tests
{
    [TestFixture]
    public class GdMusicRequestBudgetTests
    {
        [Test]
        public void EnforcesFiftyRequestsAcrossARollingFiveMinuteWindow()
        {
            var budget = new GdMusicRequestBudget();
            for (int i = 0; i < 50; i++)
                Assert.That(budget.TryAcquire(i * 1200), Is.True);
            Assert.That(budget.TryAcquire(60000), Is.False);
            Assert.That(budget.TryAcquire(299999), Is.False);
            Assert.That(budget.TryAcquire(300000), Is.True);
            Assert.That(budget.TryAcquire(300001), Is.False);
            Assert.That(budget.TryAcquire(301200), Is.True);
        }

        [Test]
        public void ServerBackoffHonorsRetryAfterAndCannotBeShortened()
        {
            var budget = new GdMusicRequestBudget();
            budget.BackOff(0, TimeSpan.FromSeconds(60));
            budget.BackOff(1000, TimeSpan.FromSeconds(1));
            Assert.That(budget.TryAcquire(59999), Is.False);
            Assert.That(budget.TryAcquire(60000), Is.True);
        }

        [Test]
        public void MissingRetryAfterBacksOffForFiveMinutes()
        {
            var budget = new GdMusicRequestBudget();
            budget.BackOff(0, null);
            Assert.That(budget.TryAcquire(299999), Is.False);
            Assert.That(budget.TryAcquire(300000), Is.True);
        }
    }
}
