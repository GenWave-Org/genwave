// STORY-364 + STORY-365 — the gate rules proven on the production preview path (PLAN T352)
//
// BDD specification — xUnit. PENDING until T352. The deployed entry point for both stories:
// POST /api/personas/preview through the REAL production binary (WebApplicationFactory<Program>,
// the Story345 factory idiom) against a scripted completions stub (the T335 recipe) — never
// CopyClaims in isolation. Station named "GWAV 108.8" via Station:Name.
namespace GenWave.Host.Tests.Specs;

public static class FeatureTheGateRulesOnThePreviewWire
{
    const string ReaskCopy =
        "Well, rockers! This one's hot off the grill for ya! Dinner is ready — come and get it " +
        "while it's HOT! GWAV 108.8... Keep those fists pumping!";
    const string AttemptOneFabrication =
        "Alright rockers, listen up! It's me, the Metal Maven, swinging into action here on GWAV 108.8. " +
        "Rumor has it our station owner whipped up a feast for us hungry listeners!";

    // ---------------------------------------------------------------------
    // HAPPY PATH — the exhibit airs in character
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheReaskCopyPassesThePreview
    {
        // Given the stub returns ReaskCopy verbatim and the preview carries the owner's message,
        // When POST /api/personas/preview runs through the production binary.
        [Fact(Skip = "pending T352 (STORY-364 AC1 · STORY-365 AC1 on the wire)")]
        public void ThePreviewAnswersTwoHundredWithThatCopy() => Assert.Fail($"pending T352 — {ReaskCopy}");

        [Fact(Skip = "pending T352 (STORY-364 AC1 on the wire)")]
        public void TheRingRecordsSuccess() => Assert.Fail("pending T352");
    }

    public sealed class ScenarioALeadInPreviewStillNamesTheStation
    {
        // When the stub returns "…right now on GWAV 108.8." for a LeadIn preview.
        [Fact(Skip = "pending T352 (STORY-364 AC5 on the wire)")]
        public void ThePreviewAnswersTwoHundredUnchanged() => Assert.Fail("pending T352");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — fabrication still dies at the gate
    // ---------------------------------------------------------------------

    public sealed class ScenarioAttemptOneIsStillRejected
    {
        // Given the stub returns AttemptOneFabrication on both the first ask and the re-ask.
        [Fact(Skip = "pending T352 (STORY-365 AC6 on the wire)")]
        public void ThePreviewAnswersFiveOhTwoNamingTheDroppedCore() => Assert.Fail($"pending T352 — {AttemptOneFabrication}");

        [Fact(Skip = "pending T352 (STORY-365 AC6 on the wire)")]
        public void TheRingRecordsTruthGateReject() => Assert.Fail("pending T352");
    }
}
