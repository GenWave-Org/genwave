// STORY-365 — My words count as preserved in any case and punctuation (SPEC F144.3 amended · PLAN T350, wire T352)
//
// BDD specification — xUnit. PENDING until T350 (the compile-clean-pending convention).
//
// F144.3 amended (gh-#632): "the core" is the message's WORD SEQUENCE — both message and copy
// are hygiene-normalised, case-folded, and reduced to word tokens (punctuation is not a word),
// and the message's token run must appear contiguous and in order in the copy. Case and
// punctuation are free; words are not. The MEDIUM-B invariant (an all-markup core is a
// violation, never a vacuous pass) stands. Subject: CopyClaims.CheckContainment.
namespace GenWave.Tts.Tests.Specs;

public static class FeatureWordsCountAsPreserved
{
    const string DinnerCore = "Dinner is ready — come and get it while it's hot.";

    // ---------------------------------------------------------------------
    // HAPPY PATH — case and punctuation are free
    // ---------------------------------------------------------------------

    public sealed class ScenarioTerminalPunctuationIsFree
    {
        // When the copy contains "Dinner is ready — come and get it while it's HOT! GWAV 108.8".
        [Fact(Skip = "pending T350 (STORY-365 AC1)")]
        public void ContainmentPasses() => Assert.Fail($"pending T350 — {DinnerCore}");
    }

    public sealed class ScenarioCaseIsFree
    {
        // Given the core "the garage sale starts at nine",
        // When the copy contains "The Garage Sale Starts At Nine, folks".
        [Fact(Skip = "pending T350 (STORY-365 AC2)")]
        public void ContainmentPasses() => Assert.Fail("pending T350");
    }

    public sealed class ScenarioInteriorPunctuationAndApostrophesAreFree
    {
        // Given the core "Mom's flight lands at 6 - pick her up",
        // When the copy contains "Mom’s flight lands at 6, pick her up" (U+2019, comma for dash).
        [Fact(Skip = "pending T350 (STORY-365 AC3)")]
        public void ContainmentPasses() => Assert.Fail("pending T350");
    }

    public sealed class ScenarioTheRunMustBeContiguous
    {
        // When the copy contains "Dinner is ready, rockers, come and get it while it's hot".
        [Fact(Skip = "pending T350 (STORY-365 AC4)")]
        public void ContainmentFailsWithAnAnnouncementCoreViolation() => Assert.Fail("pending T350");
    }

    public sealed class ScenarioTheRunMustBeInOrder
    {
        // Given the core "come and get it while it's hot",
        // When the copy contains "while it's hot, come and get it".
        [Fact(Skip = "pending T350 (STORY-365 AC5)")]
        public void ContainmentFailsWithAnAnnouncementCoreViolation() => Assert.Fail("pending T350");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — words are not free
    // ---------------------------------------------------------------------

    public sealed class ScenarioAParaphraseIsStillAReject
    {
        // The 2026-08-28 attempt shape: "Dinner's ready and steamin' hot, so dig in while it lasts!"
        [Fact(Skip = "pending T350 (STORY-365 AC6)")]
        public void ContainmentFailsWithAnAnnouncementCoreViolation() => Assert.Fail("pending T350");
    }

    public sealed class ScenarioAnAllMarkupCoreIsAViolationNeverAVacuousPass
    {
        // Given the core "*urgent*" (hygiene-strips to nothing), When any copy is checked.
        [Fact(Skip = "pending T350 (STORY-365 AC7)")]
        public void ContainmentFailsWithAnAnnouncementCoreViolation() => Assert.Fail("pending T350");
    }
}
