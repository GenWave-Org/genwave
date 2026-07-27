// STORY-244 — Listeners see who's on and who's next (SPEC F93.1/F93.2/F93.4/F93.5, PLAN T125)
//
// BDD specification — xUnit, pending. Entry-point discipline: every scenario drives the real
// GET /spectator/api/now-playing through WebApplicationFactory<Program>, credential-free,
// across staffed / music-only / gap / standby states seeded via the resolver's week snapshot.

namespace GenWave.Host.Tests.Specs;

public static class FeatureWhoIsOnAndWhoIsNext
{
    public sealed class ScenarioDjOnBothStates
    {
        // Given a staffed segment on air (F93.1).

        [Fact(Skip = "Pending (T125)")]
        public void TrackStateCarriesTheOnAirDisplayName() { }

        [Fact(Skip = "Pending (T125)")]
        public void PatterStateCarriesTheSameDisplayName() { }
    }

    public sealed class ScenarioExactlyOneUpNext
    {
        // Given a stored week with a future segment (F93.2).

        [Fact(Skip = "Pending (T125)")]
        public void UpNextCarriesStartsAtAndDj() { }

        [Fact(Skip = "Pending (T125)")]
        public void MusicOnlyNextCarriesNullDj() { }

        [Fact(Skip = "Pending (T125)")]
        public void NoDeeperLookaheadExistsInAnyPublicPayload() { }
    }

    public sealed class ScenarioHotPathStaysInMemory
    {
        // Given the poll path under load (F93.4).

        [Fact(Skip = "Pending (T125)")]
        public void AssemblyIssuesNoDbOrEngineCall() { }

        [Fact(Skip = "Pending (T125)")]
        public void ExistingCachePoliciesAndLimitsAreUnchanged() { }
    }

    public sealed class ScenarioUnstaffedAndStandbyAreHonest
    {
        // Sad path — music-only segment, grid gap, standby (F93.1, F93.5).

        [Fact(Skip = "Pending (T125)")]
        public void MusicOnlyAndGapReturnNullDj() { }

        [Fact(Skip = "Pending (T125)")]
        public void StandbyShapeIsUnchanged() { }

        [Fact(Skip = "Pending (T125)")]
        public void DisclosureContractGainsExactlyDjUpNextArtworkUrl() { }
    }
}
