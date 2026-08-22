// STORY-359 — The house never leaks to a public stream (SPEC F145.1/.2 · PLAN T339 + T343)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementPrivacy
{
    public sealed class ScenarioTheEndpointRefusesWhilePublic
    {
        [Fact(Skip = "pending T339 (STORY-359 AC1)")]
        public void AValidPostUnderSpectatorModeIsAFourOhThreeWithAnHonestReason() { }

        [Fact(Skip = "pending T339 (STORY-359 AC1)")]
        public void NoRowIsCreatedByTheRefusedPost() { }
    }

    public sealed class ScenarioGoingPublicDeclinesTheQueue
    {
        [Fact(Skip = "pending T343 (STORY-359 AC3)")]
        public void EveryPendingAnnouncementDeclinesAtThePrivateToPublicFlip() { }

        [Fact(Skip = "pending T343 (STORY-359 AC3)")]
        public void EveryClaimedAnnouncementDeclinesAtThePrivateToPublicFlip() { }

        [Fact(Skip = "pending T343 (STORY-359 AC3)")]
        public void TheDeclineReasonSaysTheStationWentPublic() { }
    }
}
