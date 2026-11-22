// STORY-565 — Rules stand alone (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureRulesStandAloneLaws
{
    // AC2 — law R3, every rotation and 20 seeded shuffles
    public sealed class ScenarioRulesRunInAnyOrder
    {
        [Fact(Skip = "pending: T777")]
        public void TheVerdictSetsAreEqual() { }
    }

    // AC3 — law R4, each rule switched off in turn
    public sealed class ScenarioARuleIsSwitchedOff
    {
        [Fact(Skip = "pending: T777")]
        public void TheVerdictsEqualAllOnMinusThatRule() { }
    }

    // AC4 — a stored snapshot judged twice
    public sealed class ScenarioASnapshotIsJudgedTwice
    {
        [Fact(Skip = "pending: T777")]
        public void TheVerdictsAreEqual() { }
    }

    // Sad path
    // AC6 — law R1, a rule that takes another rule, the rule book or the switches
    public sealed class ScenarioARuleKnowsAnother
    {
        [Fact(Skip = "pending: T777")]
        public void TakingAnotherRuleFailsTheLaw() { }

        [Fact(Skip = "pending: T777")]
        public void TakingTheRuleBookFailsTheLaw() { }

        [Fact(Skip = "pending: T777")]
        public void TakingTheSwitchesFailsTheLaw() { }
    }

    // Sad path
    // AC7 — law R2, a judge-only rule with I/O
    public sealed class ScenarioAJudgeWithInputOutput
    {
        [Fact(Skip = "pending: T777")]
        public void TakingAStoreFailsTheLaw() { }

        [Fact(Skip = "pending: T777")]
        public void TakingAnHttpClientFailsTheLaw() { }

        [Fact(Skip = "pending: T777")]
        public void TakingAClockFailsTheLaw() { }

        [Fact(Skip = "pending: T777")]
        public void TakingARandomSourceFailsTheLaw() { }
    }
}
