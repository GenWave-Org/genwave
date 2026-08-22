// STORY-358 — The DJ says it: two fidelities, one fallback (SPEC F143.3, F144.5/.6 · PLAN T343)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementAirConfirmation
{
    public sealed class ScenarioAiredMeansObservedOnAir
    {
        [Fact(Skip = "pending T343 (STORY-358 AC5)")]
        public void ATrackAiredObservationOfTheSegmentStampsAired() { }

        [Fact(Skip = "pending T343 (STORY-358 AC5)")]
        public void OneBoothLogEntryCarriesTheCollapseCount() { }

        [Fact(Skip = "pending T343 (STORY-358 AC5)")]
        public void APushAloneNeverStampsAired() { }
    }

    public sealed class ScenarioPushLossReArms
    {
        [Fact(Skip = "pending T343 (STORY-358 AC6)")]
        public void AClaimedAnnouncementUnairedPastTheGraceReturnsToPending() { }

        [Fact(Skip = "pending T343 (STORY-358 AC6)")]
        public void AReArmWithNoTtlRemainingExpiresVisiblyInstead() { }
    }
}
