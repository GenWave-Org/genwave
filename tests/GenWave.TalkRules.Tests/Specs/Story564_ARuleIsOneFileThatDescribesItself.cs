// STORY-564 — A rule is one file that describes itself (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.TalkRules.Tests.Specs;

public static class FeatureARuleIsOneFileThatDescribesItself
{
    // AC2 — a rule class added as one file
    public sealed class ScenarioARuleAddedAsOneFile
    {
        [Fact(Skip = "pending: T776")]
        public void TheRuleBookListsIt() { }

        [Fact(Skip = "pending: T776")]
        public void TheRuleBookRunsItsJudge() { }
    }

    // AC1 — every shipped rule, through the rule book listing
    public sealed class ScenarioEveryShippedRuleIsListed
    {
        [Fact(Skip = "pending: T776")]
        public void EachRuleHasADescription() { }

        [Fact(Skip = "pending: T776")]
        public void EachRuleNamesTheTalkItCanCover() { }
    }
}
