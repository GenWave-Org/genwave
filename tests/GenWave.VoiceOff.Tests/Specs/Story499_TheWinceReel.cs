// STORY-499 — The wince reel (gh-#614 · SPEC F214.13 · PLAN T618)
//
// BDD specification — xUnit. Every fact is pending at plan time (2026-10-21); the named task
// activates its facts, watches them fail, then makes them pass.

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheWinceReel
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — one reel per engine in gauntlet order
    public sealed class ScenarioOneReelPerEngineInGauntletOrder
    {
        // Given 10 ok plain-lane clips for an engine
        // When  `reel` runs

        [Fact(Skip = "pending: T618 — see docs/PLAN.md")]
        public void TheReelHoldsThemInGauntletOrderWith06SGaps() => Assert.Fail("pending: T618 — the reel holds them in gauntlet order with 0.6 s gaps");
    }

    // AC2 — the reel is matched
    public sealed class ScenarioTheReelIsMatched
    {
        // Given a built reel
        // When  it is measured

        [Fact(Skip = "pending: T618 — see docs/PLAN.md")]
        public void ItReads16LUFS1LU() => Assert.Fail("pending: T618 — it reads −16 LUFS ±1 LU");
    }

    // AC3 — winner and two decoys
    public sealed class ScenarioWinnerAndTwoDecoys
    {
        // Given a ranking winner W, runner-up R, and Kokoro
        // When  `reel` builds the wince session

        [Fact(Skip = "pending: T618 — see docs/PLAN.md")]
        public void ItOffersExactlyThreeReelsUnderOpaqueIdsWRKokoro() => Assert.Fail("pending: T618 — it offers exactly three reels under opaque ids: W, R, Kokoro");
    }

    // AC4 — truth after all three counts
    public sealed class ScenarioTruthAfterAllThreeCounts
    {
        // Given wince counts entered for two reels
        // When  GET /wince

        [Fact(Skip = "pending: T618 — see docs/PLAN.md")]
        public void NoEngineNameIsReturned() => Assert.Fail("pending: T618 — no engine name is returned");
    }

    // AC5 — truth revealed
    public sealed class ScenarioTruthRevealed
    {
        // Given wince counts for all three reels
        // When  GET /wince

        [Fact(Skip = "pending: T618 — see docs/PLAN.md")]
        public void EachReelSEngineIsNamed() => Assert.Fail("pending: T618 — each reel's engine is named");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC6 — no runner-up
    public sealed class ScenarioNoRunnerUp
    {
        // Given a single gated challenger
        // When  `reel` builds the session

        [Fact(Skip = "pending: T618 — see docs/PLAN.md")]
        public void TheSecondDecoyIsPiperIfGatedElseTheReelSetIsTwo() => Assert.Fail("pending: T618 — the second decoy is Piper if gated, else the reel set is two");

        [Fact(Skip = "pending: T618 — see docs/PLAN.md")]
        public void StdoutSaysSo() => Assert.Fail("pending: T618 — stdout says so");
    }
}
