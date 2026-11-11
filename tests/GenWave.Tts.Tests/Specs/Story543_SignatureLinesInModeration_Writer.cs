// STORY-543 — Signature lines in moderation (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T712, T713)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: LlmCopyWriter.WriteAsync with a fake LLM and fake utterance store (the production air path)

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSignatureLinesInModerationWriter
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    const string YearsQuirk = "Cites years the way other people cite scores: 'nineteen fifty-nine, naturally'";
    const string YearsSpan = "nineteen fifty-nine, naturally";

    static IReadOnlyList<string> QuirksBlock(bool yearsAvailable) =>
        LlmPromptBuilder.BuildQuirksBlock([YearsQuirk, "Admits, rarely and grandly, when he is wrong"], [],
            [new TaglineStatus(YearsSpan, yearsAvailable, YearsQuirk)]);

    // AC7 — a spent tagline leaves the prompt
    public sealed class ScenarioASpentTaglineLeavesThePrompt
    {
        // Given the tagline "nineteen fifty-nine, naturally" is unavailable
        // When  the prompt is built
        readonly IReadOnlyList<string> block = QuirksBlock(yearsAvailable: false);

        [Fact]
        public void TheQuirksLineReadsCitesYearsTheWayOtherPeopleCiteScoresWithoutTheQuote() =>
            Assert.Equal("Quirks: Cites years the way other people cite scores: '…'; Admits, rarely and grandly, when he is wrong", block[0]);

        [Fact]
        public void OneLineReadsSaidLatelySkipThisBreakNineteenFiftyNineNaturally() =>
            Assert.Equal("Said lately, skip this break: \"nineteen fifty-nine, naturally\"",
                Assert.Single(block, l => l.StartsWith(LlmPromptBuilder.SaidLatelyPrefix, StringComparison.Ordinal)));
    }

    // AC8 — an available tagline is untouched
    public sealed class ScenarioAnAvailableTaglineIsUntouched
    {
        // Given every tagline is available
        // When  the prompt is built
        readonly IReadOnlyList<string> block = QuirksBlock(yearsAvailable: true);

        [Fact]
        public void TheQuirksLineCarriesTheQuotedSpanAsWritten() =>
            Assert.Equal($"Quirks: {YearsQuirk}; Admits, rarely and grandly, when he is wrong", block[0]);

        [Fact]
        public void NoSaidLatelyLineIsPresent() =>
            Assert.DoesNotContain(block, l => l.Contains("Said lately", StringComparison.Ordinal));
    }

    // T712 round 2 — placeholder edge cases
    public sealed class ScenarioASpentCappedQuirkNeverShowsTheSpentPhrase
    {
        readonly IReadOnlyList<string> block = LlmPromptBuilder.BuildQuirksBlock([YearsQuirk], [YearsQuirk],
            [new TaglineStatus(YearsSpan, false, YearsQuirk)]);

        [Fact]
        public void TheSpanAppearsOnlyOnTheSaidLatelyLine() =>
            Assert.Equal(1, string.Join("\n", block).Split(YearsSpan).Length - 1);
    }

    public sealed class ScenarioAQuirkThatIsOnlyAQuotedSpan
    {
        readonly IReadOnlyList<string> block = LlmPromptBuilder.BuildQuirksBlock(["'go on, then'", "Sighs"], [],
            [new TaglineStatus("go on, then", false, "'go on, then'")]);

        [Fact]
        public void ItBecomesThePlaceholderNotAnEmptyEntry() => Assert.Equal("Quirks: '…'; Sighs", block[0]);
    }

    public sealed class ScenarioASpanSpentTwiceIsListedOnce
    {
        const string Other = "Opens with 'nineteen fifty-nine, naturally' sometimes";
        readonly IReadOnlyList<string> block = LlmPromptBuilder.BuildQuirksBlock([YearsQuirk, Other], [],
        [
            new TaglineStatus(YearsSpan, false, YearsQuirk),
            new TaglineStatus(YearsSpan, false, Other),
            new TaglineStatus(YearsSpan, false, null),
        ]);

        [Fact]
        public void TheSaidLatelyLineNamesItOnce() =>
            Assert.Equal("Said lately, skip this break: \"nineteen fifty-nine, naturally\"",
                Assert.Single(block, l => l.StartsWith(LlmPromptBuilder.SaidLatelyPrefix, StringComparison.Ordinal)));
    }

    public sealed class ScenarioASpentSpanWhoseQuirkWasNotSampled
    {
        readonly IReadOnlyList<string> block = LlmPromptBuilder.BuildQuirksBlock(["Sighs"], [],
            [new TaglineStatus(YearsSpan, false, YearsQuirk)]);

        [Fact]
        public void ItIsAbsentFromTheSaidLatelyLine() =>
            Assert.DoesNotContain(block, l => l.Contains("Said lately", StringComparison.Ordinal));
    }

    public sealed class ScenarioABareMatchEarlierThanTheQuotedSpan
    {
        readonly IReadOnlyList<string> block = LlmPromptBuilder.BuildQuirksBlock(["Says go on, then 'go on'"], [],
            [new TaglineStatus("go on", false, "Says go on, then 'go on'")]);

        [Fact]
        public void TheQuotedOccurrenceIsTheOneReplaced() =>
            Assert.Equal("Quirks: Says go on, then '…'", block[0]);
    }

    public sealed class ScenarioAnUnrelatedQuoteSitsBeforeTheSpan
    {
        const string Quirk = "Says 'a' go on 'b' then 'go on'";
        readonly IReadOnlyList<string> block = LlmPromptBuilder.BuildQuirksBlock([Quirk], [],
            [new TaglineStatus("go on", false, Quirk)]);

        [Fact]
        public void OnlyTheQuoteTouchingTheSpanIsReplaced() =>
            Assert.Equal("Quirks: Says 'a' go on 'b' then '…'", block[0]);
    }

    public sealed class ScenarioASpentCardTaglineIsListed
    {
        const string Tagline = "Every record is a footnote to another record.";
        readonly IReadOnlyList<string> block = LlmPromptBuilder.BuildQuirksBlock(["Sighs"], [],
            [new TaglineStatus(Tagline, false, null)]);

        [Fact]
        public void TheSaidLatelyLineNamesIt() =>
            Assert.Equal($"Said lately, skip this break: \"{Tagline}\"",
                Assert.Single(block, l => l.StartsWith(LlmPromptBuilder.SaidLatelyPrefix, StringComparison.Ordinal)));
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
