// STORY-533 — A once-a-shift quirk fires once a shift (gh-#293 gh-#920 · SPEC F218 · PLAN T683 T686 T687)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans four files by seam: AC1/AC2/AC6 (T683, card shape) here; AC3/AC4 (T686, selector) in
// tests/GenWave.Memory.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Selector.cs; AC5 (T687, prompt) in
// tests/GenWave.Tts.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Prompt.cs; AC7 (T686, preview endpoint) in
// tests/GenWave.Host.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Preview.cs.

namespace GenWave.Core.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftCard
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the card carries rules and round-trips
    // entry point: PersonaCard import/export JSON
    public sealed class ScenarioTheCardCarriesRulesAndRoundTrips
    {
        // Given a card JSON with QuirkRules [{text, maxPerShift: 1, perBreakChance: 0.15}]
        // When  it is imported and exported

        [Fact(Skip = "pending: T683 — see docs/PLAN.md")]
        public void TheExportIsByteEqualToTheImport() => Assert.Fail("pending: T683 — the export is byte-equal to the import");

        [Fact(Skip = "pending: T683 — see docs/PLAN.md")]
        public void QuirksAndQuirkRulesNameTheSameTexts() => Assert.Fail("pending: T683 — Quirks and QuirkRules name the same texts");
    }

    // AC2 — an edit keeps the rules
    public sealed class ScenarioAnEditKeepsTheRules
    {
        // Given a persona with QuirkRules
        // When  PATCH renames the persona

        [Fact(Skip = "pending: T683 — see docs/PLAN.md")]
        public void QuirkRulesAreUnchanged() => Assert.Fail("pending: T683 — QuirkRules are unchanged");
    }

    // AC6 — old cards behave as today
    public sealed class ScenarioOldCardsBehaveAsToday
    {
        // Given a card without QuirkRules
        // When  quirks are sampled

        [Fact(Skip = "pending: T683 — see docs/PLAN.md")]
        public void Exactly23QuirksAreChosenExactlyAsBeforeF713() => Assert.Fail("pending: T683 — 2–3 quirks are chosen exactly as before (F71.3)");
    }
}
