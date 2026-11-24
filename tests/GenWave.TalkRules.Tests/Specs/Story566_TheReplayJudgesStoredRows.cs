// STORY-566 — Every attempt is recorded and can be replayed (SPEC F220.20, F220.26, F220.30 · PLAN T791)
//
// Replay: re-judging stored snapshots with today's rules (SPEC F220.20, F220.26, F220.30). Fixture-level spec over fixture rules.

using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;
using static GenWave.TalkRules.Tests.Fixtures.CheckEntries;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureTheReplayJudgesStoredRows
{
    private const string Flagged = "As I said before, hello.";
    private const string Clean = "Hello, folks.";
    private static readonly DateTimeOffset Since = WrittenAt.AddDays(-1);

    private static async Task<(TalkReplayReport Report, PagedCheckStore Store)> ReplayAsync(
        IReadOnlyList<TalkCheckEntry> entries, bool compare = false, TalkKinds kinds = TalkKinds.DjLine | TalkKinds.Chat | TalkKinds.Ad,
        ITalkRule? extraRule = null)
    {
        var store = new PagedCheckStore(entries);
        var replay = new TalkRuleReplay(
            [new RepeatsFixtureRule(), new AnyKindRepeatsFixtureRule(), .. extraRule is null ? Array.Empty<ITalkRule>() : [extraRule]],
            store,
            new CapturingLogger<TalkRuleReplay>());
        return (await replay.ReplayAsync(new TalkReplayRequest(kinds, Since, compare), CancellationToken.None), store);
    }

    // Without compare: stored verdicts against today's
    public sealed class ScenarioStoredVerdictsAreReplayed : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);
        private PagedCheckStore? store;

        public async Task InitializeAsync() =>
            (report, store) = await ReplayAsync(
            [
                // Stored as flagged, still flagged: no change.
                Entry(1, TalkSnapshots.DjLine(Flagged), null, ("fixture-repeats", Repeated), ("fixture-any-repeats", Repeated)),
                // Stored clean by an older rule, flagged today: a change. The off rule's verdict is still re-judged.
                Entry(2, TalkSnapshots.DjLine(Flagged), null, ("fixture-repeats", null), ("fixture-any-repeats", Repeated)),
                // No stored verdict for the rule at all counts as stored null.
                Entry(3, TalkSnapshots.DjLine(Flagged), null, ("fixture-any-repeats", Repeated)),
            ]);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void EachRuleCountsTheRowsItCovered() =>
            Assert.Equal(3, report.Counts.Single(c => c.RuleId == "fixture-repeats").Checked);

        [Fact]
        public void EachRuleCountsTheRowsItFlagged() =>
            Assert.Equal(3, report.Counts.Single(c => c.RuleId == "fixture-repeats").Flagged);

        [Fact]
        public void TheCountsAreSortedByRuleId() =>
            Assert.Equal(["fixture-any-repeats", "fixture-repeats"], report.Counts.Select(c => c.RuleId));

        [Fact]
        public void OnlyTheRowsWhoseVerdictDiffersAreListed() =>
            Assert.Equal([(2L, "fixture-repeats"), (3L, "fixture-repeats")], report.Changes.Select(c => (c.CheckId, c.RuleId)));

        [Fact]
        public void AChangeCarriesTheStoredAndTheReplayedFlag() =>
            Assert.Equal((null, Repeated), (report.Changes[0].Stored, report.Changes[0].Replayed));

        [Fact]
        public void NothingIsWritten() => Assert.Equal(0, store?.Appends);

        [Fact]
        public void TheLogIsReadInBoundedPages() => Assert.Equal((0, ITalkCheckStore.MaxPageSize), (store?.UnboundedReads, store?.LargestLimitAsked));

        [Fact]
        public void NoTrimSavedLinesAreCountedWithoutCompare() => Assert.Equal(0, report.TrimSavedLines);
    }

    // F220.30 — a row whose stored verdicts are what today's rules say
    public sealed class ScenarioRulesAreUnchanged : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() =>
            (report, _) = await ReplayAsync(
            [
                Entry(1, TalkSnapshots.DjLine(Flagged), null, ("fixture-repeats", Repeated), ("fixture-any-repeats", Repeated)),
                Entry(2, TalkSnapshots.DjLine(Clean), null, ("fixture-repeats", null), ("fixture-any-repeats", null)),
            ]);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NoVerdictChanges() => Assert.Empty(report.Changes);
    }

    // F220.20 — legacy against the rules
    public sealed class ScenarioLegacyIsComparedWithTheRules : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() =>
            (report, _) = await ReplayAsync(
            [
                Entry(1, TalkSnapshots.DjLine(Flagged), TalkLegacyOutcome.Passed, ("fixture-repeats", null)), // legacy passed, rules flag: differs
                Entry(2, TalkSnapshots.DjLine(Clean), TalkLegacyOutcome.ReAsked), // legacy rejected, no rule flags: differs under "none"
                Entry(3, TalkSnapshots.DjLine(Flagged), TalkLegacyOutcome.Template), // both reject: agree
                Entry(4, TalkSnapshots.DjLine(Clean), TalkLegacyOutcome.Passed), // both pass: agree
                Entry(5, TalkSnapshots.DjLine(Flagged), TalkLegacyOutcome.Trimmed), // a trim saved a line the rules flag
                Entry(6, TalkSnapshots.DjLine(Flagged), null), // no legacy outcome: not a shadow row, skipped
                Entry(7, Ad(Flagged), TalkLegacyOutcome.Trimmed), // an ad's trim is the phone rewrite, not a saved line
            ],
            compare: true);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void EachDifferingLineIsListedUnderItsRule() =>
            Assert.Equal(
                [(1L, "fixture-any-repeats"), (1L, "fixture-repeats"), (2L, TalkRuleReplay.NoRuleFlagged)],
                report.Changes.Select(c => (c.CheckId, c.RuleId)));

        [Fact]
        public void ALineLegacyRejectedAndNoRuleFlaggedIsListedUnderNone() =>
            Assert.Equal((2L, TalkLegacyOutcome.ReAsked), report.Changes.Where(c => c.RuleId == TalkRuleReplay.NoRuleFlagged).Select(c => (c.CheckId, c.LegacyOutcome)).Single());

        [Fact]
        public void LinesBothAgreeOnAreNotListed() =>
            Assert.DoesNotContain(report.Changes, c => c.CheckId is 3 or 4);

        [Fact]
        public void AChangeCarriesTheLegacyOutcome() =>
            Assert.Equal(TalkLegacyOutcome.Passed, report.Changes.First(c => c.CheckId == 1).LegacyOutcome);

        [Fact]
        public void ARowWithNoLegacyOutcomeIsSkipped() =>
            Assert.DoesNotContain(report.Changes, c => c.CheckId == 6);

        [Fact]
        public void OnlyALineATrimSavedIsCounted() => Assert.Equal(1, report.TrimSavedLines);
    }

    // Only the asked-for kinds are replayed
    public sealed class ScenarioOneKindIsAsked : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() =>
            (report, _) = await ReplayAsync(
                [Entry(1, TalkSnapshots.DjLine(Flagged)), Entry(2, TalkSnapshots.Chat(Flagged))], kinds: TalkKinds.Chat);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void OnlyThatKindIsChecked() => Assert.Equal(1, report.Counts.Single(c => c.RuleId == "fixture-any-repeats").Checked);

        [Fact]
        public void RulesThatCannotCoverItAreNotListed() =>
            Assert.DoesNotContain(report.Counts, c => c.RuleId == "fixture-repeats");
    }

    // Sad path — a row nothing was judged for (F220.30: replay reproduces what was stored)
    public sealed class ScenarioARowHadNothingJudged : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() =>
            (report, _) = await ReplayAsync([Unjudged(1, Ad(Flagged))]);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsNotReportedAsAChange() => Assert.Empty(report.Changes);

        [Fact]
        public void ItIsNotChecked() => Assert.All(report.Counts, c => Assert.Equal(0, c.Checked));
    }

    // Sad path — the same row in a shadow report
    public sealed class ScenarioAnUnjudgedRowIsComparedWithLegacy : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() =>
            (report, _) = await ReplayAsync([Unjudged(1, Ad(Clean), TalkLegacyOutcome.Dropped)], compare: true);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItIsNotListedUnderNone() => Assert.Empty(report.Changes);

        [Fact]
        public void ItIsNotChecked() => Assert.All(report.Counts, c => Assert.Equal(0, c.Checked));
    }

    // Sad path — more rows than one page
    public sealed class ScenarioTheWindowHoldsManyPages : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() =>
            (report, _) = await ReplayAsync(
                Enumerable.Range(1, 2500).Select(id => Entry(id, TalkSnapshots.DjLine(Clean), null, ("fixture-any-repeats", null))).ToList());

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void EveryRowIsChecked() => Assert.Equal(2500, report.Counts.Single(c => c.RuleId == "fixture-any-repeats").Checked);
    }

    // Sad path — a judge that throws
    public sealed class ScenarioAJudgeThrows : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() =>
            (report, _) = await ReplayAsync([Entry(1, TalkSnapshots.DjLine(Clean))], extraRule: new ThrowingFixtureRule());

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItCountsAsAFlag() => Assert.Equal(1, report.Counts.Single(c => c.RuleId == "fixture-throws").Flagged);

        [Fact]
        public void TheOtherRulesAreStillReplayed() => Assert.Equal(1, report.Counts.Single(c => c.RuleId == "fixture-repeats").Checked);
    }

    // Sad path — nothing stored
    public sealed class ScenarioTheLogIsEmpty : IAsyncLifetime
    {
        private TalkReplayReport report = new(TalkList<TalkRuleReplayCount>.Empty, TalkList<TalkVerdictChange>.Empty, 0);

        public async Task InitializeAsync() => (report, _) = await ReplayAsync([], compare: true);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void NothingIsChanged() => Assert.Empty(report.Changes);

        [Fact]
        public void EveryRuleHasAZeroCount() => Assert.All(report.Counts, c => Assert.Equal(0, c.Checked));
    }
}
