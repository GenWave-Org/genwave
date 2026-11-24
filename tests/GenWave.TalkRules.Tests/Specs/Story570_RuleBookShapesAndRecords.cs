// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// Rule book: shapers run before writing, recorders after accepted talk (SPEC F220.9, F220.10, F220.15).
// Fixture-level spec over fixture rules.

using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureRuleBookShapesAndRecords
{
    private static TalkRuleBook BookOf(InMemoryTalkRuleSwitches switches, params ITalkRule[] rules) =>
        new(rules, switches, new CapturingLogger<TalkRuleBook>());

    // AC6 — two switched-on shapers (registered out of id order)
    public sealed class ScenarioShapersAddNotes : IAsyncLifetime
    {
        private readonly NoteShaperFixtureRuleB second = new();
        private readonly NoteShaperFixtureRuleA first = new();
        private TalkShaping shaping = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

        public async Task InitializeAsync() =>
            shaping = await BookOf(new InMemoryTalkRuleSwitches(), second, first)
                .ShapeAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void EachNoteIsUnderForThisBreak() =>
            Assert.Equal("For this break:\nKeep it short.\nMention the weather.", shaping.ForThisBreak());

        [Fact]
        public void TheNotesAreSortedByRuleId() =>
            Assert.Equal(["Keep it short.", "Mention the weather."], shaping.PromptNotes);

        [Fact]
        public void EachMemoIsOnTheSnapshot()
        {
            var snapshot = TalkSnapshots.DjLine("Hello.", shaping.Memos);
            Assert.Equal(
                ("fixture-note-a", "fixture-note-b"),
                (snapshot.MemoOf(first)?.GetProperty("from").GetString(), snapshot.MemoOf(second)?.GetProperty("from").GetString()));
        }
    }

    // AC9 — accepted talk only
    public sealed class ScenarioRecordingRules : IAsyncLifetime
    {
        private readonly RecorderFixtureRule recorder = new();

        public async Task InitializeAsync()
        {
            var book = BookOf(new InMemoryTalkRuleSwitches(), recorder);
            // The rejected attempt is judged and never accepted; only the accepted one is passed on.
            await book.JudgeAsync(TalkSnapshots.DjLine("Rejected attempt."), CancellationToken.None);
            await book.AcceptedAsync(TalkSnapshots.DjLine("Accepted attempt."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void AcceptedTalkReachesTheRecordingRule() => Assert.Contains("Accepted attempt.", recorder.Recorded);

        [Fact]
        public void RejectedTalkDoesNot() => Assert.DoesNotContain("Rejected attempt.", recorder.Recorded);
    }

    // Sad path
    // AC11 — a switched-off shaper
    public sealed class ScenarioAShaperIsSwitchedOff : IAsyncLifetime
    {
        private readonly NoteShaperFixtureRuleB off = new();
        private readonly NoteShaperFixtureRuleA on = new();
        private readonly RecorderFixtureRule offRecorder = new();
        private TalkShaping shaping = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

        public async Task InitializeAsync()
        {
            var switches = new InMemoryTalkRuleSwitches();
            await switches.SetAsync("fixture-note-b", TalkKinds.DjLine, false, CancellationToken.None);
            await switches.SetAsync("fixture-recorder", TalkKinds.DjLine, false, CancellationToken.None);
            var book = BookOf(switches, off, on, offRecorder);
            shaping = await book.ShapeAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);
            await book.AcceptedAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ThePromptHasNoNoteFromIt() => Assert.Equal("For this break:\nKeep it short.", shaping.ForThisBreak());

        [Fact]
        public void ItIsNotCalled() => Assert.Equal(0, off.Calls);

        [Fact]
        public void ItLeavesNoMemo() =>
            Assert.Null(TalkSnapshots.DjLine("Hello.", shaping.Memos).MemoOf(off));

        [Fact]
        public void ASwitchedOffRecorderKeepsNoState() => Assert.Empty(offRecorder.Recorded);
    }

    // SPEC F220.9 (Dean 11-22, option B) — a shaper that throws
    public sealed class ScenarioAShaperThrows : IAsyncLifetime
    {
        private TalkShaping shaping = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);
        private TalkShaping withoutIt = new(TalkList<string>.Empty, TalkMemos.Empty, TalkList<string>.Empty);

        public async Task InitializeAsync()
        {
            var broken = new ThrowingShaperFixtureRule();
            var good = new NoteShaperFixtureRuleA();
            shaping = await BookOf(new InMemoryTalkRuleSwitches(), broken, good)
                .ShapeAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);
            withoutIt = await BookOf(new InMemoryTalkRuleSwitches(), good)
                .ShapeAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItsFailureIsReported() => Assert.Equal(["fixture-shaper-throws"], shaping.CouldNotShape);

        [Fact]
        public void ThePromptIsAsIfItWereSwitchedOff() =>
            Assert.Equal((withoutIt.PromptNotes, withoutIt.Memos), (shaping.PromptNotes, shaping.Memos));

        [Fact]
        public void TheOtherShapersNoteIsStillThere() => Assert.Equal(["Keep it short."], shaping.PromptNotes);

        [Fact]
        public void NothingIsReportedWhenNothingThrows() => Assert.Empty(withoutIt.CouldNotShape);

        [Fact]
        public async Task TheCallersCancellationStillPropagates()
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            var book = BookOf(new InMemoryTalkRuleSwitches(), new CancellingShaperFixtureRule());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await book.ShapeAsync(TalkSnapshots.DjLine("Hello."), cts.Token));
        }

        [Fact]
        public async Task ARuleCancellingOnItsOwnIsBrokenNotCancelled()
        {
            var book = BookOf(new InMemoryTalkRuleSwitches(), new CancellingShaperFixtureRule());
            var result = await book.ShapeAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);
            Assert.Equal(["fixture-shaper-cancels"], result.CouldNotShape);
        }
    }

    // SPEC F220.9 — a recorder that throws
    public sealed class ScenarioARecorderThrows : IAsyncLifetime
    {
        private readonly RecorderFixtureRule other = new();
        private TalkList<string> failed = TalkList<string>.Empty;

        public async Task InitializeAsync() =>
            failed = await BookOf(new InMemoryTalkRuleSwitches(), new ThrowingRecorderFixtureRule(), other)
                .AcceptedAsync(TalkSnapshots.DjLine("Accepted attempt."), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItsFailureIsReported() => Assert.Equal(["fixture-recorder-throws"], failed);

        [Fact]
        public void TheOtherRecorderIsStillCalled() => Assert.Contains("Accepted attempt.", other.Recorded);

        [Fact]
        public async Task TheCallersCancellationStillPropagates()
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            var book = BookOf(new InMemoryTalkRuleSwitches(), new CancellingRecorderFixtureRule());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await book.AcceptedAsync(TalkSnapshots.DjLine("Hello."), cts.Token));
        }
    }
}
