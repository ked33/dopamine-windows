using Dopamine.Data;
using NUnit.Framework;

namespace Dopamine.Tests
{
    [TestFixture]
    public class TrackSearchClauseTests
    {
        [Test]
        public void EmptySearch_MatchesAll()
        {
            Assert.That(DataUtils.CreateTrackSearchClause(null), Is.EqualTo("1=1"));
            Assert.That(DataUtils.CreateTrackSearchClause("   "), Is.EqualTo("1=1"));
        }

        [Test]
        public void SingleToken_BuildsOrAcrossFields()
        {
            string clause = DataUtils.CreateTrackSearchClause("Radiohead");
            Assert.That(clause, Does.Contain("LOWER(IFNULL(t.TrackTitle,'')) LIKE '%radiohead%'"));
            Assert.That(clause, Does.Contain("LOWER(IFNULL(t.Artists,'')) LIKE '%radiohead%'"));
            Assert.That(clause, Does.Contain("LOWER(IFNULL(t.AlbumTitle,'')) LIKE '%radiohead%'"));
            Assert.That(clause, Does.Contain(" OR "));
        }

        [Test]
        public void MultiToken_UsesAndBetweenPieces()
        {
            string clause = DataUtils.CreateTrackSearchClause("OK Computer");
            Assert.That(clause, Does.Contain(" AND "));
            Assert.That(clause, Does.Contain("%ok%"));
            Assert.That(clause, Does.Contain("%computer%"));
        }

        [Test]
        public void Quotes_AreEscaped()
        {
            string clause = DataUtils.CreateTrackSearchClause("O'Brien");
            Assert.That(clause, Does.Contain("o''brien"));
        }
    }
}
