// STORY-550/551 — the memory-offer table and callback_rate view (PLAN T730): db/06 and db/52 must build the same shape.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureMemoryOfferMirror
{
    public sealed class ScenarioDb06AndDb52CarryTheSameBlock
    {
        [Fact]
        public void TheMemoryOfferBlockTextIsIdentical()
        {
            var root = RepoRootLocator.Find(AppContext.BaseDirectory);
            var fresh = MirroredSqlBlock.Read(Path.Combine(root, "db", "06-station-settings-migration.sh"), "f218-memory-offer");
            var upgrade = MirroredSqlBlock.Read(Path.Combine(root, "db", "52-memory-offer-migration.sh"), "f218-memory-offer");

            Assert.Equal(fresh, upgrade);
        }
    }
}
