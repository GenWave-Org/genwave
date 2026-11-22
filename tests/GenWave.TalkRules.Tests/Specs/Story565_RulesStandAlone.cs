// STORY-565 — Rules stand alone (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureRulesStandAlone
{
    // AC1 — a reply that breaks the facts rule and the repeats rule
    public sealed class ScenarioAReplyBreaksTwoRules
    {
        [Fact(Skip = "pending: T776")]
        public void TheFactsRuleFlagsIt() { }

        [Fact(Skip = "pending: T776")]
        public void TheRepeatsRuleFlagsIt() { }
    }

    // AC5 — a memo written by rule A
    public sealed class ScenarioAnotherRulesMemo
    {
        [Fact(Skip = "pending: T776")]
        public void RuleBGetsNothing() { }

        [Fact(Skip = "pending: T776")]
        public void RuleAGetsItsOwnMemo() { }
    }

    // Sad path
    // AC8 — a rule that throws
    public sealed class ScenarioARuleThrows
    {
        [Fact(Skip = "pending: T776")]
        public void ItsVerdictIsAFlagCouldNotCheck() { }

        [Fact(Skip = "pending: T776")]
        public void TheOtherRulesVerdictsArePresent() { }

        [Fact(Skip = "pending: T776")]
        public void AWarningNamesTheRule() { }
    }
}
