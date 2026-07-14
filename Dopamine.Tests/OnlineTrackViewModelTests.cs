using Dopamine.Data.Entities;
using Dopamine.Services.Entities;
using NUnit.Framework;

namespace Dopamine.Tests
{
    [TestFixture]
    public class OnlineTrackViewModelTests
    {
        [Test]
        public void DeepCopyPreservesIndependentOnlineSourceIdentity()
        {
            var track = new Track
            {
                Path = "netease://song/123",
                SafePath = "netease://song/123",
                FileName = "Song",
                TrackTitle = "Song",
                TrackNumber = 0
            };
            var viewModel = new TrackViewModel(null, null, track)
            {
                SourceInfo = new TrackSourceInfo
                {
                    Kind = TrackSourceKind.Netease,
                    ProviderId = "netease",
                    RemoteId = "123"
                }
            };

            TrackViewModel copy = viewModel.DeepCopy();
            copy.SourceInfo.RemoteId = "456";

            Assert.That(copy.IsOnline, Is.True);
            Assert.That(copy.SupportsFileMetadataActions, Is.False);
            Assert.That(viewModel.SourceInfo.RemoteId, Is.EqualTo("123"));
            Assert.That(copy.SourceInfo.RemoteId, Is.EqualTo("456"));
        }
    }
}
