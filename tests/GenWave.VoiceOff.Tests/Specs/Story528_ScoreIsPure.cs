// STORY-528 — Score is pure (gh-#614 · SPEC F217 · PLAN T666)
//
// BDD specification — xUnit. Active since T666.

using System.Text.Json.Nodes;
using GenWave.VoiceOff.Tests.Support;

namespace GenWave.VoiceOff.Tests.Specs;

public static class FeatureScoreIsPure
{
    private static readonly string[] JudgedOrder = [ScoreScenario.Three, ScoreScenario.One, ScoreScenario.Two];

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC1 — the ranking follows the judgement
    public sealed class ScenarioTheRankingFollowsTheJudgement : IDisposable
    {
        // Given a judgement ordering three cards
        // When  score runs
        private readonly ScoreScenario score = new(JudgedOrder);

        [Fact]
        public void TheRankingTableListsThemInThatOrder() =>
            Assert.Equal(JudgedOrder, score.Table("Ranking").Select(r => r[1]));

        [Fact]
        public void TheFirstRowIsRank1() => Assert.Equal("1", score.Table("Ranking")[0][0]);

        [Fact]
        public void EachRowCarriesCardEngineHostWincesStarsAndWhy() =>
            Assert.Equal(["2", "a-one", "alpha", "thor-cpu", "2", "4", "Because a-one."], score.Table("Ranking")[1]);

        [Fact]
        public void AnUnratedCardPrintsADashForStars() => Assert.Equal("—", score.Table("Ranking")[2][5]);

        public void Dispose() => score.Dispose();
    }

    // AC2 — notes are kept
    public sealed class ScenarioNotesAreKept : IDisposable
    {
        // Given the judgement
        // When  RESULTS.md is read
        private readonly ScoreScenario score = new(JudgedOrder);

        [Fact]
        public void ThereIsANotesSectionPerCard() =>
            Assert.Equal(JudgedOrder, score.Section("Notes").Where(l => l.StartsWith("### ", StringComparison.Ordinal)).Select(l => l[4..]));

        // The note sits in a code fence one backtick longer than the longest run inside it, so nothing in it can close the fence or become markup.
        [Fact]
        public void EachNoteIsVerbatim() =>
            Assert.Contains("````\n" + ScoreScenario.AwkwardNote + "\n````\n", score.Results, StringComparison.Ordinal);

        public void Dispose() => score.Dispose();
    }

    // AC3 — the tray is printed
    public sealed class ScenarioTheTrayIsPrinted : IDisposable
    {
        // Given a run with a failed card
        // When  RESULTS.md is read
        private readonly ScoreScenario score = new(JudgedOrder);

        [Fact]
        public void TheDidNotRenderSectionNamesTheCardAndItsReason() =>
            Assert.Equal([ScoreScenario.Four, ScoreScenario.FailureReason], score.Table("Did not render").Single());

        public void Dispose() => score.Dispose();
    }

    // AC4 — the facts are printed
    public sealed class ScenarioTheFactsArePrinted : IDisposable
    {
        // Given the cards
        // When  RESULTS.md is read
        private readonly ScoreScenario score = new(JudgedOrder);

        [Fact]
        public void AFactsTableListsVoicesCloningOwnVoiceAndCannotPerCard() =>
            Assert.Equal(["a-one", "cp-1", "54", "no", "yes", "breath, sigh"], score.Table("Facts").First()[..6]);

        [Fact]
        public void EveryCardOfTheRunHasARow() =>
            Assert.Equal([ScoreScenario.One, ScoreScenario.Two, ScoreScenario.Three, ScoreScenario.Four], score.Table("Facts").Select(r => r[0]));

        // renders.jsonl is used here and nowhere else: the seconds spent on a card, 1.5 s + 2.8 s.
        [Fact]
        public void TheRenderTimeComesFromTheRenderLog() => Assert.Equal("4.3 s", score.Table("Facts")[0][8]);

        public void Dispose() => score.Dispose();
    }

    // AC5 — the header names the run
    public sealed class ScenarioTheHeaderNamesTheRun : IDisposable
    {
        // Given run.json
        // When  RESULTS.md is read
        private readonly ScoreScenario score = new(JudgedOrder);

        [Fact]
        public void ItCarriesTheRunId() => Assert.Contains("- Run: scored\n", score.Results, StringComparison.Ordinal);

        // The run started at 23:30 on the 27th at UTC-5, which is the 28th in UTC.
        [Fact]
        public void ItCarriesTheDateFromRunJson() => Assert.Contains("- Date: 2026-10-28\n", score.Results, StringComparison.Ordinal);

        [Fact]
        public void ItCarriesTheScriptHash() => Assert.Contains($"- Script hash: {score.ScriptHash}\n", score.Results, StringComparison.Ordinal);

        [Fact]
        public void ItCarriesTheHosts() => Assert.Contains("- Hosts: runes-gpu, thor-cpu, thor-gpu\n", score.Results, StringComparison.Ordinal);

        public void Dispose() => score.Dispose();
    }

    // AC6 — unjudged cards sort last
    public sealed class ScenarioUnjudgedCardsSortLast : IDisposable
    {
        // Given an order naming one of three cards
        // When  score runs
        private readonly ScoreScenario score = new([ScoreScenario.Two]);

        [Fact]
        public void TheUnnamedCardsFollowInIdOrder() =>
            Assert.Equal([ScoreScenario.Two, ScoreScenario.One, ScoreScenario.Three], score.Table("Ranking").Select(r => r[1]));

        public void Dispose() => score.Dispose();
    }

    // AC7 — the output is deterministic
    public sealed class ScenarioTheOutputIsDeterministic : IDisposable
    {
        // Given the same inputs
        // When  score runs twice
        private readonly ScoreScenario score = new(JudgedOrder);
        private readonly string secondPath;

        public ScenarioTheOutputIsDeterministic()
        {
            secondPath = score.ResultsPath + ".again";
            score.Score(secondPath);
        }

        [Fact]
        public void TheTwoFilesAreByteIdentical() => Assert.Equal(File.ReadAllBytes(score.ResultsPath), File.ReadAllBytes(secondPath));

        [Fact]
        public void TheFileHasNoCarriageReturns() =>
            Assert.DoesNotContain('\r', score.Results);

        public void Dispose() => score.Dispose();
    }

    // AC10 — the judge's remarks are printed under the ranking heading
    public sealed class ScenarioRemarksArePrinted : IDisposable
    {
        private const string Remarks = "Cards 13 to 23 were not ranked | their order means nothing.";

        // Given a judgement with remarks
        // When  score runs
        private readonly ScoreScenario score = new(JudgedOrder, remarks: JsonValue.Create(Remarks));

        [Fact]
        public void TheyAreTheFirstThingUnderTheRankingHeading() =>
            Assert.Equal("Cards 13 to 23 were not ranked \\| their order means nothing.", score.Section("Ranking").First(l => l.Length > 0));

        [Fact]
        public void TheTableFollowsThem() =>
            Assert.StartsWith("| Rank |", score.Section("Ranking").First(l => l.StartsWith('|')), StringComparison.Ordinal);

        [Fact]
        public void TheyAreNotRepeatedInTheNotes() => Assert.DoesNotContain("not ranked", string.Join('\n', score.Section("Notes")), StringComparison.Ordinal);

        public void Dispose() => score.Dispose();
    }

    // AC11 — no remarks, no extra lines
    public sealed class ScenarioNoRemarksAddNothing : IDisposable
    {
        // Given a judgement without remarks, and one whose remarks are blank
        // When  score runs
        private readonly ScoreScenario absent = new(JudgedOrder);
        private readonly ScoreScenario blank = new(JudgedOrder, remarks: JsonValue.Create("  \n "));

        [Fact]
        public void TheTableFollowsTheHeadingAfterOneBlankLine() => Assert.StartsWith("| Rank |", absent.Section("Ranking")[1], StringComparison.Ordinal);

        [Fact]
        public void BlankRemarksPrintNothing() => Assert.Equal(absent.Results, blank.Results);

        public void Dispose()
        {
            absent.Dispose();
            blank.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    // AC8 — a script hash mismatch is refused
    public sealed class ScenarioAScriptHashMismatchIsRefused : IDisposable
    {
        // Given run.json with a different script hash
        // When  score runs
        private readonly ScoreScenario score = new(JudgedOrder, scriptHashInRun: "stale-hash");

        [Fact]
        public void ItExitsNonZeroNamingBothHashes() =>
            Assert.Equal($"The run was rendered from script stale-hash but the script is now {score.ScriptHash}.", score.Stderr.Trim());

        [Fact]
        public void TheExitCodeIs1() => Assert.Equal(1, score.Exit);

        [Fact]
        public void NoResultsFileIsWritten() => Assert.False(File.Exists(score.ResultsPath));

        public void Dispose() => score.Dispose();
    }

    // AC9 — no judgement is refused
    public sealed class ScenarioNoJudgementIsRefused : IDisposable
    {
        // Given a run without judgement.json
        // When  score runs
        private readonly ScoreScenario score = new(null);

        [Fact]
        public void ItExitsNonZeroSayingNothingHasBeenJudgedYet() =>
            Assert.Equal($"Nothing has been judged yet: {score.JudgementPath} does not exist.", score.Stderr.Trim());

        [Fact]
        public void TheExitCodeIs1() => Assert.Equal(1, score.Exit);

        [Fact]
        public void NoResultsFileIsWritten() => Assert.False(File.Exists(score.ResultsPath));

        public void Dispose() => score.Dispose();
    }

    // AC12 — remarks that are not text are refused
    public sealed class ScenarioRemarksThatAreNotTextAreRefused : IDisposable
    {
        // Given a judgement whose remarks is a number
        // When  score runs
        private readonly ScoreScenario score = new(JudgedOrder, remarks: JsonValue.Create(5));

        [Fact]
        public void TheExitCodeIs1() => Assert.Equal(1, score.Exit);

        [Fact]
        public void NoResultsFileIsWritten() => Assert.False(File.Exists(score.ResultsPath));

        public void Dispose() => score.Dispose();
    }
}
