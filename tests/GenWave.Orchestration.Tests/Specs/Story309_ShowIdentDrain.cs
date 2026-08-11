// STORY-309 — Show-branded idents (F117) — drain-preference half
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: the StationId drain's show preference lands at T250. Ladder under spec:
// scoped authored row → templated show line → F110.2 exactly. ⚠️ Drain-region caution
// recorded in PLAN: serialize behind T248 if both touch the drain switch.

namespace GenWave.Orchestration.Tests.Specs;

using Xunit;

public static class FeatureShowIdentDrain
{
    public sealed class ScenarioScopedPoolFirst
    {
        [Fact(Skip = "Pending (T250)")]
        public void ScopedAuthoredRowAirsDuringItsShow()
        {
            // Given a ready authored station_id row scoped to the current show
            // When  the StationId drain fires during that show
            // Then  the scoped row airs (authored voice preserved — it is rendered audio)
        }
    }

    public sealed class ScenarioTemplatedFloor
    {
        [Fact(Skip = "Pending (T250)")]
        public void TemplatedShowLineAirsWhenNoScopedRows()
        {
            // Given a show with zero scoped authored rows
            // When  the drain fires
            // Then  "You're listening to {show} on {station}." renders — station-voiced,
            //       zero LLM (the gate-countable floor, F117.2)
        }

        [Fact(Skip = "Pending (T250)")]
        public void SecondAiringIsACacheHit()
        {
            // Given the templated show line rendered once
            // When  the drain fires again for the same show name
            // Then  the render is a forever-cache hit (keyed on the rendered text)
        }

        [Fact(Skip = "Pending (T250)")]
        public void RenameRekeysTheCache()
        {
            // Given the show is renamed
            // When  the next drain fires
            // Then  a fresh render occurs by construction — the key IS the text
        }
    }

    public sealed class ScenarioOutsideShowsUntouched
    {
        [Fact(Skip = "Pending (T250)")]
        public void NoShowMeansF110Exactly()
        {
            // Given no show on the air
            // When  the StationId drain fires
            // Then  behavior is byte-identical to F110.2 (station pool → template)
        }
    }
}
