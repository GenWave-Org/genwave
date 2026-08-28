// STORY-364 — The station's own name is never a lie (SPEC F138.8 · PLAN T350, wire T352)
//
// BDD specification — xUnit. PENDING: every Specification is skipped until T350 builds the
// behavior (the compile-clean-pending convention). /build-loop unskips per task; a body still
// failing after its task is a defect, not a pending.
//
// The 2026-08-28 exhibit is the pinned regression: announcement #1's re-ask carried the owner's
// sentence intact and was killed by the station name alone ("unsupported claim: 108.8") — the
// announcement lane runs CheckFacts against a fact block that is the owner's message, so the
// station's own digits read as an invented number. F138.8: the name's spans join the exempt
// spans the track title already gets, on EVERY lane. The deployed-entry-point scenario (the
// production preview endpoint through a scripted completions stub) lives in GenWave.Host.Tests
// (Story364_TheGateRulesOnThePreviewWire.cs, T352) — this file pins the pure CopyClaims seam.
namespace GenWave.Tts.Tests.Specs;

public static class FeatureStationNameIsNeverALie
{
    // ── Shared arrange — the exhibit, verbatim ─────────────────────────────────────────────
    const string StationName = "GWAV 108.8";
    const string OwnerMessage = "Dinner is ready — come and get it while it's hot.";
    const string ReaskCopy =
        "Well, rockers! This one's hot off the grill for ya! Dinner is ready — come and get it " +
        "while it's HOT! GWAV 108.8... Keep those fists pumping!";
    const string ContextFactBlock = "overcast · 15°C";

    // ---------------------------------------------------------------------
    // HAPPY PATH — the name is a supported fact wherever it appears
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheExhibitPasses
    {
        // Given the AC1 station and a fact block holding only the owner's message,
        // When CopyClaims.CheckFacts checks ReaskCopy with the station name as an exempt span.
        [Fact(Skip = "pending T350 (STORY-364 AC1)")]
        public void NoDigitRunViolationIsRaisedForTheStationsFrequency() =>
            Assert.Fail($"pending T350 — {StationName} / {OwnerMessage} / {ReaskCopy}");
    }

    public sealed class ScenarioTheNameAppearsTwice
    {
        // When the copy names the station mid-sentence and again at the end.
        [Fact(Skip = "pending T350 (STORY-364 AC2)")]
        public void NeitherOccurrenceRaisesADigitRunViolation() => Assert.Fail("pending T350");
    }

    public sealed class ScenarioTheNamesWordsAreExemptFromTheClockCheck
    {
        // Given "Saturday Night Radio" on a station-local Tuesday,
        // When CopyClaims.CheckClock checks "You're listening to Saturday Night Radio".
        [Fact(Skip = "pending T350 (STORY-364 AC3)")]
        public void NoWeekdayViolationIsRaisedForSaturday() => Assert.Fail("pending T350");
    }

    public sealed class ScenarioTheContextLaneIsUnchangedForEverythingElse
    {
        // Given ContextFactBlock and the station "GWAV 108.8",
        // When the copy says "a sunny 22 degrees on GWAV 108.8".
        [Fact(Skip = "pending T350 (STORY-364 AC4)")]
        public void TheViolationsAreExactlyTwentyTwoAndSunny() =>
            Assert.Fail($"pending T350 — {ContextFactBlock}");
    }

    public sealed class ScenarioTheLeadInLaneKeepsPassingTheName
    {
        // Given a LeadIn render with no fact block (CheckFacts never runs; CheckClock does),
        // When the copy ends "…right now on GWAV 108.8."
        [Fact(Skip = "pending T350 (STORY-364 AC5)")]
        public void TheGatePassesAsItDidBeforeF138_8() => Assert.Fail("pending T350");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — what is not the name is still a lie
    // ---------------------------------------------------------------------

    public sealed class ScenarioANumberThatIsNotTheNameStillViolates
    {
        // When the copy says "Dinner is ready at 7 PM — come and get it while it's hot."
        [Fact(Skip = "pending T350 (STORY-364 AC6)")]
        public void ADigitRunViolationIsRaisedForSeven() => Assert.Fail("pending T350");
    }

    public sealed class ScenarioAnEmptyStationNameExemptsNothing
    {
        // Given a blank station name, When the copy carries "108.8" with no supporting fact.
        [Fact(Skip = "pending T350 (STORY-364 AC7)")]
        public void ADigitRunViolationIsRaisedForTheFrequency() => Assert.Fail("pending T350");
    }
}
