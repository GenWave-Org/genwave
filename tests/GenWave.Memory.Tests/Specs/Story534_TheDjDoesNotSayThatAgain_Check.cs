// STORY-534 — The DJ does not say that again (gh-#293 gh-#920 · SPEC F218 · PLAN T691)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-11-08); the named task
// activates its facts, watches them fail, then makes them pass.
//
// Spans two files by seam: AC2–AC4 (T691, IRepetitionCheck) here; AC1/AC5–AC8 (T692/T693, prompt + writer) in
// tests/GenWave.Tts.Tests/Specs/Story534_TheDjDoesNotSayThatAgain_Writer.cs.

namespace GenWave.Memory.Tests.Specs;

public static class FeatureTheDjDoesNotSayThatAgainCheck
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC2 — a four-word run is a hit
    // entry point: IRepetitionCheck (pure; reached via LlmCopyWriter.WriteAsync)
    public sealed class ScenarioAFourWordRunIsAHit
    {
        // Given a recent utterance "welcome to the dance floor of the airwaves tonight"
        // When  the candidate copy contains "the dance floor of the airwaves"

        [Fact(Skip = "pending: T691 — see docs/PLAN.md")]
        public void TheCheckReportsOneHitNamingThatPhrase() => Assert.Fail("pending: T691 — the check reports one hit naming that phrase");
    }

    // AC3 — names and titles are not hits
    public sealed class ScenarioNamesAndTitlesAreNotHits
    {
        // Given the station name "GenWave Radio" and track "Dancing in the Dark" by "Bruce Springsteen"
        // When  the candidate repeats only those words in a run

        [Fact(Skip = "pending: T691 — see docs/PLAN.md")]
        public void TheCheckReportsNoHit() => Assert.Fail("pending: T691 — the check reports no hit");
    }

    // AC4 — a spent catchphrase is a hit
    public sealed class ScenarioASpentCatchphraseIsAHit
    {
        // Given the quirk "Declares at least one song per shift 'officially a banger, log it'" is spent this shift
        // When  the candidate contains "banger, log it"

        [Fact(Skip = "pending: T691 — see docs/PLAN.md")]
        public void TheCheckReportsOneHitNamingOfficiallyABangerLogIt() => Assert.Fail("pending: T691 — the check reports one hit naming \"officially a banger, log it\"");
    }
}
