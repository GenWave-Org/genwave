// STORY-358 — The DJ says it: two fidelities, one fallback (SPEC F144.1/.2 · PLAN T341)
using Xunit;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureAnnouncementVendAndVerbatim
{
    public sealed class ScenarioVendAtUnitAssembly
    {
        [Fact(Skip = "pending T341 (STORY-358 AC1)")]
        public void TheTwoOldestDeliverableAnnouncementsAreClaimedAtomically() { }

        [Fact(Skip = "pending T341 (STORY-358 AC1)")]
        public void AThirdPendingAnnouncementWaitsForTheNextUnit() { }

        [Fact(Skip = "pending T341 (STORY-358 AC1)")]
        public void EachVendedAnnouncementBecomesAnAnnouncementKindSegment() { }

        [Fact(Skip = "pending T341 (STORY-358 AC1)")]
        public void TheSegmentIsPlacedAfterTheBackAnnounce() { }
    }

    public sealed class ScenarioVerbatimBypassesTheLlm
    {
        [Fact(Skip = "pending T341 (STORY-358 AC2)")]
        public void TheExactMessageTextRendersThroughTheTtsPipeline() { }

        [Fact(Skip = "pending T341 (STORY-358 AC2)")]
        public void NoLlmCallOccursForAVerbatimAnnouncement() { }

        [Fact(Skip = "pending T341 (STORY-358 AC2)")]
        public void ARequestedVoiceIsHonoredWhenKnown() { }

        [Fact(Skip = "pending T341 (STORY-358 AC2)")]
        public void AnUnknownRequestedVoiceFallsBackToTheStationVoice() { }
    }

    public sealed class ScenarioTheVendRefusesWhilePublic
    {
        [Fact(Skip = "pending T341 (STORY-359 AC2)")]
        public void NoAnnouncementVendsWhileSpectatorModeIsOn() { }
    }
}
