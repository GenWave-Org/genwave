// STORY-565 — Rules stand alone (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Rule-book facts run over fixture rules in this test assembly.

using System.Text.Json;
using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;
using GenWave.TestSupport.Fakes;
using Microsoft.Extensions.Logging;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureRulesStandAlone
{
    private static TalkRuleBook BookOf(CapturingLogger<TalkRuleBook> logger, params ITalkRule[] rules) =>
        new(rules, new InMemoryTalkRuleSwitches(), logger);

    // AC1 — a reply that breaks the facts rule and the repeats rule
    public sealed class ScenarioAReplyBreaksTwoRules : IAsyncLifetime
    {
        private TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync()
        {
            var book = BookOf(new CapturingLogger<TalkRuleBook>(), new FactsFixtureRule(), new RepeatsFixtureRule());
            verdicts = await book.JudgeAsync(
                TalkSnapshots.DjLine("An invented fact, as I said before."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheFactsRuleFlagsIt() =>
            Assert.NotNull(Assert.Single(verdicts, v => v.RuleId == "fixture-facts").Flag);

        [Fact]
        public void TheRepeatsRuleFlagsIt() =>
            Assert.NotNull(Assert.Single(verdicts, v => v.RuleId == "fixture-repeats").Flag);
    }

    // AC5 — a memo written by rule A
    public sealed class ScenarioAnotherRulesMemo : IAsyncLifetime
    {
        private readonly MemoProbeRuleA ruleA = new();
        private readonly MemoProbeRuleB ruleB = new();

        public async Task InitializeAsync()
        {
            var memos = TalkMemos.Empty.With("fixture-memo-a", JsonSerializer.SerializeToElement(new { picked = 3 }));
            var book = BookOf(new CapturingLogger<TalkRuleBook>(), ruleA, ruleB);
            await book.JudgeAsync(TalkSnapshots.DjLine("Hello.", memos), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void RuleBGetsNothing() => Assert.Null(ruleB.Seen);

        [Fact]
        public void RuleAGetsItsOwnMemo() => Assert.Equal(3, ruleA.Seen?.GetProperty("picked").GetInt32());
    }

    // Sad path
    // AC8 — a rule that throws
    public sealed class ScenarioARuleThrows : IAsyncLifetime
    {
        private readonly CapturingLogger<TalkRuleBook> logger = new();
        private TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync()
        {
            var book = BookOf(logger, new ThrowingFixtureRule(), new FactsFixtureRule(), new RepeatsFixtureRule());
            verdicts = await book.JudgeAsync(
                TalkSnapshots.DjLine("An invented fact."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItsVerdictIsAFlagCouldNotCheck() =>
            Assert.Equal("Could not check", Assert.Single(verdicts, v => v.RuleId == "fixture-throws").Flag?.Reason);

        [Fact]
        public void TheOtherRulesVerdictsArePresent() =>
            Assert.Equal(["fixture-facts", "fixture-repeats", "fixture-throws"], verdicts.Select(v => v.RuleId));

        [Fact]
        public void AWarningNamesTheRule() =>
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("fixture-throws"));
    }

    // Sad path: a broken rule that throws a cancellation
    public sealed class ScenarioARuleThrowsACancellation : IAsyncLifetime
    {
        private TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync()
        {
            var book = BookOf(new CapturingLogger<TalkRuleBook>(), new CancellingFixtureRule());
            verdicts = await book.JudgeAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItsVerdictIsAFlagCouldNotCheck() =>
            Assert.Equal("Could not check", Assert.Single(verdicts).Flag?.Reason);
    }
}
