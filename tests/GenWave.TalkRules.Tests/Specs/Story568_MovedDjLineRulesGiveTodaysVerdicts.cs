// STORY-568 — The moved DJ-line rules give today's verdicts (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureMovedDjLineRulesGiveTodaysVerdicts
{
    // AC1 — the fact fixtures (digits, weekday, condition words)
    public sealed class ScenarioOfferedFacts
    {
        [Fact(Skip = "pending: T783")]
        public void ItFlagsExactlyTheLinesCheckFactsFlags() { }

        [Fact(Skip = "pending: T783")]
        public void ADjLineWithNoFactsOfferedIsNotJudged() { }
    }

    // AC2 — the clock fixtures
    public sealed class ScenarioDayAndTime
    {
        [Fact(Skip = "pending: T783")]
        public void ItFlagsExactlyTheLinesCheckClockFlags() { }
    }

    // AC3 — an announcement whose reply drops the owner's words
    public sealed class ScenarioOwnersMessage
    {
        [Fact(Skip = "pending: T783")]
        public void ItIsFlagged() { }
    }

    // AC4 — the repeat fixtures
    public sealed class ScenarioRepeatedPhrases
    {
        [Fact(Skip = "pending: T784")]
        public void ItFlagsExactlyTheLinesTheSharedRunCheckFlags() { }

        [Fact(Skip = "pending: T784")]
        public void CardPhrasesAreNeverTheFlaggedWords() { }
    }

    // AC5 — a capped catchphrase not offered this break, and an unavailable tagline
    public sealed class ScenarioCatchphraseLimits
    {
        [Fact(Skip = "pending: T785")]
        public void TheCatchphraseIsFlagged() { }

        [Fact(Skip = "pending: T785")]
        public void TheUnavailableTaglineIsFlagged() { }
    }

    // AC6 — any flag from the truth rules
    public sealed class ScenarioTheAsk
    {
        [Fact(Skip = "pending: T783")]
        public void IsOneSentenceTellingTheModelWhatToFix() { }
    }

    // Sad path
    // AC7 — a preview render
    public sealed class ScenarioAPreviewRender
    {
        [Fact(Skip = "pending: T784")]
        public void RepeatPhrasesDoesNotJudge() { }
    }

    // Sad path
    // AC8 — no catchphrase memo on the snapshot
    public sealed class ScenarioNoCatchphraseMemo
    {
        [Fact(Skip = "pending: T785")]
        public void NothingIsFlagged() { }
    }
}
