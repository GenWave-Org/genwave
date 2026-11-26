// Rule book — a failed switch read falls back to every rule's own default (Dean 11-25, PLAN T800). Fixture-level spec over fixture rules.

using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureASwitchReadFails
{
    private sealed class ThrowingSwitches(Exception failure) : ITalkRuleSwitches
    {
        public ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(TalkKinds kind, CancellationToken ct) => throw failure;

        public ValueTask SetAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct) => throw failure;
    }

    private static TalkRuleBook BookOver(Exception failure, CapturingLogger<TalkRuleBook> logger, params ITalkRule[] rules) =>
        new(rules, new ThrowingSwitches(failure), logger);

    public sealed class ScenarioTheStoreIsDownWhenJudging : IAsyncLifetime
    {
        private readonly CapturingLogger<TalkRuleBook> logger = new();
        private TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync() =>
            verdicts = await BookOver(new InvalidOperationException("db down"), logger, new FactsFixtureRule(), new OffByDefaultFixtureRule())
                .JudgeAsync(TalkSnapshots.DjLine("An invented fact."), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ARuleOnByDefaultIsSwitchedOn() =>
            Assert.True(Assert.Single(verdicts, v => v.RuleId == "fixture-facts").SwitchedOn);

        [Fact]
        public void ARuleOffByDefaultIsSwitchedOff() =>
            Assert.False(Assert.Single(verdicts, v => v.RuleId == "fixture-off-by-default").SwitchedOn);

        [Fact]
        public void TheVerdictIsAFlagOfTheRuleNotCouldNotCheck() =>
            Assert.NotEqual(TalkRuleReasons.CouldNotCheck, Assert.Single(verdicts, v => v.RuleId == "fixture-facts").Flag?.Reason);

        [Fact]
        public void OneWarningIsLogged() => Assert.Single(logger.Warnings);
    }

    public sealed class ScenarioTheStoreIsDownWhenShaping : IAsyncLifetime
    {
        private readonly NoteShaperFixtureRuleA shaper = new();
        private TalkShaping shaping = null!;

        public async Task InitializeAsync() =>
            shaping = await BookOver(new InvalidOperationException("db down"), new CapturingLogger<TalkRuleBook>(), shaper)
                .ShapeAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheShaperOnByDefaultStillShapes() => Assert.Equal(["Keep it short."], shaping.PromptNotes);

        [Fact]
        public void NothingIsReportedAsBroken() => Assert.Empty(shaping.CouldNotShape);
    }

    public sealed class ScenarioTheStoreIsDownWhenRecording : IAsyncLifetime
    {
        private readonly RecorderFixtureRule recorder = new();

        public async Task InitializeAsync() =>
            await BookOver(new InvalidOperationException("db down"), new CapturingLogger<TalkRuleBook>(), recorder)
                .AcceptedAsync(TalkSnapshots.DjLine("Hello."), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRecorderOnByDefaultIsStillTold() => Assert.Equal(["Hello."], recorder.Recorded);
    }

    /// <summary>Cancels the caller's token mid-read, then throws as a cancelled read does; ShapeAsync has no pre-check, so only the filter decides.</summary>
    private sealed class CancellingSwitches(CancellationTokenSource source) : ITalkRuleSwitches
    {
        public async ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(TalkKinds kind, CancellationToken ct)
        {
            await source.CancelAsync();
            throw new OperationCanceledException(ct);
        }

        public ValueTask SetAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct) => throw new NotSupportedException();
    }

    public sealed class ScenarioTheCallerCancelsDuringTheRead : IAsyncLifetime
    {
        private readonly CapturingLogger<TalkRuleBook> logger = new();
        private Exception? escaped;

        public async Task InitializeAsync()
        {
            using var source = new CancellationTokenSource();
            var book = new TalkRuleBook([new NoteShaperFixtureRuleA()], new CancellingSwitches(source), logger);
            try
            {
                await book.ShapeAsync(TalkSnapshots.DjLine("Hello."), source.Token);
            }
            catch (Exception ex)
            {
                escaped = ex;
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void CancellationStillPropagates() => Assert.IsAssignableFrom<OperationCanceledException>(escaped);

        [Fact]
        public void NoWarningIsLogged() => Assert.Empty(logger.Warnings);
    }
}
