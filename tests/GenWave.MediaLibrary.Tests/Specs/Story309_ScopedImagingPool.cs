// STORY-309 — Show-branded idents (F117) — pool-query + authored-scope half
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: library.media.show_id lands at T238, the scoped query at T250, the authored
// insert scope at T246. No FK across the schema/grant boundary (the db/22 precedent).
// The drain-preference half lives in Orchestration.Tests/Story309_ShowIdentDrain.cs.

namespace GenWave.MediaLibrary.Tests.Specs;

using Xunit;

public static class FeatureScopedImagingPool
{
    public sealed class ScenarioScopedQuery
    {
        [Fact(Skip = "Pending (T250)")]
        public void ScopedRowsPreferredWhenAShowIsActive()
        {
            // Given ready station_id rows both scoped to show 7 and unscoped
            // When  the pool query runs for show 7
            // Then  only scoped rows are candidates in the scoped-first pass
        }

        [Fact(Skip = "Pending (T250)")]
        public void UnscopedFallbackWhenNoScopedRows()
        {
            // Given only unscoped ready station_id rows
            // When  the pool query runs for a show
            // Then  the unscoped fallback pass serves them (the station-wide pool survives)
        }

        [Fact(Skip = "Pending (T250)")]
        public void ScopedRowsNeverServeOutsideTheirShow()
        {
            // Given a ready row scoped to show 7
            // When  the pool query runs with no show (or another show) active
            // Then  the scoped row is not a candidate — scoped means scoped (F117.1)
        }
    }

    public sealed class ScenarioAuthoringWithScope
    {
        [Fact(Skip = "Pending (T246)")]
        public void InsertAuthoredAcceptsAShowScope()
        {
            // Given the authoring path with a show scope selected
            // When  InsertAuthoredAsync runs
            // Then  the row lands with show_id set; the default remains station-wide NULL
        }
    }
}
