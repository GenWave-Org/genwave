// STORY-550 — Every ask leaves a record, the store half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture; add [Collection(DatabaseCollection.Name)] on activation).
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEveryAskLeavesARecordStore
{
    // AC1 — written before the model call
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheOfferRow
    {
        // When  OfferAsync runs

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void AMemoryOfferRowExistsWithUsedFalseAndNoUtterance() => Assert.Fail("pending: T733 — a memory offer row exists with used false and no utterance");
    }

    // AC4 — use starts the rest
    [Trait("Category", "Integration")]
    public sealed class ScenarioUseStartsTheRest
    {
        // Given an offer settled as used

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void TheMemorysAiredCountIsOneHigherAndLastAiredIsSet() => Assert.Fail("pending: T733 — the memorys aired count is one higher and last aired is set");
    }

    // AC5 — a miss leaves the memory alone
    [Trait("Category", "Integration")]
    public sealed class ScenarioAMissLeavesTheMemoryAlone
    {
        // Given an offer settled as not used

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void TheMemorysAiredCountAndLastAiredAreUnchanged() => Assert.Fail("pending: T733 — the memorys aired count and last aired are unchanged");
    }

    // AC6 — the migration resets old counts
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheMigrationResetsOldCounts
    {
        // Given pre-db/52 counts on lore, episode and life rows and a quirk spend row

        [Fact(Skip = "pending: T730 — see docs/PLAN.md")]
        public void LoreEpisodeAndLifeRowsHaveAiredCount0AndNoLastAired() => Assert.Fail("pending: T730 — lore episode and life rows have aired count 0 and no last aired");

        [Fact(Skip = "pending: T730 — see docs/PLAN.md")]
        public void TheQuirkRowIsUnchanged() => Assert.Fail("pending: T730 — the quirk row is unchanged");
    }

    // AC8 — 28 days (sad path)
    [Trait("Category", "Integration")]
    public sealed class ScenarioTwentyEightDays
    {
        // Given an offer 29 days old
        // When  the next offer is written

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void The29DayOldOfferIsGone() => Assert.Fail("pending: T733 — the 29 day old offer is gone");
    }

    // STORY-549 AC4, AC5, AC8 — the store's eligibility query
    [Trait("Category", "Integration")]
    public sealed class ScenarioEligibilityInSQL
    {

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void LoreUsed5HoursAgoIsNotEligible() => Assert.Fail("pending: T733 — lore used 5 hours ago is not eligible");

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void AMemoryAsked90MinutesAgoAndNotUsedIsNotEligible() => Assert.Fail("pending: T733 — a memory asked 90 minutes ago and not used is not eligible");

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void AnUnapprovedDraftAndAForgottenRowAreNotEligible() => Assert.Fail("pending: T733 — an unapproved draft and a forgotten row are not eligible");

        [Fact(Skip = "pending: T733 — see docs/PLAN.md")]
        public void NeverOfferedRowsComeFirst() => Assert.Fail("pending: T733 — never offered rows come first");
    }
}
