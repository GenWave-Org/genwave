// STORY-543 — Signature lines in moderation (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T709)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: PersonaCard import/export (the card serializer)

namespace GenWave.Core.Tests.Specs;

public static class FeatureSignatureLinesInModerationCard
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC6 — the quota fields survive a round trip
    public sealed class ScenarioTheQuotaFieldsSurviveARoundTrip
    {
        // Given a card whose QuirkRules entry has maxPerHour 4 and minBreaksApart 1
        // When  the card is imported and exported

        [Fact(Skip = "pending: T709 — see docs/PLAN.md")]
        public void TheExportIsByteEqualToTheImport() => Assert.Fail("pending: T709 — the export is byte-equal to the import");
    }
}
