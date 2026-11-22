// STORY-564 — A rule is one file that describes itself (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureDescribesItselfLaw
{
    // AC1, AC3 — law R6 over every shipped rule
    public sealed class ScenarioEveryShippedRule
    {
        [Fact(Skip = "pending: T777")]
        public void HasAUniqueKebabId() { }

        [Fact(Skip = "pending: T777")]
        public void NameStartsWithACapitalAndEndsWithAFullStop() { }

        [Fact(Skip = "pending: T777")]
        public void WhatItDoesStartsWithACapitalAndEndsWithAFullStop() { }

        [Fact(Skip = "pending: T777")]
        public void WhatItDoesStatesItsOwnNumbers() { }

        [Fact(Skip = "pending: T777")]
        public void DefaultCoverIsWithinWhatItCanCover() { }
    }

    // AC2 — law R5, a rule declared in the test assembly
    public sealed class ScenarioARuleDeclaredInOneFile
    {
        [Fact(Skip = "pending: T777")]
        public void IsListedByTheRuleBookWithNoOtherEdit() { }
    }

    // Sad path
    // AC4 — a rule that neither shapes nor judges
    public sealed class ScenarioARuleWithNeitherPart
    {
        [Fact(Skip = "pending: T777")]
        public void TheLawFailsNamingTheRule() { }
    }

    // Sad path
    // AC5 — two rules with one id
    public sealed class ScenarioTwoRulesShareAnId
    {
        [Fact(Skip = "pending: T777")]
        public void TheLawFailsNamingBoth() { }
    }
}
