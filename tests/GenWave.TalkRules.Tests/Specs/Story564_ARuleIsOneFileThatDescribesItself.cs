// STORY-564 — A rule is one file that describes itself (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Facts run over fixture rules in this test assembly, scanned the way the
// host scans GenWave.TalkRules, so they stay meaningful before and after real rules ship.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.TalkRules.Tests.Fixtures;
using GenWave.Tts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureARuleIsOneFileThatDescribesItself
{
    private static TalkRuleBook ScannedRuleBook()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The collaborators a shipped shaping rule takes (the host registers the real ones).
        services.AddSingleton<IQuirkSelector>(new RecordingQuirkSelector(QuirkSelection.None));
        services.AddSingleton<ITaglineLedger, TaglineLedger>();
        services.AddSingleton<IPersonaCardByIdSource>(new OneCardSource(null));
        services.AddSingleton<IPersonaStore>(new RosterPersonaStore());
        services.AddSingleton<ILlmContextWindow>(new FixedContextWindow(MemoryBudget.Off));
        services.AddSingleton<IMemoryRecallSource>(new RecordingMemoryRecall());
        services.AddSingleton<IBreakCallbacks>(new BreakCallbacks(
            new CallbackPlanner(new CallbackStoreOfOneMemory(null)), new FixedCallbackRate(0), new RosterPersonaStore(),
            NullLogger<BreakCallbacks>.Instance));
        services.AddTalkRules(typeof(TalkRuleBook).Assembly, typeof(FactsFixtureRule).Assembly);
        return services.BuildServiceProvider().GetRequiredService<TalkRuleBook>();
    }

    // AC2 — a rule class added as one file
    public sealed class ScenarioARuleAddedAsOneFile : IAsyncLifetime
    {
        private readonly TalkRuleBook book = ScannedRuleBook();
        private TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync() =>
            verdicts = await book.JudgeAsync(TalkSnapshots.DjLine("This is an invented fact."), CancellationToken.None);

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void TheRuleBookListsIt() =>
            Assert.Contains(book.Rules, rule => rule.Id == "fixture-facts");

        [Fact]
        public void TheRuleBookRunsItsJudge() =>
            Assert.NotNull(Assert.Single(verdicts, v => v.RuleId == "fixture-facts").Flag);
    }

    // AC1 — every rule, through the rule book listing
    public sealed class ScenarioEveryScannedRuleIsListed
    {
        private readonly IReadOnlyList<ITalkRule> listed;

        public ScenarioEveryScannedRuleIsListed()
        {
            listed = ScannedRuleBook().Rules;
            if (listed.Count == 0)
            {
                throw new InvalidOperationException("The scan found no rules, so the facts below would be vacuous.");
            }
        }

        [Fact]
        public void EachRuleHasAName() =>
            Assert.All(listed, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Describe().Name)));

        [Fact]
        public void EachRuleSaysWhatItDoes() =>
            Assert.All(listed, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Describe().WhatItDoes)));

        [Fact]
        public void EachRuleSaysWhy() =>
            Assert.All(listed, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Describe().Why)));

        [Fact]
        public void EachRuleGivesAnExample() =>
            Assert.All(listed, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Describe().Example)));

        [Fact]
        public void EachRuleCanCoverSomeTalk() =>
            Assert.All(listed, rule => Assert.NotEqual(TalkKinds.None, rule.CanCover));

        [Fact]
        public void EachRuleIsOnByDefaultOnlyWhereItCanCover() =>
            Assert.All(listed, rule => Assert.Equal(TalkKinds.None, rule.OnByDefault & ~rule.CanCover));
    }
}
