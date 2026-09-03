// STORY-389 — A spot has a visible lifecycle (store half: AC1/AC6 · F159 · pending T398)
// The stock-keeping half (AC2–AC5) lives in GenWave.Ads.Tests/Specs/Story389_AdStockKeeping.cs.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAdSpotLifecycleStore
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTransitionsAreTotalAndStamped
    {
        [Fact(Skip = "Pending T398 — see docs/PLAN.md")]
        public void EveryLegalTransitionStampsStateChangedAt()
        {
            // draft→approved→rendering→ready; failed→approved; ready→retired; draft→retired —
            //   each updates state_changed_at (live Postgres).
            Assert.Fail("pending T398");
        }

        [Fact(Skip = "Pending T398 — see docs/PLAN.md")]
        public void ReadyRequiresANonNullMediaId()
        {
            // rendering→ready without media_id is refused at the store.
            Assert.Fail("pending T398");
        }

        [Fact(Skip = "Pending T398 — see docs/PLAN.md")]
        public void BriefUpsertIsKeyedOnPackSlugAndBrand()
        {
            // Upserting the same (pack_slug, brand) twice updates in place — count stays 1.
            Assert.Fail("pending T398");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioIllegalMovesAreRefused
    {
        [Fact(Skip = "Pending T398 — see docs/PLAN.md")]
        public void AnIllegalTransitionIsRefusedAndTheRowUnchanged()
        {
            // e.g. retired→approved and draft→ready both refuse; xmin guard holds concurrency.
            Assert.Fail("pending T398");
        }

        [Fact(Skip = "Pending T398 — see docs/PLAN.md")]
        public void NothingIsEverSystemDeleted()
        {
            // Retirement and failure leave every ad_spot row and media row present (F159.1).
            Assert.Fail("pending T398");
        }
    }
}
