// STORY-197 — Speech waits for the track boundary
//
// BDD specification — xUnit (SPEC F74.1, F74.2, F74.4). Pending scaffold; /build-loop
// (PLAN T42) implements and removes Skip. The real-boundary compose acceptance is T42's
// wire criterion.

using Xunit;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureSpeechBoundaryDeferral
{
    private const string Pending = "pending — PLAN T42 (/build-loop)";

    public static class ScenarioBoundaryAiring
    {
        [Fact(Skip = Pending)]
        public static void Deferred_ident_airs_at_the_boundary_never_mid_track()
        {
            // Given an ident deferral queued mid-track
            // When  the current track ends
            // Then  the ident airs at the boundary and never interrupted the track (F74.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioSupersede
    {
        [Fact(Skip = Pending)]
        public static void Newer_deferral_of_same_kind_replaces_the_stale_one()
        {
            // Given two idents of the same kind queued across one long track
            // When  the boundary arrives
            // Then  only the newer airs and the stale one never does (F74.2)
            Assert.Fail(Pending);
        }
    }

    public static class SadPathRestart
    {
        [Fact(Skip = Pending)]
        public static void Deferrals_regenerate_after_restart_and_nothing_double_airs()
        {
            // Given queued deferrals and a host restart
            // When  the schedule state is rebuilt
            // Then  due deferrals regenerate and nothing double-airs (F74.4)
            Assert.Fail(Pending);
        }
    }
}
