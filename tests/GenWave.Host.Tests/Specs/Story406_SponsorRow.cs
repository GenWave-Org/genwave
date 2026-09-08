// STORY-406 — A sponsor is a row, not a string (SPEC F171.1 · PLAN T431)

namespace GenWave.Host.Tests.Specs;

public static class FeatureASponsorIsARowNotAString
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTableShape
    {
        [Fact]
        public void StationSponsorCarriesEveryF171Column()
            => Assert.Fail("pending: T431 db/46 station.sponsor columns — AC1");

        [Fact]
        public void NameKeyIsAStoredFoldOfName()
            => Assert.Fail("pending: T431 db/46 name_key generated column — AC1");

        [Fact]
        public void TheUniqueConstraintIsOnPackSlugAndNameKeyNullsNotDistinct()
            => Assert.Fail("pending: T431 db/46 sponsor_pack_slug_name_key — AC1");
    }

    public sealed class ScenarioPackNamespaceIsSeparateFromOwnerNamespace
    {
        [Fact]
        public void AnOwnerSponsorNamedLikeAPackSponsorIsCreated()
            => Assert.Fail("pending: T431 store: owner vs pack namespace — AC3");

        [Fact]
        public void BothRowsCoexist()
            => Assert.Fail("pending: T431 store: two rows after the pair — AC3");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioCaseAndWhitespaceCollapseToOneSponsor
    {
        [Fact]
        public void ASecondCreateDifferingOnlyByCaseAndSpacingIsRefused()
            => Assert.Fail("pending: T431 POST /api/sponsors 409 sponsor_name_taken — AC2");
    }
}
