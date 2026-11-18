// STORY-559 — Old audio is replaced quietly (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureOldAudioIsReplacedQuietly
{
    // AC2, AC3 — 3 stale ads and stale seeder content after a switch
    public sealed class ScenarioOnePerIdleTick
    {
        [Fact(Skip = "pending: T763")]
        public void OneAdIsReRenderedPerTick() { }

        [Fact(Skip = "pending: T763")]
        public void SeederContentIsReRenderedOnePerTick() { }
    }

    // AC4
    public sealed class ScenarioTheOldFileAirsMeanwhile
    {
        [Fact(Skip = "pending: T763")]
        public void AStaleItemIsStillEligible() { }
    }

    // AC5 — 4 stale items; GET /api/status
    public sealed class ScenarioTheCount
    {
        [Fact(Skip = "pending: T764")]
        public void TheStatusResponseCarriesFour() { }
    }

    // Sad path
    // AC6 — uploaded imaging
    public sealed class ScenarioDeansOwnAudio
    {
        [Fact(Skip = "pending: T763")]
        public void TheSweepSkipsIt() { }
    }
}
