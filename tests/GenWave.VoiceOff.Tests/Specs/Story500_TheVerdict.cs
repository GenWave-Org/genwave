// STORY-500 — The verdict (gh-#614 · SPEC F214.11, F214.12, F214.14, F214.15 · PLAN T614)
//
// BDD specification — xUnit. The scenarios drive `score` through Cli.Run over fixture runs in a temp root
// (made-up slugs: alpha is the baseline, beta and gamma the challengers) and read the RESULTS file it wrote.

using GenWave.VoiceOff.Speech;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureTheVerdict
{
    private static JudgeTally Dean(int wins, int losses, int ties = 0) => new("Dean", wins, losses, ties);

    private static JudgeTally Wife(int wins, int losses, int ties = 0) => new("Wife", wins, losses, ties);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — a clear win
    public sealed class ScenarioAClearWin : IDisposable
    {
        // Given plain-lane votes 30 wins / 10 losses, Dean 15/5, wife 15/5, winces 0
        // When  `score` runs
        private readonly ScoreScenario scenario = new();
        private readonly int exit;

        public ScenarioAClearWin()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(15, 5)).Reel("beta", winces: 0);
            exit = scenario.Score();
        }

        [Fact]
        public void TheTierVerdictReadsBEATSALPHABetaN40Winces0() =>
            Assert.Contains("**Verdict: BEATS ALPHA: beta (0.75, n=40, winces=0)**", scenario.Results(), StringComparison.Ordinal);

        [Fact]
        public void TheVerdictLineIsPrintedForTheTierAndTheExitIsZero() =>
            Assert.Equal((0, "cpu: BEATS ALPHA: beta (0.75, n=40, winces=0)"), (exit, scenario.Stdout.Split('\n')[0]));

        public void Dispose() => scenario.Dispose();
    }

    // AC2 — ties are counted, not divided
    public sealed class ScenarioTiesAreCountedNotDivided : IDisposable
    {
        // Given 30 wins, 10 losses, 8 ties
        // When  winRate is computed
        private readonly ScoreScenario scenario = new();

        public ScenarioTiesAreCountedNotDivided()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5, 4), Wife(15, 5, 4));
            scenario.Score();
        }

        [Fact]
        public void ItIs075AndTiesShow8() =>
            Assert.Contains("| beta | 30 | 10 | 8 | 40 | 0.75 |", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // AC3 — winces block the win
    public sealed class ScenarioWincesBlockTheWin : IDisposable
    {
        // Given a clear plain-lane win and a wince count of 2
        // When  `score` runs
        private readonly ScoreScenario scenario = new();

        public ScenarioWincesBlockTheWin()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(15, 5)).Reel("beta", winces: 2);
            scenario.Score();
        }

        [Fact]
        public void TheVerdictReadsBEATSALPHAONPAPERWINCES2() =>
            Assert.Contains("**Verdict: BEATS ALPHA ON PAPER, WINCES: 2**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // AC4 — nobody wins
    public sealed class ScenarioNobodyWins : IDisposable
    {
        // Given every engine below 0.65
        // When  `score` runs
        private readonly ScoreScenario scenario = new("alpha", "beta", "gamma");

        public ScenarioNobodyWins()
        {
            scenario.AddRun()
                .Plain("beta", Dean(10, 10), Wife(10, 10))
                .Plain("gamma", Dean(12, 8), Wife(12, 8));
            scenario.Score();
        }

        [Fact]
        public void TheVerdictReadsNOBODYBEATSALPHA() =>
            Assert.Contains("**Verdict: NOBODY BEATS ALPHA**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // AC5 — one judge must agree
    public sealed class ScenarioOneJudgeMustAgree : IDisposable
    {
        // Given overall 0.70 but wife 0.50
        // When  `score` runs
        private readonly ScoreScenario scenario = new();

        public ScenarioOneJudgeMustAgree()
        {
            scenario.AddRun().Plain("beta", Dean(25, 5), Wife(10, 10)).Reel("beta", winces: 0);
            scenario.Score();
        }

        [Fact]
        public void TheEngineDoesNotBeatAlpha() =>
            Assert.Contains("**Verdict: NOBODY BEATS ALPHA**", scenario.Results(), StringComparison.Ordinal);

        [Fact]
        public void TheOverallRateIsStill070() =>
            Assert.Contains("| beta | 35 | 15 | 0 | 50 | 0.70 |", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // AC6 — markup informs only
    public sealed class ScenarioMarkupInformsOnly : IDisposable
    {
        // Given markup 0.80 and plain 0.55
        // When  `score` runs
        private readonly ScoreScenario scenario = new();

        public ScenarioMarkupInformsOnly()
        {
            scenario.AddRun()
                .Plain("beta", Dean(11, 9), Wife(11, 9))
                .Markup("beta", Dean(16, 4), Wife(16, 4))
                .Reel("beta", winces: 0);
            scenario.Score();
        }

        [Fact]
        public void TheRowCarriesAdoptOnlyWithACueMapper() =>
            Assert.Contains("adopt only with a cue mapper", scenario.Results(), StringComparison.Ordinal);

        [Fact]
        public void TheVerdictIsNotAWinForIt() =>
            Assert.Contains("**Verdict: NOBODY BEATS ALPHA**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // AC7 — the tables are present
    public sealed class ScenarioTheTablesArePresent : IDisposable
    {
        // Given a scored run
        // When  RESULTS.md is read
        private readonly ScoreScenario scenario = new();

        public ScenarioTheTablesArePresent()
        {
            ScoreRunBuilder run = scenario.AddRun(startedAt: "2026-10-22T10:00:00Z", hash: "hash-seven");
            run.Host = "box-seven";
            run.Plain("beta", Dean(15, 5), Wife(15, 5)).Markup("beta", Dean(1, 1), Wife(1, 1)).Reel("beta", winces: 1);
            scenario.Score();
        }

        [Fact]
        public void ItHoldsRunIdDateHostTierGauntletHashGateTableScoredTablePerLaneWinRateTablesWinc()
        {
            string results = scenario.Results();
            string[] missing =
            [
                "- Run: 20261022T100000Z-cpu",
                "- Date: 2026-10-22",
                "- Host: box-seven",
                "- Tier: cpu",
                "- Gauntlet hash: hash-seven",
                "### 🚦 Gates",
                "| alpha | ✅ ok | ✅ ok |",
                "### ⏱️ Scored",
                "| Engine | Median rtf | p95 rtf | Peak RSS (MB) | Peak VRAM (MB) | License |",
                "| beta | 0.15 | 0.15 | 900 | — | MIT |",
                "### 🎧 Win rate, plain lane",
                "### 🎧 Win rate, markup lane",
                "| Engine | Wins | Losses | Ties | n | Win rate | Dean | Wife | Note |",
                "| beta | 30 | 10 | 0 | 40 | 0.75 | 0.75 (15/5) | 0.75 (15/5) |",
                "### 😬 Wince counts",
                "| beta | 1 |",
            ];
            Assert.Equal(string.Empty, string.Join(" || ", missing.Where(fragment => !results.Contains(fragment, StringComparison.Ordinal))));
        }

        public void Dispose() => scenario.Dispose();
    }

    // AC8 — the budget reference line
    public sealed class ScenarioTheBudgetReferenceLine : IDisposable
    {
        // Given the scored table
        // When  it is read
        private readonly ScoreScenario scenario = new();
        private readonly string plain;
        private readonly string withBudget;

        public ScenarioTheBudgetReferenceLine()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(15, 5));
            scenario.Score("plain.md");
            plain = scenario.Results("plain.md");
            scenario.Score("budget.md", "--budget-seconds", "120");
            withBudget = scenario.Results("budget.md");
        }

        [Fact]
        public void ItShowsTheReferenceRtf15FromRenderBudgetSeconds30ForA20SLine() =>
            Assert.Contains("Speed reference: shipped budget 30 s for a 20 s line ⇒ rtf 1.5", plain, StringComparison.Ordinal);

        [Fact]
        public void WithoutTheFlagThereIsNoSecondLine() =>
            Assert.DoesNotContain("Your box", plain, StringComparison.Ordinal);

        [Fact]
        public void TheBudgetFlagAddsYourBoxAtRtf6() =>
            Assert.Contains("Your box: 120 s for a 20 s line ⇒ rtf 6", withBudget, StringComparison.Ordinal);

        [Fact]
        public void TheShippedLineStaysBesideIt() =>
            Assert.Contains("Speed reference: shipped budget 30 s for a 20 s line ⇒ rtf 1.5", withBudget, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // AC9 — deterministic
    public sealed class ScenarioDeterministic : IDisposable
    {
        // Given the same fixtures scored twice
        // When  the two RESULTS.md are compared
        private readonly ScoreScenario scenario = new("alpha", "beta", "gamma");

        public ScenarioDeterministic()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5, 2), Wife(15, 5, 1)).Markup("gamma", Dean(3, 2), Wife(2, 3)).Reel("beta", winces: 0);
            scenario.AddRun(EngineTier.Gpu, "2026-10-22T11:00:00Z").Plain("gamma", Dean(20, 0), Wife(20, 0));
            scenario.Score("first.md");
            scenario.Score("second.md");
        }

        [Fact]
        public void TheyAreByteIdentical() =>
            Assert.Equal(File.ReadAllBytes(scenario.OutPath("first.md")), File.ReadAllBytes(scenario.OutPath("second.md")));

        [Fact]
        public void TheCpuTierComesBeforeTheGpuTier()
        {
            string results = scenario.Results("first.md");
            Assert.True(results.IndexOf("Tier: cpu", StringComparison.Ordinal) < results.IndexOf("Tier: gpu", StringComparison.Ordinal));
        }

        [Fact]
        public void TheFileEndsInOneNewline() =>
            Assert.EndsWith("\n", scenario.Results("first.md"), StringComparison.Ordinal);

        [Fact]
        public void TheFileHasNoCarriageReturns() =>
            Assert.DoesNotContain('\r', scenario.Results("first.md"));

        public void Dispose() => scenario.Dispose();
    }

    // AC10 — last vote counts
    public sealed class ScenarioLastVoteCounts : IDisposable
    {
        // Given a judge voted a then b on one pair
        // When  `score` runs
        private readonly ScoreScenario scenario = new();

        public ScenarioLastVoteCounts()
        {
            ScoreRunBuilder run = scenario.AddRun();
            string pair = run.Pair("beta");
            run.Vote(pair, "Dean", "a").Vote(pair, " Dean ", "b");
            scenario.Score();
        }

        [Fact]
        public void OneVoteBIsCounted() =>
            Assert.Contains("| beta | 0 | 1 | 0 | 1 | 0.00 |", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC11 — too few pairs
    public sealed class ScenarioTooFewPairs : IDisposable
    {
        // Given 39 decided pairs
        // When  `score` runs
        private readonly ScoreScenario scenario = new();

        public ScenarioTooFewPairs()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(14, 5)).Reel("beta", winces: 0);
            scenario.Score();
        }

        [Fact]
        public void ItPrintsInsufficientPairs39() =>
            Assert.Contains("insufficient pairs (39)", scenario.Results(), StringComparison.Ordinal);

        [Fact]
        public void NoVerdictForThatEngine() =>
            Assert.Contains("**Verdict: NOBODY BEATS ALPHA**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    // AC12 — mismatched gauntlet
    public sealed class ScenarioMismatchedGauntlet : IDisposable
    {
        // Given two runs with different gauntlet hashes
        // When  `score` merges them
        private readonly ScoreScenario scenario = new();
        private readonly int exit;

        public ScenarioMismatchedGauntlet()
        {
            scenario.AddRun(EngineTier.Cpu, hash: "hash-cpu").Plain("beta", Dean(15, 5), Wife(15, 5));
            scenario.AddRun(EngineTier.Gpu, "2026-10-22T11:00:00Z", "hash-gpu").Plain("beta", Dean(15, 5), Wife(15, 5));
            exit = scenario.Score();
        }

        [Fact]
        public void ItRefusesNamingBothHashes() =>
            Assert.True(scenario.Stderr.Contains("hash-cpu", StringComparison.Ordinal) && scenario.Stderr.Contains("hash-gpu", StringComparison.Ordinal));

        [Fact]
        public void TheExitIs1() => Assert.Equal(1, exit);

        [Fact]
        public void NothingIsWritten() => Assert.False(File.Exists(scenario.OutPath()));

        public void Dispose() => scenario.Dispose();
    }

    // ---------------------------------------------------------------------
    // EXTRA SAD PATHS (T614)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAMalformedVoteLine : IDisposable
    {
        private readonly ScoreScenario scenario = new();
        private readonly int exit;

        public ScenarioAMalformedVoteLine()
        {
            ScoreRunBuilder run = scenario.AddRun();
            string pair = run.Pair("beta");
            run.Vote(pair, "Dean", "a").RawVote("{ not json").Vote(pair, "Dean", "b");
            exit = scenario.Score();
        }

        [Fact]
        public void ItRefusesTheWholeScoreWithExit1() => Assert.Equal(1, exit);

        [Fact]
        public void ItNamesTheFileAndTheLineNumber() =>
            Assert.Contains("votes.jsonl line 2", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAVoteWithAnUnknownMember : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioAVoteWithAnUnknownMember()
        {
            ScoreRunBuilder run = scenario.AddRun();
            string pair = run.Pair("beta");
            run.RawVote($$"""{"pairId":"{{pair}}","judge":"Dean","pick":"a","at":"2026-10-22T12:00:00Z","extra":1}""");
            scenario.Score();
        }

        [Fact]
        public void ItIsRefusedAtThatLine() => Assert.Contains("votes.jsonl line 1", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAVoteWithADuplicateKey : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioAVoteWithADuplicateKey()
        {
            ScoreRunBuilder run = scenario.AddRun();
            string pair = run.Pair("beta");
            run.RawVote($$"""{"pairId":"{{pair}}","judge":"Dean","pick":"a","pick":"b","at":"2026-10-22T12:00:00Z"}""");
            scenario.Score();
        }

        [Fact]
        public void ItIsRefusedAtThatLine() => Assert.Contains("votes.jsonl line 1", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAVoteForAPairThatIsNotInTheManifest : IDisposable
    {
        private readonly ScoreScenario scenario = new();
        private readonly int exit;

        public ScenarioAVoteForAPairThatIsNotInTheManifest()
        {
            ScoreRunBuilder run = scenario.AddRun();
            run.Pair("beta");
            run.Vote("p999", "Dean", "a");
            exit = scenario.Score();
        }

        [Fact]
        public void ItExits1() => Assert.Equal(1, exit);

        [Fact]
        public void ItNamesThePairAndTheLine() =>
            Assert.Contains("votes.jsonl line 1: pairId 'p999' is not in the manifest", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAWinceForAReelThatIsNotInTheManifest : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioAWinceForAReelThatIsNotInTheManifest()
        {
            ScoreRunBuilder run = scenario.AddRun();
            run.Plain("beta", Dean(1, 0)).Reel("beta", winces: 0).RawWince("""{"reelId":"ghost","count":1,"at":"2026-10-22T13:00:00Z"}""");
            scenario.Score();
        }

        [Fact]
        public void ItIsRefused() =>
            Assert.Contains("winces.jsonl line 2: reelId 'ghost' is not in the manifest", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioARunWithNoManifest : IDisposable
    {
        private readonly ScoreScenario scenario = new();
        private readonly int exit;

        public ScenarioARunWithNoManifest()
        {
            ScoreRunBuilder run = scenario.AddRun();
            run.WriteManifest = false;
            exit = scenario.Score();
        }

        [Fact]
        public void ItExits1() => Assert.Equal(1, exit);

        [Fact]
        public void ItSaysToRunPairFirst() =>
            Assert.Contains("has no manifest.json — run `voiceoff pair` first", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAManifestFromAnotherGauntlet : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioAManifestFromAnotherGauntlet()
        {
            ScoreRunBuilder run = scenario.AddRun(hash: "hash-run");
            run.ManifestHash = "hash-manifest";
            scenario.Score();
        }

        [Fact]
        public void ItNamesBothHashes() =>
            Assert.True(scenario.Stderr.Contains("hash-run", StringComparison.Ordinal) && scenario.Stderr.Contains("hash-manifest", StringComparison.Ordinal));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioTwoRunsOnTheSameTier : IDisposable
    {
        private readonly ScoreScenario scenario = new();
        private readonly int exit;

        public ScenarioTwoRunsOnTheSameTier()
        {
            scenario.AddRun(EngineTier.Cpu, "2026-10-22T10:00:00Z");
            scenario.AddRun(EngineTier.Cpu, "2026-10-22T11:00:00Z");
            exit = scenario.Score();
        }

        [Fact]
        public void ItExits1() => Assert.Equal(1, exit);

        [Fact]
        public void ItSaysOneRunPerTier() => Assert.Contains("one run per tier", scenario.Stderr, StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAWinnerWithNoWinceCount : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioAWinnerWithNoWinceCount()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(15, 5)).Reel("beta", winces: null);
            scenario.Score();
        }

        [Fact]
        public void ItIsOnPaperWithWincesNotCounted() =>
            Assert.Contains("**Verdict: BEATS ALPHA ON PAPER, WINCES: not counted**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAWinnerWithNoReel : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioAWinnerWithNoReel()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(15, 5));
            scenario.Score();
        }

        [Fact]
        public void ItIsOnPaperWithWincesNotCounted() =>
            Assert.Contains("**Verdict: BEATS ALPHA ON PAPER, WINCES: not counted**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioTheLastWinceEntryCounts : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioTheLastWinceEntryCounts()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(15, 5)).Reel("beta", winces: 3)
                .RawWince("""{"reelId":"reel-beta","count":0,"at":"2026-10-22T09:00:00Z"}""");
            scenario.Score();
        }

        [Fact]
        public void TheLastLineInFileOrderWinsNotTheLatestTime() =>
            Assert.Contains("**Verdict: BEATS ALPHA: beta (0.75, n=40, winces=0)**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioTheVerdictUsesTheBaselineSlugFromTheProfile : IDisposable
    {
        private readonly ScoreScenario scenario = new("omega", "beta");

        public ScenarioTheVerdictUsesTheBaselineSlugFromTheProfile()
        {
            scenario.AddRun().Plain("beta", Dean(15, 5), Wife(15, 5)).Reel("beta", winces: 0);
            scenario.Score();
        }

        [Fact]
        public void ItReadsBEATSOMEGA() =>
            Assert.Contains("**Verdict: BEATS OMEGA: beta (0.75, n=40, winces=0)**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioTwoTiersMerge : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioTwoTiersMerge()
        {
            scenario.AddRun(EngineTier.Gpu, "2026-10-22T11:00:00Z").Plain("beta", Dean(10, 10), Wife(10, 10));
            scenario.AddRun(EngineTier.Cpu, "2026-10-22T10:00:00Z").Plain("beta", Dean(15, 5), Wife(15, 5)).Reel("beta", winces: 0);
            scenario.Score();
        }

        [Fact]
        public void EachTierGetsItsOwnVerdictLineCpuFirst() =>
            Assert.Equal(
                ["cpu: BEATS ALPHA: beta (0.75, n=40, winces=0)", "gpu: NOBODY BEATS ALPHA"],
                scenario.Stdout.Split('\n').Take(2));

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioABudgetOutsideTheRange : IDisposable
    {
        private readonly ScoreScenario scenario = new();
        private readonly int exit;

        public ScenarioABudgetOutsideTheRange()
        {
            scenario.AddRun();
            exit = scenario.Score("RESULTS.md", "--budget-seconds", "0");
        }

        [Fact]
        public void ItIsAUsageError() => Assert.Equal(2, exit);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAFractionalBudget : IDisposable
    {
        private readonly ScoreScenario scenario = new();

        public ScenarioAFractionalBudget()
        {
            scenario.AddRun().Plain("beta", Dean(1, 0));
            scenario.Score("RESULTS.md", "--budget-seconds", "45");
        }

        [Fact]
        public void ItPrintsTwoDecimals() =>
            Assert.Contains("Your box: 45 s for a 20 s line ⇒ rtf 2.25", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioTwoEnginesBeatTheBaseline : IDisposable
    {
        private readonly ScoreScenario scenario = new("alpha", "beta", "gamma", "delta");

        public ScenarioTwoEnginesBeatTheBaseline()
        {
            scenario.AddRun()
                .Plain("beta", Dean(15, 5), Wife(15, 5))
                .Plain("gamma", Dean(30, 10), Wife(30, 10))
                .Plain("delta", Dean(18, 2), Wife(16, 4))
                .Reel("delta", winces: 0)
                .Reel("gamma", winces: 4);
            scenario.Score();
        }

        [Fact]
        public void TheHigherWinRateIsTheCandidate() =>
            Assert.Contains("**Verdict: BEATS ALPHA: delta (0.85, n=40, winces=0)**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioTwoEnginesTieOnWinRate : IDisposable
    {
        private readonly ScoreScenario scenario = new("alpha", "beta", "gamma");

        public ScenarioTwoEnginesTieOnWinRate()
        {
            scenario.AddRun()
                .Plain("beta", Dean(15, 5), Wife(15, 5))
                .Plain("gamma", Dean(30, 10), Wife(30, 10))
                .Reel("gamma", winces: 0);
            scenario.Score();
        }

        [Fact]
        public void TheLargerNWins() =>
            Assert.Contains("**Verdict: BEATS ALPHA: gamma (0.75, n=80, winces=0)**", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAnEngineThatFailedItsGate : IDisposable
    {
        private readonly ScoreScenario scenario = new("alpha", "beta", "gamma");

        public ScenarioAnEngineThatFailedItsGate()
        {
            scenario.AddRun().Plain("beta", Dean(1, 0)).FailedEngine("gamma", "image did not start");
            scenario.Score();
        }

        [Fact]
        public void ItsSpeedCellSaysNotRendered() =>
            Assert.Contains("| gamma | not rendered | — | — | — | MIT |", scenario.Results(), StringComparison.Ordinal);

        [Fact]
        public void ItsWinRateRowStillSaysNoPairs() =>
            Assert.Contains(scenario.Results().Split('\n'), line => line.StartsWith("| gamma |", StringComparison.Ordinal) && line.EndsWith("| no pairs |", StringComparison.Ordinal));

        [Fact]
        public void ItsContainerGateIsMarkedFailed() =>
            Assert.Contains("| gamma | ❌ image did not start | — not run |", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }

    public sealed class ScenarioAnEngineThatFailedVoiceSelect : IDisposable
    {
        private readonly ScoreScenario scenario = new("alpha", "beta", "gamma");

        public ScenarioAnEngineThatFailedVoiceSelect()
        {
            scenario.AddRun().Plain("beta", Dean(1, 0)).FailedVoiceSelect("gamma", "no such voice");
            scenario.Score();
        }

        [Fact]
        public void ItsSpeedCellIsTheMedianRtfNotNotRendered() =>
            Assert.Contains("| gamma | 0.15 | 0.15 | 900 | — | MIT |", scenario.Results(), StringComparison.Ordinal);

        public void Dispose() => scenario.Dispose();
    }
}
