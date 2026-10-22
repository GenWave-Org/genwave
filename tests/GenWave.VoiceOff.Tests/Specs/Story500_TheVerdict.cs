// STORY-500 — The verdict (gh-#614 · SPEC F214.11, F214.12, F214.14, F214.15 · PLAN T614)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheVerdict
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — a clear win
    public sealed class ScenarioAClearWin
    {
        // Given plain-lane votes 30 wins / 10 losses, Dean 15/5, wife 15/5, winces 0
        // When  `score` runs

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void TheTierVerdictReadsBEATSKOKOROEngine075N40Winces0() => Assert.Fail("pending: T614 — the tier verdict reads 'BEATS KOKORO: <engine> (0.75, n=40, winces=0)'");
    }

    // AC2 — ties are counted, not divided
    public sealed class ScenarioTiesAreCountedNotDivided
    {
        // Given 30 wins, 10 losses, 8 ties
        // When  winRate is computed

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void ItIs075AndTiesShow8() => Assert.Fail("pending: T614 — it is 0.75 and ties show 8");
    }

    // AC3 — winces block the win
    public sealed class ScenarioWincesBlockTheWin
    {
        // Given a clear plain-lane win and a wince count of 2
        // When  `score` runs

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void TheVerdictReadsBEATSKOKOROONPAPERWINCES2() => Assert.Fail("pending: T614 — the verdict reads 'BEATS KOKORO ON PAPER, WINCES: 2'");
    }

    // AC4 — nobody wins
    public sealed class ScenarioNobodyWins
    {
        // Given every engine below 0.65
        // When  `score` runs

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void TheVerdictReadsNOBODYBEATSKOKORO() => Assert.Fail("pending: T614 — the verdict reads 'NOBODY BEATS KOKORO'");
    }

    // AC5 — one judge must agree
    public sealed class ScenarioOneJudgeMustAgree
    {
        // Given overall 0.70 but wife 0.50
        // When  `score` runs

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void TheEngineDoesNotBeatKokoro() => Assert.Fail("pending: T614 — the engine does not beat Kokoro");
    }

    // AC6 — markup informs only
    public sealed class ScenarioMarkupInformsOnly
    {
        // Given markup 0.80 and plain 0.55
        // When  `score` runs

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void TheRowCarriesAdoptOnlyWithACueMapper() => Assert.Fail("pending: T614 — the row carries 'adopt only with a cue mapper'");

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void TheVerdictIsNotAWinForIt() => Assert.Fail("pending: T614 — the verdict is not a win for it");
    }

    // AC7 — the tables are present
    public sealed class ScenarioTheTablesArePresent
    {
        // Given a scored run
        // When  RESULTS.md is read

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void ItHoldsRunIdDateHostTierGauntletHashGateTableScoredTablePerLaneWinRateTablesWinc() => Assert.Fail("pending: T614 — it holds run id, date, host, tier, gauntlet hash, gate table, scored table, per-lane win-rate tables, wince counts");
    }

    // AC8 — the budget reference line
    public sealed class ScenarioTheBudgetReferenceLine
    {
        // Given the scored table
        // When  it is read

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void ItShowsTheReferenceRtf15FromRenderBudgetSeconds30ForA20SLine() => Assert.Fail("pending: T614 — it shows the reference rtf 1.5 from RenderBudgetSeconds 30 for a 20 s line");
    }

    // AC9 — deterministic
    public sealed class ScenarioDeterministic
    {
        // Given the same fixtures scored twice
        // When  the two RESULTS.md are compared

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void TheyAreByteIdentical() => Assert.Fail("pending: T614 — they are byte-identical");
    }

    // AC10 — last vote counts
    public sealed class ScenarioLastVoteCounts
    {
        // Given a judge voted a then b on one pair
        // When  `score` runs

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void OneVoteBIsCounted() => Assert.Fail("pending: T614 — one vote b is counted");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC11 — too few pairs
    public sealed class ScenarioTooFewPairs
    {
        // Given 39 decided pairs
        // When  `score` runs

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void ItPrintsInsufficientPairs39() => Assert.Fail("pending: T614 — it prints 'insufficient pairs (39)'");

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void NoVerdictForThatEngine() => Assert.Fail("pending: T614 — no verdict for that engine");
    }

    // AC12 — mismatched gauntlet
    public sealed class ScenarioMismatchedGauntlet
    {
        // Given two runs with different gauntlet hashes
        // When  `score` merges them

        [Fact(Skip = "pending: T614 — see docs/PLAN.md")]
        public void ItRefusesNamingBothHashes() => Assert.Fail("pending: T614 — it refuses naming both hashes");
    }
}
