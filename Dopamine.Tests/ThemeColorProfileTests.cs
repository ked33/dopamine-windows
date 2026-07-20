using Dopamine.Services.Appearance;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Linq;

namespace Dopamine.Tests
{
    [TestFixture]
    public class ThemeColorProfileTests
    {
        [Test]
        public void SemanticColorTokens_ContainsExpectedIds()
        {
            Assert.That(SemanticColorTokens.All.Count, Is.GreaterThanOrEqualTo(15));
            Assert.That(SemanticColorTokens.GetById("MainBackground"), Is.Not.Null);
            Assert.That(SemanticColorTokens.GetById("Sidebar"), Is.Not.Null);
            Assert.That(SemanticColorTokens.GetById("Accent"), Is.Not.Null);
            Assert.That(SemanticColorTokens.GetById("Accent").IsAccent, Is.True);
            Assert.That(SemanticColorTokens.GetById("DoesNotExist"), Is.Null);
        }

        [Test]
        public void ThemeColorProfile_RoundTripsJson()
        {
            var profile = new ThemeColorProfile();
            profile.SetColor("MainBackground", "#FF1A1A1A");
            profile.SetColor("Accent", "#FF1D7DD4");

            string json = JsonConvert.SerializeObject(profile);
            ThemeColorProfile loaded = JsonConvert.DeserializeObject<ThemeColorProfile>(json);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.HasColor("MainBackground"), Is.True);
            string accent;
            Assert.That(loaded.TryGetColor("Accent", out accent), Is.True);
            Assert.That(accent, Is.EqualTo("#FF1D7DD4"));
            Assert.That(loaded.HasAny, Is.True);
        }

        [Test]
        public void ThemeColorProfile_RemoveAndClear()
        {
            var profile = new ThemeColorProfile();
            profile.SetColor("Sidebar", "#FF171717");
            Assert.That(profile.RemoveColor("Sidebar"), Is.True);
            Assert.That(profile.HasColor("Sidebar"), Is.False);

            profile.SetColor("ListBackground", "#FF272727");
            profile.Clear();
            Assert.That(profile.HasAny, Is.False);
        }

        [Test]
        public void SemanticColorTokens_AllHaveResourceKeys()
        {
            Assert.That(SemanticColorTokens.All.All(t => t.ResourceKeys != null && t.ResourceKeys.Count > 0), Is.True);
            Assert.That(SemanticColorTokens.All.All(t => !string.IsNullOrEmpty(t.DisplayNameKey)), Is.True);
        }
    }
}
