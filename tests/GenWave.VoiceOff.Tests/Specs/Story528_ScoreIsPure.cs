// STORY-528 — Score is pure (gh-#614 · SPEC F217 · PLAN T666)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-26); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureScoreIsPure
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the ranking follows the judgement
    public sealed class ScenarioTheRankingFollowsTheJudgement
    {
        // Given a judgement ordering three cards
        // When  score runs

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void TheRankingTableListsThemInThatOrder() => Assert.Fail("pending: T666 — the ranking table lists them in that order");

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void TheFirstRowIsRank1() => Assert.Fail("pending: T666 — the first row is rank 1");

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void EachRowCarriesCardEngineHostWincesStarsAndWhy() => Assert.Fail("pending: T666 — each row carries card, engine, host, winces, stars and why");
    }

    // AC2 — notes are kept
    public sealed class ScenarioNotesAreKept
    {
        // Given the judgement
        // When  RESULTS.md is read

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void ThereIsANotesSectionPerCard() => Assert.Fail("pending: T666 — there is a notes section per card");

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void EachNoteIsVerbatim() => Assert.Fail("pending: T666 — each note is verbatim");
    }

    // AC3 — the tray is printed
    public sealed class ScenarioTheTrayIsPrinted
    {
        // Given a run with a failed card
        // When  RESULTS.md is read

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void TheDidNotRenderSectionNamesTheCardAndItsReason() => Assert.Fail("pending: T666 — the did-not-render section names the card and its reason");
    }

    // AC4 — the facts are printed
    public sealed class ScenarioTheFactsArePrinted
    {
        // Given the cards
        // When  RESULTS.md is read

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void AFactsTableListsVoicesCloningOwnVoiceAndCannotPerCard() => Assert.Fail("pending: T666 — a facts table lists voices, cloning, own voice and cannot per card");
    }

    // AC5 — the header names the run
    public sealed class ScenarioTheHeaderNamesTheRun
    {
        // Given run.json
        // When  RESULTS.md is read

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void ItCarriesTheRunId() => Assert.Fail("pending: T666 — it carries the run id");

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void ItCarriesTheDateFromRunJson() => Assert.Fail("pending: T666 — it carries the date from run.json");

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void ItCarriesTheScriptHash() => Assert.Fail("pending: T666 — it carries the script hash");

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void ItCarriesTheHosts() => Assert.Fail("pending: T666 — it carries the hosts");
    }

    // AC6 — unjudged cards sort last
    public sealed class ScenarioUnjudgedCardsSortLast
    {
        // Given an order naming one of three cards
        // When  score runs

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void TheUnnamedCardsFollowInIdOrder() => Assert.Fail("pending: T666 — the unnamed cards follow in id order");
    }

    // AC7 — the output is deterministic
    public sealed class ScenarioTheOutputIsDeterministic
    {
        // Given the same inputs
        // When  score runs twice

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void TheTwoFilesAreByteIdentical() => Assert.Fail("pending: T666 — the two files are byte-identical");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — a script hash mismatch is refused
    public sealed class ScenarioAScriptHashMismatchIsRefused
    {
        // Given run.json with a different script hash
        // When  score runs

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void ItExitsNonZeroNamingBothHashes() => Assert.Fail("pending: T666 — it exits non-zero naming both hashes");
    }

    // AC9 — no judgement is refused
    public sealed class ScenarioNoJudgementIsRefused
    {
        // Given a run without judgement.json
        // When  score runs

        [Fact(Skip = "pending: T666 — see docs/PLAN.md")]
        public void ItExitsNonZeroSayingNothingHasBeenJudgedYet() => Assert.Fail("pending: T666 — it exits non-zero saying nothing has been judged yet");
    }
}
