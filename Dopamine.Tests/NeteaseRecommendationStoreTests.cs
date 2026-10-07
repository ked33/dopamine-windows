using Dopamine.Services.Online.Netease;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseRecommendationStoreTests
    {
        [Test]
        public void OldCacheWithoutAccessHintsRequiresRefresh()
        {
            string folder = Path.Combine(Path.GetTempPath(), "Dopamine-OldRecommendationCache-" + Guid.NewGuid().ToString("N"));
            try
            {
                string directory = Path.Combine(folder, "Netease");
                Directory.CreateDirectory(directory);
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes("{\"Version\":1,\"AccountUserId\":\"test\",\"Songs\":[{\"Id\":\"123\"}]}"),
                    Encoding.UTF8.GetBytes("Dopamine.Netease.DailyRecommendations.v1"), DataProtectionScope.CurrentUser);
                File.WriteAllBytes(Path.Combine(directory, "daily-recommendations.dat"), encrypted);
                var result = new DpapiNeteaseRecommendationStore(folder).LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(result.Exists, Is.True);
                Assert.That(result.IsSuccess, Is.False);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

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
                        new NeteaseRecommendedSong { Id = "first", PreferFallbackAudio = true },
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
                Assert.That(load.Snapshot.Version, Is.EqualTo(2));
                Assert.That(load.Snapshot.Songs[0].PreferFallbackAudio, Is.True);
                Assert.That(load.Snapshot.Songs[1].PreferFallbackAudio, Is.False);

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
