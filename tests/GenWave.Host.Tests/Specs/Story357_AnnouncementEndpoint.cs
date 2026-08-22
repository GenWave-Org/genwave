// STORY-357 — An accepted announcement never vanishes (SPEC F143.1/.4 · PLAN T339)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementEndpoint
{
    public sealed class ScenarioAcceptedMeansDurableBeforeTheReply
    {
        [Fact(Skip = "pending T339 (STORY-357 AC1)")]
        public void AValidPostCreatesThePendingRowBeforeTheTwoHundredReturns() { }

        [Fact(Skip = "pending T339 (STORY-357 AC1)")]
        public void ATtlOverrideInsideTheBoundsIsHonored() { }

        [Fact(Skip = "pending T339 (STORY-357 AC1)")]
        public void ATtlOverrideOutsideSixtyToThirtySixHundredIsRejected() { }
    }

    public sealed class ScenarioEveryCapDeclinesVisibly
    {
        [Fact(Skip = "pending T339 (STORY-357 AC3)")]
        public void AMessageOverTwoEightyCharsIsAFourHundredWithAnHonestReason() { }

        [Fact(Skip = "pending T339 (STORY-357 AC3)")]
        public void ASeventhAcceptedSubmissionInsideAMinuteIsAFourTwentyNine() { }

        [Fact(Skip = "pending T339 (STORY-357 AC3)")]
        public void AThirteenthPendingAnnouncementIsAFourTwentyNine() { }

        [Fact(Skip = "pending T339 (STORY-357 AC3)")]
        public void NoCappedRequestEverReachesTheStore() { }
    }
}
