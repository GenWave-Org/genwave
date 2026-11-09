// STORY-538 — Pending lore is silent (gh-#293 gh-#920 · SPEC F218 · PLAN T700)
//
// BDD specification — xUnit. AC5 only; the other ACs live in the Core and Host Story538 files.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Memory.Tests.Specs;

public static class FeaturePendingLoreIsSilent
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — Pending is silent
    public sealed class ScenarioPendingIsSilent
    {
        // entry point: IMemoryRecallSource over the in-memory store
        // Given a drafted row with approved_at null
        // When  the prompt is built

        [Fact(Skip = "pending: T700 — see docs/PLAN.md")]
        public void TheRowIsNotRecalled() => Assert.Fail("pending: T700 — the row is not recalled");
    }
}
