// STORY-544 — A track name is not a repeat (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T710)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-10); the named task activates its
// facts, watches them fail, then makes them pass.
// entry point: IRepetitionCheck (pure; reached via LlmCopyWriter.WriteAsync)

namespace GenWave.Memory.Tests.Specs;

public static class FeatureATrackNameIsNotARepeat
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — five words make a hit
    public sealed class ScenarioFiveWordsMakeAHit
    {
        // Given a recent utterance "now this next track disagrees with the last one"
        // When  the candidate contains "this next track disagrees with the"

        [Fact(Skip = "pending: T710 — see docs/PLAN.md")]
        public void TheCheckReportsOneHitNamingThatPhrase() => Assert.Fail("pending: T710 — the check reports one hit naming that phrase");
    }

    // AC2 — four words do not
    public sealed class ScenarioFourWordsDoNot
    {
        // Given a recent utterance "next up we've got something special"
        // When  the candidate contains "next up we've got"

        [Fact(Skip = "pending: T710 — see docs/PLAN.md")]
        public void TheCheckReportsNoHit() => Assert.Fail("pending: T710 — the check reports no hit");
    }

    // AC3 — a possessive artist is still the artist
    public sealed class ScenarioAPossessiveArtistIsStillTheArtist
    {
        // Given the track "Knowing Me, Knowing You" by "ABBA" and a recent utterance "That was ABBA's Knowing Me, Knowing You"
        // When  the candidate says "ABBA's Knowing Me, Knowing You, coming up"

        [Fact(Skip = "pending: T710 — see docs/PLAN.md")]
        public void TheCheckReportsNoHit() => Assert.Fail("pending: T710 — the check reports no hit");
    }

    // AC4 — the track before counts too
    public sealed class ScenarioTheTrackBeforeCountsToo
    {
        // Given the crossing track "Do You Believe in Shame" by "Duran Duran"
        // When  the candidate repeats "Duran Duran's Do You Believe in Shame" from a recent utterance

        [Fact(Skip = "pending: T710 — see docs/PLAN.md")]
        public void TheCheckReportsNoHit() => Assert.Fail("pending: T710 — the check reports no hit");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC5 — a name does not hide the words around it
    public sealed class ScenarioANameDoesNotHideTheWordsAroundIt
    {
        // Given a recent utterance "ABBA's Knowing Me, Knowing You, and what a night it has been so far"
        // When  the candidate repeats "and what a night it has been so far"

        [Fact(Skip = "pending: T710 — see docs/PLAN.md")]
        public void TheCheckReportsOneHit() => Assert.Fail("pending: T710 — the check reports one hit");
    }
}
