// STORY-543 — Signature lines in moderation (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T712, T713)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: LlmCopyWriter.WriteAsync with a fake LLM and fake utterance store (the production air path)

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSignatureLinesInModerationWriter
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC7 — a spent tagline leaves the prompt
    public sealed class ScenarioASpentTaglineLeavesThePrompt
    {
        // Given the tagline "nineteen fifty-nine, naturally" is unavailable
        // When  the prompt is built

        [Fact(Skip = "pending: T712 — see docs/PLAN.md")]
        public void TheQuirksLineReadsCitesYearsTheWayOtherPeopleCiteScoresWithoutTheQuote() => Assert.Fail("pending: T712 — the quirks line reads \"Cites years the way other people cite scores\" without the quote");

        [Fact(Skip = "pending: T712 — see docs/PLAN.md")]
        public void OneLineReadsSaidLatelySkipThisBreakNineteenFiftyNineNaturally() => Assert.Fail("pending: T712 — one line reads: Said lately, skip this break: \"nineteen fifty-nine, naturally\"");
    }

    // AC8 — an available tagline is untouched
    public sealed class ScenarioAnAvailableTaglineIsUntouched
    {
        // Given every tagline is available
        // When  the prompt is built

        [Fact(Skip = "pending: T712 — see docs/PLAN.md")]
        public void TheQuirksLineCarriesTheQuotedSpanAsWritten() => Assert.Fail("pending: T712 — the quirks line carries the quoted span as written");

        [Fact(Skip = "pending: T712 — see docs/PLAN.md")]
        public void NoSaidLatelyLineIsPresent() => Assert.Fail("pending: T712 — no \"Said lately\" line is present");
    }

    // AC9 — inside quota it is not a repeat
    public sealed class ScenarioInsideQuotaItIsNotARepeat
    {
        // Given one earlier use, five utterances ago, and a copy that says the tagline again
        // When  WriteAsync runs on air

        [Fact(Skip = "pending: T713 — see docs/PLAN.md")]
        public void TheCopyIsStoredAndRenderedUnchanged() => Assert.Fail("pending: T713 — the copy is stored and rendered unchanged");

        [Fact(Skip = "pending: T713 — see docs/PLAN.md")]
        public void NoRepeatCaughtRowIsWritten() => Assert.Fail("pending: T713 — no repeat-caught row is written");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC10 — over quota it is a repeat
    public sealed class ScenarioOverQuotaItIsARepeat
    {
        // Given the tagline is unavailable and the copy says it anyway
        // When  the repetition check runs

        [Fact(Skip = "pending: T713 — see docs/PLAN.md")]
        public void ItReportsOneHitNamingTheTagline() => Assert.Fail("pending: T713 — it reports one hit naming the tagline");
    }
}
