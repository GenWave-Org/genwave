// STORY-570 — One flow for flagged talk (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Ads.Tests.Specs;

public static class FeatureFlaggedAdsFail
{
    // AC5 — an ad still flagged by two rules after the re-ask
    public sealed class ScenarioAnAdIsStillFlagged
    {
        [Fact(Skip = "pending: T802")]
        public void TheSpotIsFailed() { }

        [Fact(Skip = "pending: T802")]
        public void TheReasonNamesEveryFlaggedRule() { }

        [Fact(Skip = "pending: T802")]
        public void OnlyOneReAskWasMade() { }
    }

    // AC2 — an ad two rules flag
    public sealed class ScenarioAnAdTwoRulesFlag
    {
        [Fact(Skip = "pending: T802")]
        public void TheReAskListsBothAsks() { }
    }
}
