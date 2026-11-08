// STORY-532 — Every DJ line is on the record (gh-#293 gh-#920 · SPEC F218 · PLAN T684 T685 T682)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans three files by seam: AC1/AC2 (T684, LlmCopyWriter) here; AC3/AC4/AC6 (T684/T685,
// BoothLogWriter + engine callback) in tests/GenWave.Host.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Booth.cs;
// AC5/AC7 (T682, IUtteranceStore) in tests/GenWave.Memory.Tests/Specs/Story532_EveryDjLineIsOnTheRecord_Store.cs.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureEveryDjLineIsOnTheRecordCopy
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — one break, one row
    // entry point: LlmCopyWriter.WriteAsync
    public sealed class ScenarioOneBreakOneRow
    {
        // Given an active persona and a LeadIn request
        // When  LlmCopyWriter.WriteAsync accepts a copy

        [Fact(Skip = "pending: T684 — see docs/PLAN.md")]
        public void StationUtteranceHoldsExactlyOneNewRowForThatPersona() => Assert.Fail("pending: T684 — station.utterance holds exactly one new row for that persona");

        [Fact(Skip = "pending: T684 — see docs/PLAN.md")]
        public void ItsTextIsByteEqualToTheCopyHandedToTheRenderer() => Assert.Fail("pending: T684 — its text is byte-equal to the copy handed to the renderer");

        [Fact(Skip = "pending: T684 — see docs/PLAN.md")]
        public void ItsKindIsPatterItsSegmentKindIsLeadInAndFallbackIsFalse() => Assert.Fail("pending: T684 — its kind is patter, its segment_kind is LeadIn, and fallback is false");
    }

    // AC2 — the fallback is on the record too
    public sealed class ScenarioTheFallbackIsOnTheRecordToo
    {
        // Given the truth gate rejects the copy twice
        // When  the template line is used instead

        [Fact(Skip = "pending: T684 — see docs/PLAN.md")]
        public void TheStoredRowHasFallbackTrueAndTheTemplateText() => Assert.Fail("pending: T684 — the stored row has fallback = true and the template text");
    }
}
