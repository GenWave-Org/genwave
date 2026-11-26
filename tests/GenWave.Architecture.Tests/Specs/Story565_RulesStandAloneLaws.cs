// STORY-565 — Rules stand alone (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Laws R1–R4 over the shipped rules plus the good fixture rules, each proven red against a bad fixture.

using GenWave.Architecture.Tests.TalkRules;
using GenWave.Architecture.Tests.TalkRules.Fixtures;
using GenWave.Architecture.Tests.TalkRules.Fixtures.Bad;
using GenWave.Architecture.Tests.TalkRules.Fixtures.Good;
using GenWave.Architecture.Tests.TalkRules.Laws;
using GenWave.Core.TalkRules;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureRulesStandAloneLaws
{
    // AC2 — law R3, every rotation and 20 seeded shuffles
    public sealed class ScenarioRulesRunInAnyOrder
    {
        private readonly IReadOnlyList<IJudgingTalkRule> judges =
            TalkRuleCatalog.Build(TalkRuleCatalog.CheckedTypes).OfType<IJudgingTalkRule>().ToList();

        public ScenarioRulesRunInAnyOrder() => Assert.True(judges.Count >= 2);

        [Fact]
        public void TheVerdictSetsAreEqual() =>
            Assert.Empty(OrderDoesNotMatterLaw.Violations(judges, TalkSnapshotFixtures.All));

        [Fact]
        public void SomeFixtureIsFlagged() =>
            Assert.Contains(JudgeVerdicts.Collect(judges, TalkSnapshotFixtures.All), line => line.Contains(": flag", StringComparison.Ordinal));

        [Fact]
        public void SomeFixtureIsPassed() =>
            Assert.Contains(JudgeVerdicts.Collect(judges, TalkSnapshotFixtures.All), line => line.Contains(": pass", StringComparison.Ordinal));

        [Fact]
        public void ARuleThatRemembersItsFirstCallFailsTheLaw()
        {
            IReadOnlyList<IJudgingTalkRule> withBad = [.. judges, new FirstCallOnlyJudge()];
            Assert.Contains(
                OrderDoesNotMatterLaw.Violations(withBad, TalkSnapshotFixtures.All),
                v => v.Contains("changed the verdicts of", StringComparison.Ordinal) && v.Split(':')[0].Contains("first-call-only", StringComparison.Ordinal));
        }
    }

    // AC3 — law R4, each rule switched off in turn
    public sealed class ScenarioARuleIsSwitchedOff
    {
        [Fact]
        public async Task TheVerdictsEqualAllOnMinusThatRule()
        {
            var violations = await SwitchOffIsHonestLaw.ViolationsAsync(
                _ => TalkRuleCatalog.Build(TalkRuleCatalog.CheckedTypes), TalkSnapshotFixtures.All);
            Assert.Empty(violations);
        }

        [Fact]
        public async Task ARuleThatReadsTheSwitchesFailsTheLaw()
        {
            var violations = await SwitchOffIsHonestLaw.ViolationsAsync(
                switches => [new ShoutingJudge(), new SwitchPeekingJudge(switches)], TalkSnapshotFixtures.All);
            Assert.Contains(violations, violation => violation.Contains("verdicts of switch-peeking", StringComparison.Ordinal));
        }
    }

    // AC4 — a stored snapshot judged twice
    public sealed class ScenarioASnapshotIsJudgedTwice
    {
        private readonly IReadOnlyList<IJudgingTalkRule> judges =
            TalkRuleCatalog.Build(TalkRuleCatalog.CheckedTypes).OfType<IJudgingTalkRule>().ToList();

        private readonly IReadOnlyList<TalkSnapshot> stored =
            TalkSnapshotFixtures.All.Select(s => TalkSnapshotJson.Deserialize(TalkSnapshotJson.Serialize(s))).ToList();

        public ScenarioASnapshotIsJudgedTwice() => Assert.True(judges.Count >= 2);

        [Fact]
        public void TheVerdictsAreEqual() =>
            Assert.Equal(JudgeVerdicts.Collect(judges, stored), JudgeVerdicts.Collect(judges, stored));
    }

    // Sad path
    // AC6 — law R1, a rule that takes another rule, the rule book or the switches
    public sealed class ScenarioARuleKnowsAnother
    {
        [Fact]
        public void TheShippedAndGoodRulesPassTheLaw() => Assert.Empty(RuleKnowsNoOtherLaw.Violations(TalkRuleCatalog.CheckedTypes));

        [Fact]
        public void TakingAnotherRuleFailsTheLaw() =>
            Assert.Contains(RuleKnowsNoOtherLaw.Violations([typeof(RuleTakingAnotherRule)]), violation => violation.Contains(nameof(RuleTakingAnotherRule), StringComparison.Ordinal));

        [Fact]
        public void TakingTheRuleBookFailsTheLaw() =>
            Assert.Contains(RuleKnowsNoOtherLaw.Violations([typeof(RuleTakingTheBook)]), violation => violation.Contains(nameof(RuleTakingTheBook), StringComparison.Ordinal));

        [Fact]
        public void TakingTheSwitchesFailsTheLaw() =>
            Assert.Contains(RuleKnowsNoOtherLaw.Violations([typeof(RuleTakingTheSwitches)]), violation => violation.Contains(nameof(RuleTakingTheSwitches), StringComparison.Ordinal));

        [Fact]
        public void TakingTheSwitchboardFailsTheLaw() =>
            Assert.Contains(RuleKnowsNoOtherLaw.Violations([typeof(RuleTakingTheSwitchboard)]), violation => violation.Contains(nameof(RuleTakingTheSwitchboard), StringComparison.Ordinal));

        [Fact]
        public void TakingAnArrayOfRulesFailsTheLaw() =>
            Assert.Contains(RuleKnowsNoOtherLaw.Violations([typeof(RuleTakingARuleArray)]), violation => violation.Contains(nameof(RuleTakingARuleArray), StringComparison.Ordinal));

        [Fact]
        public void TakingTheServiceProviderFailsTheLaw() =>
            Assert.Contains(RuleKnowsNoOtherLaw.Violations([typeof(RuleTakingAServiceProvider)]), violation => violation.Contains(nameof(RuleTakingAServiceProvider), StringComparison.Ordinal));
    }

    // Sad path
    // AC7 — law R2, a judge-only rule with I/O
    public sealed class ScenarioAJudgeWithInputOutput
    {
        [Fact]
        public void TheShippedAndGoodJudgesPassTheLaw() => Assert.Empty(JudgesArePureLaw.Violations(TalkRuleCatalog.CheckedTypes));

        [Fact]
        public void TakingAStoreFailsTheLaw() =>
            Assert.Contains(JudgesArePureLaw.Violations([typeof(JudgeTakingAStore)]), violation => violation.Contains(nameof(JudgeTakingAStore), StringComparison.Ordinal));

        [Fact]
        public void TakingAnHttpClientFailsTheLaw() =>
            Assert.Contains(JudgesArePureLaw.Violations([typeof(JudgeTakingAnHttpClient)]), violation => violation.Contains(nameof(JudgeTakingAnHttpClient), StringComparison.Ordinal));

        [Fact]
        public void TakingAClockFailsTheLaw() =>
            Assert.Contains(JudgesArePureLaw.Violations([typeof(JudgeTakingAClock)]), violation => violation.Contains(nameof(JudgeTakingAClock), StringComparison.Ordinal));

        [Fact]
        public void TakingARandomSourceFailsTheLaw() =>
            Assert.Contains(JudgesArePureLaw.Violations([typeof(JudgeTakingARandomSource)]), violation => violation.Contains(nameof(JudgeTakingARandomSource), StringComparison.Ordinal));
    }
}
