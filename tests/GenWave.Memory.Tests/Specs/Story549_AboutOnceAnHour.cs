// STORY-549 — About once an hour (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. CallbackPlanner over the in-memory callback store, a FakeTimeProvider and a seeded draw.
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Memory.Tests.Specs;

public static class FeatureAboutOnceAnHour
{
    // AC1 — the gates (table: last used, last offer, draw)
    public sealed class ScenarioTheGates
    {
        // Given Memory:CallbacksPerHour is 1

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void NeverUsedNeverOfferedAndAPassingDrawIsACallbackBreak() => Assert.Fail("pending: T734 — never used never offered and a passing draw is a callback break");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void AFailingDrawIsNotACallbackBreak() => Assert.Fail("pending: T734 — a failing draw is not a callback break");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void Used44MinutesAgoIsNotACallbackBreak() => Assert.Fail("pending: T734 — used 44 minutes ago is not a callback break");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void Used46MinutesAgoIsACallbackBreak() => Assert.Fail("pending: T734 — used 46 minutes ago is a callback break");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void AnyOffer9MinutesAgoIsNotACallbackBreak() => Assert.Fail("pending: T734 — any offer 9 minutes ago is not a callback break");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void AnyOffer11MinutesAgoIsACallbackBreak() => Assert.Fail("pending: T734 — any offer 11 minutes ago is a callback break");
    }

    // AC2 — a higher rate shortens the wait
    public sealed class ScenarioAHigherRateShortensTheWait
    {
        // Given the setting is 3 and the last used offer was 16 minutes ago

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void TheBreakIsACallbackBreakAtRate3() => Assert.Fail("pending: T734 — the break is a callback break at rate 3");
    }

    // AC3 — least recently offered first
    public sealed class ScenarioLeastRecentlyOfferedFirst
    {
        // Given one never-offered memory and two offered 3 h and 5 h ago

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void TheNeverOfferedMemoryIsPicked() => Assert.Fail("pending: T734 — the never offered memory is picked");
    }

    // AC4 — rest starts on use
    public sealed class ScenarioRestStartsOnUse
    {
        // Given lore used 5 h ago and a life memory used 47 h ago

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void NeitherRestedMemoryIsPicked() => Assert.Fail("pending: T734 — neither rested memory is picked");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void TheLoreMemoryIsEligibleAgainAt7Hours() => Assert.Fail("pending: T734 — the lore memory is eligible again at 7 hours");
    }

    // AC5 — a missed ask rests two hours
    public sealed class ScenarioAMissedAskRestsTwoHours
    {
        // Given a memory asked 90 minutes ago and not used

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void TheMemoryAsked90MinutesAgoIsSkipped() => Assert.Fail("pending: T734 — the memory asked 90 minutes ago is skipped");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void At3HoursItIsEligibleAgain() => Assert.Fail("pending: T734 — at 3 hours it is eligible again");
    }

    // AC6 — off switch (sad path)
    public sealed class ScenarioOffSwitch
    {
        // Given the setting is 0
        // When  100 breaks are planned

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void NoneOf100BreaksIsACallbackBreak() => Assert.Fail("pending: T734 — none of 100 breaks is a callback break");
    }

    // AC7 — nothing to call back to (sad path)
    public sealed class ScenarioNothingToCallBackTo
    {
        // Given a DJ with no eligible memory

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void ItIsNotACallbackBreakAndNoOfferIsWritten() => Assert.Fail("pending: T734 — it is not a callback break and no offer is written");
    }

    // AC8 — drafts and forgotten rows never air (sad path)
    public sealed class ScenarioDraftsAndForgottenRowsNeverAir
    {

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void AnUnapprovedDraftIsNotPicked() => Assert.Fail("pending: T734 — an unapproved draft is not picked");

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void AForgottenRowIsNotPicked() => Assert.Fail("pending: T734 — a forgotten row is not picked");
    }

    // AC9 — a memory with no distinctive word (sad path)
    public sealed class ScenarioAMemoryWithNoDistinctiveWord
    {
        // Given Maxxie Volt whose only memory is "Maxxie is here"

        [Fact(Skip = "pending: T734 — see docs/PLAN.md")]
        public void NothingIsPickedForAMemoryWithNoDistinctiveWord() => Assert.Fail("pending: T734 — nothing is picked for a memory with no distinctive word");
    }
}
