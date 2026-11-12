// STORY-543 — Signature lines in moderation (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T709)
//
// BDD specification — xUnit. T709 activates every fact here.
// entry point: TaglineLedger.Evaluate (pure; reached via LlmCopyWriter.WriteAsync, see the _Writer file)

using GenWave.Core.Domain;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureSignatureLinesInModerationLedger
{
    const long PersonaId = 7;
    const string Years = "Cites years the way other people cite scores: 'nineteen fifty-nine, naturally'";
    const string Segues = "Calls segues 'arguments' \u2014 'this next track disagrees with the last one, listen'";
    const string SeguesSpan = "this next track disagrees with the last one, listen";
    const string Wrong = "Admits, rarely and grandly, when he is wrong";
    const string YearsSpan = "nineteen fifty-nine, naturally";
    const string YearsCopy = "Oh, nineteen fifty nine, naturally, what a year.";
    const string PlainCopy = "Here is another record from the pile.";

    static readonly DateTimeOffset Now = new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);
    static readonly ShiftKey Shift = ShiftKey.Unscheduled(PersonaId, new DateOnly(2026, 11, 10));

    static PersonaCard ProfessorB(params QuirkRule[] rules) =>
        new(PersonaCard.CurrentSchemaVersion, "Professor B", "Every record is a footnote to another record.", "A pedant.",
            [Years, Segues, Wrong], new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], [])
        {
            QuirkRules = rules.Length == 0 ? null : rules,
        };

    static PersonaCard CardQuoting(string quirk, params QuirkRule[] rules) =>
        ProfessorB() with { Quirks = [quirk], QuirkRules = rules.Length == 0 ? null : rules };

    /// <summary>Utterances newest first, each the given number of minutes before <see cref="Now"/>.</summary>
    static UtteranceEntry[] Utterances(params (int MinutesAgo, string Text)[] newestFirst) =>
        newestFirst.Select((u, i) => new UtteranceEntry(
            100 - i, PersonaId, null, UtteranceKind.Patter, "patter", Shift, u.Text, false,
            Now.AddMinutes(-u.MinutesAgo), null)).ToArray();

    static bool YearsAvailable(PersonaCard card, params UtteranceEntry[] recent) =>
        new TaglineLedger().Evaluate(card, recent, [], Now).Single(t => t.Span == YearsSpan).Available;

    static bool SeguesAvailable(string said) =>
        new TaglineLedger()
            .Evaluate(ProfessorB(), Utterances((2, PlainCopy), (5, PlainCopy), (8, PlainCopy), (20, said), (40, said)), [], Now)
            .Single(t => t.Span == SeguesSpan).Available;

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — taglines come from the card
    public sealed class ScenarioTaglinesComeFromTheCard
    {
        // Given Professor B's card with quirks quoting 'nineteen fifty-nine, naturally' and 'this next track disagrees with the last one, listen' and tagline "Every record is a footnote to another record."
        // When  the ledger evaluates the card with no utterances

        readonly IReadOnlyList<TaglineStatus> statuses = new TaglineLedger().Evaluate(ProfessorB(), [], [], Now);

        [Fact]
        public void ItReports3Taglines() => Assert.Equal(3, statuses.Count);

        [Fact]
        public void All3AreAvailable() => Assert.All(statuses, t => Assert.True(t.Available));
    }

    // AC2 — two uses in the hour spends it
    public sealed class ScenarioTwoUsesInTheHourSpendsIt
    {
        // Given two utterances in the last 60 minutes that use "nineteen fifty-nine, naturally", neither in the last 3
        // When  the ledger evaluates

        [Fact]
        public void ThatTaglineIsUnavailable() =>
            Assert.False(YearsAvailable(ProfessorB(),
                Utterances((2, PlainCopy), (5, PlainCopy), (8, PlainCopy), (20, YearsCopy), (40, YearsCopy))));
    }

    // AC3 — an hour later it is back
    public sealed class ScenarioAnHourLaterItIsBack
    {
        // Given the same two uses, both older than 60 minutes
        // When  the ledger evaluates

        [Fact]
        public void ThatTaglineIsAvailable() =>
            Assert.True(YearsAvailable(ProfessorB(),
                Utterances((2, PlainCopy), (5, PlainCopy), (8, PlainCopy), (65, YearsCopy), (90, YearsCopy))));
    }

    // AC4 — never back to back
    public sealed class ScenarioNeverBackToBack
    {
        // Given one use, in the persona's second-newest utterance
        // When  the ledger evaluates

        [Fact]
        public void ThatTaglineIsUnavailable() =>
            Assert.False(YearsAvailable(ProfessorB(), Utterances((2, PlainCopy), (5, YearsCopy), (8, PlainCopy))));
    }

    // AC5 — the card can set its own quota
    public sealed class ScenarioTheCardCanSetItsOwnQuota
    {
        // Given a QuirkRules entry for that quirk with maxPerHour 1 and minBreaksApart 0
        // When  one use sits in the last 60 minutes

        [Fact]
        public void ThatTaglineIsUnavailable() =>
            Assert.False(YearsAvailable(
                ProfessorB(new QuirkRule(Years, MaxPerHour: 1, MinBreaksApart: 0)),
                Utterances((2, PlainCopy), (5, PlainCopy), (8, PlainCopy), (20, YearsCopy))));
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC11 — once-a-shift still wins
    public sealed class ScenarioOnceAShiftStillWins
    {
        // Given a quirk with maxPerShift 1 already spent this shift and its quoted span unused this hour
        // When  the ledger evaluates

        [Fact]
        public void ThatSpanIsUnavailable() =>
            Assert.False(new TaglineLedger()
                .Evaluate(ProfessorB(new QuirkRule(Years, MaxPerShift: 1)), [], [Years], Now)
                .Single(t => t.Span == YearsSpan).Available);
    }

    // AC12 — a one-word quote is not a tagline
    public sealed class ScenarioAOneWordQuoteIsNotATagline
    {
        // Given a quirk quoting only 'folks'
        // When  the ledger evaluates

        [Fact]
        public void ItReportsNoTaglineForThatQuirk()
        {
            const string Quirk = "Greets everyone with 'folks'";

            Assert.DoesNotContain(
                new TaglineLedger().Evaluate(CardQuoting(Quirk), [], [], Now),
                t => t.QuirkText == Quirk);
        }
    }

    // AC13 (F218.39) — a long catchphrase is not used by its first words
    public sealed class ScenarioALongCatchphraseIsNotUsedByItsFirstWords
    {
        // Given the 9-word tagline and two utterances in the last 60 minutes, neither in the last 3, each saying only "this next track"
        // When  the ledger evaluates

        [Fact]
        public void ThatTaglineIsAvailable() => Assert.True(SeguesAvailable("Here is this next track, folks."));
    }

    // AC14 — five of its words are a use
    public sealed class ScenarioFiveOfItsWordsAreAUse
    {
        // Given the same tagline and two utterances each saying "this next track disagrees with the last"
        // When  the ledger evaluates

        [Fact]
        public void ThatTaglineIsUnavailable() => Assert.False(SeguesAvailable("Well, this next track disagrees with the last."));
    }

    // AC15 — a short catchphrase still needs only three
    public sealed class ScenarioAShortCatchphraseStillNeedsOnlyThree
    {
        // Given 'nineteen fifty-nine, naturally' and two utterances each saying "fifty nine naturally"
        // When  the ledger evaluates

        [Fact]
        public void ThatTaglineIsUnavailable() =>
            Assert.False(YearsAvailable(ProfessorB(),
                Utterances((2, PlainCopy), (5, PlainCopy), (8, PlainCopy), (20, "Fifty nine naturally."), (40, "Fifty nine naturally."))));
    }
}
