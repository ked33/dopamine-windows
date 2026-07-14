using Dopamine.Services.Online.Netease;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Tests
{
    [TestFixture]
    public class NeteaseSessionStoreTests
    {
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            this.temporaryDirectory = Path.Combine(Path.GetTempPath(), "Dopamine-Netease-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(this.temporaryDirectory))
            {
                Directory.Delete(this.temporaryDirectory, true);
            }
        }

        [Test]
        public async Task CurrentUserDpapiRoundTrip()
        {
            var store = new DpapiNeteaseSessionStore(this.temporaryDirectory);
            var snapshot = new NeteaseSessionSnapshot
            {
                Cookies = new Dictionary<string, string> { ["session"] = "fixed-test-value" },
                Account = new NeteaseAccountProfile { UserId = "1", Nickname = "tester" }
            };

            var saved = await store.SaveAsync(snapshot, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.That(saved.IsSuccess, Is.True);
            Assert.That(loaded.IsSuccess, Is.True);
            Assert.That(loaded.Snapshot.Cookies["session"], Is.EqualTo("fixed-test-value"));
            Assert.That(loaded.Snapshot.Account.Nickname, Is.EqualTo("tester"));
        }

        [Test]
        public async Task CorruptFileReturnsStructuredFailure()
        {
            string directory = Path.Combine(this.temporaryDirectory, "Netease");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "session.dat"), new byte[] { 1, 2, 3, 4 });
            var store = new DpapiNeteaseSessionStore(this.temporaryDirectory);

            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.That(loaded.Exists, Is.True);
            Assert.That(loaded.IsSuccess, Is.False);
            Assert.That(loaded.Error.Code, Is.EqualTo(NeteaseErrorCode.StorageFailed));
        }

        [Test]
        public async Task LogoutDeleteIsIdempotent()
        {
            var store = new DpapiNeteaseSessionStore(this.temporaryDirectory);
            await store.DeleteAsync();
            await store.DeleteAsync();

            Assert.That(Directory.Exists(Path.Combine(this.temporaryDirectory, "Netease")), Is.False);
        }
    }
}
