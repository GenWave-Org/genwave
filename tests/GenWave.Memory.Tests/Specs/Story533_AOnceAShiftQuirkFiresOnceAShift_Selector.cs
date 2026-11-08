// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T686)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans four files; see tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

namespace GenWave.Memory.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftSelector
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC3 — the shift key
    // entry point: IQuirkSelector via LlmCopyWriter.WriteAsync (ShiftKey from CachingScheduleResolver)
    public sealed class ScenarioTheShiftKey
    {
        // Given a schedule segment Tuesday 06:00–10:00 for persona 7 on 2026-11-10
        // When  two breaks happen inside it

        [Fact(Skip = "pending: T686 — see docs/PLAN.md")]
        public void BothCarryShiftKey7Tuesday36020261110() => Assert.Fail("pending: T686 — both carry shift key \"7|Tuesday|360|2026-11-10\"");

        [Fact(Skip = "pending: T686 — see docs/PLAN.md")]
        public void ABreakInTheSameSlotOn20261117CarriesADifferentKey() => Assert.Fail("pending: T686 — a break in the same slot on 2026-11-17 carries a different key");
    }

    // AC4 — spent means gone for the shift
    public sealed class ScenarioSpentMeansGoneForTheShift
    {
        // Given a maxPerShift: 1 quirk and 100 breaks in one shift
        // When  the selector samples quirks for each break

        [Fact(Skip = "pending: T686 — see docs/PLAN.md")]
        public void TheQuirkIsChosenExactlyOnce() => Assert.Fail("pending: T686 — the quirk is chosen exactly once");

        [Fact(Skip = "pending: T686 — see docs/PLAN.md")]
        public void ItsSpendRowExistsInPersonaMemoryKindQuirkBeforeTheRenderIsDispatched() => Assert.Fail("pending: T686 — its spend row exists in persona_memory (kind quirk) before the render is dispatched");
    }
}
