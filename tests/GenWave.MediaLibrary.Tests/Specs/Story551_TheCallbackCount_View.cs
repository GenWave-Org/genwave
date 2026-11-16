// STORY-551 — The callback count, the view half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. Postgres-backed (Category=Integration, shared DatabaseFixture; add [Collection(DatabaseCollection.Name)] on activation).
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTheCallbackCountView
{
    // AC1 — the view
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheView
    {
        // Given lines in 4 clock hours, 6 offers, 3 used

        [Fact(Skip = "pending: T730 — see docs/PLAN.md")]
        public void TheDJsRowShows6Asks3Used4HoursAnd075UsedPerHour() => Assert.Fail("pending: T730 — the DJs row shows 6 asks 3 used 4 hours and 0.75 used per hour");
    }

    // AC4 — old rows don't count (sad path)
    [Trait("Category", "Integration")]
    public sealed class ScenarioOldRowsDoNotCount
    {
        // Given an offer 8 days old

        [Fact(Skip = "pending: T730 — see docs/PLAN.md")]
        public void The8DayOldOfferIsNotCounted() => Assert.Fail("pending: T730 — the 8 day old offer is not counted");
    }
}
