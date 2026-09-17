// STORY-454 — The plan is a value (gh-#401 · SPEC F187 · PLAN T520)
//
// BDD specification — xUnit. AC1/AC2 reflect the types; AC3–AC8 build plans by hand and read ordinals, traces, reservations and speakers.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureThePlanIsAValue
{
    const string Pending = "pending: T520 — BreakPlan, PlannedSlot, SlotSource, Reservation, BreakContext, ToTrace() (STORY-454)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePlanTypesReflected
    {
        // Given: typeof(BreakPlan), PlannedSlot, Reservation, BreakContext

        /// <summary>AC1 — </summary>
        [Fact(Skip = Pending)]
        public void BreakPlanIsAPublicSealedRecord() => Assert.Fail(Pending);

        /// <summary>AC1 — </summary>
        [Fact(Skip = Pending)]
        public void PlannedSlotIsAPublicSealedRecord() => Assert.Fail(Pending);

        /// <summary>AC1 — </summary>
        [Fact(Skip = Pending)]
        public void ReservationIsAPublicSealedRecord() => Assert.Fail(Pending);

        /// <summary>AC1 — </summary>
        [Fact(Skip = Pending)]
        public void BreakContextIsAPublicSealedRecord() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheClosedHierarchies
    {
        // Given: every subtype of SlotSource and SlotOutcome in the assembly

        /// <summary>AC2 — Render, Verbatim, Ready</summary>
        [Fact(Skip = Pending)]
        public void EverySlotSourceIsSealed() => Assert.Fail(Pending);

        /// <summary>AC2 — Rendered, TimedOut, Failed, Abandoned</summary>
        [Fact(Skip = Pending)]
        public void EverySlotOutcomeIsSealed() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAThreeSlotPlan
    {
        // Given: a plan with three slots

        /// <summary>AC3 — ordinals 1, 2, 3 in list order</summary>
        [Fact(Skip = Pending)]
        public void NumbersThemFromOne() => Assert.Fail(Pending);
    }

    public sealed class ScenarioARenderSlotTrace
    {
        // Given: Render slot LeadIn, speaker Ada, no reservation

        /// <summary>AC4 — #1 LeadIn render speaker=Ada res=-</summary>
        [Fact(Skip = Pending)]
        public void PrintsTheLine() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAReservedReadySlotTrace
    {
        // Given: Ready slot Ad with reservation AdSpot:42

        /// <summary>AC5 — #1 Ad ready speaker=- res=AdSpot:42</summary>
        [Fact(Skip = Pending)]
        public void PrintsTheLine() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTheEmptyPlan
    {
        // Given: a plan with no slots

        /// <summary>AC6 — (empty)</summary>
        [Fact(Skip = Pending)]
        public void PrintsEmpty() => Assert.Fail(Pending);
    }

    public sealed class ScenarioAPlanFromTheScript
    {
        // Given: the F185.1 break planned

        /// <summary>AC7 — BackAnnounce, LeadIn, SignOff, SignOn, TimeDate, Context</summary>
        [Fact(Skip = Pending)]
        public void UnclaimedKindsCarryNoReservation() => Assert.Fail(Pending);

        /// <summary>AC7 — every other slot</summary>
        [Fact(Skip = Pending)]
        public void ClaimedKindsCarryOne() => Assert.Fail(Pending);

        /// <summary>AC8 — Render and Verbatim</summary>
        [Fact(Skip = Pending)]
        public void SpokenSlotsCarryASpeaker() => Assert.Fail(Pending);

        /// <summary>AC8 — </summary>
        [Fact(Skip = Pending)]
        public void ReadySlotsCarryNone() => Assert.Fail(Pending);
    }
}
