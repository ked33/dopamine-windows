using Dopamine.Services.Playback;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Dopamine.Tests
{
    [TestFixture]
    public class AudioFallbackSettingsTests
    {
        private string directory;
        private string path;

        [SetUp]
        public void SetUp()
        {
            this.directory = Path.Combine(Path.GetTempPath(), "Dopamine-Fallback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.directory);
            this.path = Path.Combine(this.directory, "AudioFallback.json");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(this.directory, true);

        [Test]
        public void DefaultsSelectGdNeteaseFirstAndKeepLegacyMasterState()
        {
            var defaults = AudioFallbackConfiguration.CreateDefault(false, new[] { "kuwo", "kugou" }, true);
            Assert.That(defaults.Sources[0].Id, Is.EqualTo("gd:netease"));
            Assert.That(defaults.Sources[0].Enabled, Is.True);
            Assert.That(defaults.Sources.Count, Is.EqualTo(13));
            Assert.That(defaults.Sources.Count(x => x.Enabled && x.Id.StartsWith("gd:")), Is.EqualTo(1));
            Assert.That(defaults.Sources.Single(x => x.Id == "unblock:kuwo").Enabled, Is.True);
            Assert.That(defaults.Enabled, Is.False);
            Assert.That(defaults.UnblockEnableFlac, Is.True);
        }

        [Test]
        public void FiveMixedSourcesCustomOrderAndQualitySurviveRestart()
        {
            var settings = new AudioFallbackSettings(this.path, AudioFallbackConfiguration.CreateDefault());
            var config = settings.Current;
            string[] order = { "unblock:kuwo", "gd:joox", "gd:netease", "unblock:kugou", "gd:qobuz" };
            config.Sources = order.Select(x => new AudioFallbackSource { Id = x, Enabled = true }).ToList();
            config.Enabled = true;
            config.GdQuality = 999;
            config.UnblockEnableFlac = true;
            Assert.That(settings.TrySave(config), Is.True);
            var restored = new AudioFallbackSettings(this.path, AudioFallbackConfiguration.CreateDefault()).Current;
            Assert.That(restored.Sources.Where(x => x.Enabled).Select(x => x.Id), Is.EqualTo(order));
            Assert.That(restored.Enabled, Is.True);
            Assert.That(restored.GdQuality, Is.EqualTo(999));
            Assert.That(restored.UnblockEnableFlac, Is.True);
        }

        [Test]
        public void EmptySelectionIsNotResetAndSnapshotsAreIndependent()
        {
            var settings = new AudioFallbackSettings(this.path, AudioFallbackConfiguration.CreateDefault());
            var next = settings.Current;
            next.Sources.ForEach(x => x.Enabled = false);
            Assert.That(settings.Current.Sources[0].Enabled, Is.True);
            Assert.That(settings.TrySave(next), Is.True);
            var restored = new AudioFallbackSettings(this.path, AudioFallbackConfiguration.CreateDefault()).Current;
            Assert.That(restored.Sources.Any(x => x.Enabled), Is.False);
        }

        [Test]
        public void UnknownAndDuplicateSourcesAreRemovedWithoutChangingOrder()
        {
            var config = AudioFallbackConfiguration.CreateDefault();
            config.Sources = new[] { "gd:qobuz", "bad", "gd:qobuz", "gd:netease" }
                .Select(x => new AudioFallbackSource { Id = x, Enabled = true }).ToList();
            config.GdQuality = 123;
            var normalized = config.Normalize();
            Assert.That(normalized.Sources.Take(2).Select(x => x.Id), Is.EqualTo(new[] { "gd:qobuz", "gd:netease" }));
            Assert.That(normalized.Sources.Count, Is.EqualTo(13));
            Assert.That(normalized.Sources.Count(x => x.Enabled), Is.EqualTo(2));
            Assert.That(normalized.GdQuality, Is.EqualTo(320));
        }

        [TestCase("broken json")]
        [TestCase("{\"Version\":2,\"Enabled\":true}")]
        public void InvalidOrNewerConfigurationIsPreservedAndDisabled(string json)
        {
            File.WriteAllText(this.path, json);
            var settings = new AudioFallbackSettings(this.path, AudioFallbackConfiguration.CreateDefault(true));
            Assert.That(settings.Current.Enabled, Is.False);
            Assert.That(settings.Error, Is.Not.Empty);
            Assert.That(settings.TrySave(AudioFallbackConfiguration.CreateDefault(true)), Is.False);
            Assert.That(File.ReadAllText(this.path), Is.EqualTo(json));
        }

        [Test]
        public void FailedAtomicSaveDoesNotPublishOrLosePreviousSettings()
        {
            var settings = new AudioFallbackSettings(this.path, AudioFallbackConfiguration.CreateDefault());
            int changes = 0;
            settings.Changed += (_, __) => changes++;
            var next = settings.Current;
            next.Enabled = true;
            using (File.Open(this.path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(settings.TrySave(next), Is.False);
                Assert.That(settings.Current.Enabled, Is.False);
                Assert.That(changes, Is.Zero);
            }
            Assert.That(settings.TrySave(next), Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(this.directory, "*.tmp"), Is.Empty);
        }
    }

    internal sealed class FakeFallbackSettings : IAudioFallbackSettings
    {
        public AudioFallbackConfiguration Current { get; private set; }
        public string Error => null;
        public event EventHandler Changed = delegate { };
        public FakeFallbackSettings(AudioFallbackConfiguration current = null)
        {
            this.Current = current ?? AudioFallbackConfiguration.CreateDefault(true, new[] { "kugou" });
        }
        public bool TrySave(AudioFallbackConfiguration configuration)
        {
            this.Current = configuration.Normalize();
            this.Changed(this, EventArgs.Empty);
            return true;
        }
    }
}
