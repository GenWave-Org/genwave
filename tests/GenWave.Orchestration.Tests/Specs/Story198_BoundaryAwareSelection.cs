// STORY-198 — Selection avoids burying the boundary
//
// BDD specification — xUnit (SPEC F74.3). Pending scaffold; /build-loop (PLAN T43)
// implements and removes Skip.

using Xunit;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBoundaryAwareSelection
{
    private const string Pending = "pending — PLAN T43 (/build-loop)";

    public static class ScenarioBiasNearDeadline
    {
        [Fact(Skip = Pending)]
        public static void Shorter_track_wins_when_an_ident_is_due_soon()
        {
            // Given an ident due in 3 minutes and two otherwise-equal candidates of
            //       3 and 9 minutes
            // When  the next track is selected
            // Then  the 3-minute track is chosen (F74.3)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioSoftNeverAFilter
    {
        [Fact(Skip = Pending)]
        public static void Only_long_tracks_available_still_selects_a_track()
        {
            // Given only long tracks available near a deadline
            // When  selection runs
            // Then  a track is still selected — the pool never empties for boundary
            //       reasons (F74.3)
            Assert.Fail(Pending);
        }
    }
}
