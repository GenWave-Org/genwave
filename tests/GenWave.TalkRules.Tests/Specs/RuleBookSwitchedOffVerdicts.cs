// Rule book — switched-off verdicts are recorded (SPEC F220.7). Fixture-level spec over fixture rules.

using GenWave.Core.TalkRules;
using GenWave.TalkRules.Tests.Fixtures;

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureSwitchedOffVerdictsAreRecorded
{
    private static async Task<TalkList<RuleVerdict>> JudgeAsync(InMemoryTalkRuleSwitches switches)
    {
        var book = new TalkRuleBook(
            [new FactsFixtureRule(), new OffByDefaultFixtureRule()], switches, new CapturingLogger<TalkRuleBook>());
        return await book.JudgeAsync(TalkSnapshots.DjLine("An invented fact."), CancellationToken.None);
    }

    public sealed class ScenarioARuleIsSwitchedOff : IAsyncLifetime
    {
        private TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync()
        {
            var switches = new InMemoryTalkRuleSwitches();
            await switches.SetAsync("fixture-facts", TalkKinds.DjLine, false, CancellationToken.None);
            verdicts = await JudgeAsync(switches);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ItsVerdictSaysSwitchedOff() =>
            Assert.False(Assert.Single(verdicts, v => v.RuleId == "fixture-facts").SwitchedOn);

        [Fact]
        public void ItsFlagIsStillRecorded() =>
            Assert.NotNull(Assert.Single(verdicts, v => v.RuleId == "fixture-facts").Flag);
    }

    public sealed class ScenarioNothingIsStored : IAsyncLifetime
    {
        private TalkList<RuleVerdict> verdicts = TalkList<RuleVerdict>.Empty;

        public async Task InitializeAsync() => verdicts = await JudgeAsync(new InMemoryTalkRuleSwitches());

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void ARuleOffByDefaultForTheKindIsSwitchedOff() =>
            Assert.False(Assert.Single(verdicts, v => v.RuleId == "fixture-off-by-default").SwitchedOn);

        [Fact]
        public void ARuleOnByDefaultForTheKindIsSwitchedOn() =>
            Assert.True(Assert.Single(verdicts, v => v.RuleId == "fixture-facts").SwitchedOn);
    }
}
