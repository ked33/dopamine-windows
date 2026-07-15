using Dopamine.Services.Online.Netease;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseRecommendationStoreTests
    {
        [Test]
        public void SavesAndRestoresEncryptedRecommendationOrder()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "Dopamine-NeteaseRecommendationStoreTests-" + Guid.NewGuid().ToString("N"));

            try
            {
                var store = new DpapiNeteaseRecommendationStore(folder);
                var snapshot = new NeteaseRecommendationSnapshot
                {
                    AccountUserId = "unit-test-account",
                    RecommendationDate = new DateTime(2026, 7, 15),
                    Songs = new List<NeteaseRecommendedSong>
                    {
                        new NeteaseRecommendedSong { Id = "first" },
                        new NeteaseRecommendedSong { Id = "second" }
                    }
                };

                NeteaseResult<bool> save = store.SaveAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult();
                NeteaseRecommendationLoadResult load = store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

                Assert.That(save.IsSuccess, Is.True);
                Assert.That(load.IsSuccess, Is.True);
                Assert.That(load.Snapshot.AccountUserId, Is.EqualTo("unit-test-account"));
                Assert.That(load.Snapshot.Songs[0].Id, Is.EqualTo("first"));
                Assert.That(load.Snapshot.Songs[1].Id, Is.EqualTo("second"));

                store.DeleteAsync().GetAwaiter().GetResult();
                string cacheDirectory = Path.Combine(folder, "Netease");
                Assert.That(Directory.Exists(cacheDirectory), Is.True);
                Assert.That(Directory.GetFiles(cacheDirectory), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }
    }
}
