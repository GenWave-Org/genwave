// STORY-410 — Deleting a sponsor is guarded (SPEC F171.5 · PLAN T434)

namespace GenWave.Host.Tests.Specs;

public static class FeatureDeletingASponsorIsGuarded
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDeleteSucceedsWhenUnreferenced
    {
        [Fact]
        public void DeleteIs204()
            => Assert.Fail("pending: T434 DELETE /api/sponsors/{id} — AC1");

        [Fact]
        public void TheRowIsGone()
            => Assert.Fail("pending: T434 DELETE removes the row — AC1");
    }

    public sealed class ScenarioPauseCoversTheKeepItAroundCase
    {
        [Fact]
        public void PauseOnAReferencedSponsorIs200()
            => Assert.Fail("pending: T434 POST pause on referenced — AC3");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRefuseNamesTheCounts
    {
        [Fact]
        public void DeleteOnAReferencedSponsorIs409SponsorInUse()
            => Assert.Fail("pending: T434 DELETE 409 sponsor_in_use — AC2");

        [Fact]
        public void TheDetailNamesBriefsSpotsAndShowsCounts()
            => Assert.Fail("pending: T434 409 detail counts — AC2");

        [Fact]
        public void TheDetailListsTheFirstTenReferencingTitles()
            => Assert.Fail("pending: T434 409 detail titles — AC2");

        [Fact]
        public void NothingWasRemoved()
            => Assert.Fail("pending: T434 409 leaves the row — AC2");
    }
}
