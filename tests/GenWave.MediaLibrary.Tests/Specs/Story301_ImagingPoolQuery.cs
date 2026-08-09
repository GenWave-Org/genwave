// STORY-301 — Top-of-hour idents from the imaging pool: the pool query (F110.2, T231)

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureImagingPoolQuery
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioRandomReadyByImagingKind
    {
        [Fact(Skip = "Pending T231 — see docs/PLAN.md")]
        public void ReturnsOnlyRowsOfTheRequestedKind()
        {
            // Seed liner + station_id + jingle rows; query kind=station_id ⇒ every
            // returned row has imaging_kind='station_id'.
            // Assert.All(rows, r => Assert.Equal("station_id", r.ImagingKind));
            Assert.Fail("pending T231");
        }

        [Fact(Skip = "Pending T231 — see docs/PLAN.md")]
        public void ReturnsOnlyReadyRows()
        {
            // A station_id row that is not ready (unenriched/unavailable) never returns.
            // Assert.DoesNotContain(rows, r => r.Id == notReadyId);
            Assert.Fail("pending T231");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEmptyPool
    {
        [Fact(Skip = "Pending T231 — see docs/PLAN.md")]
        public void NoMatchingRowsReturnsNullNotAnError()
        {
            // Zero station_id rows ⇒ null result (the drain's template-fallback signal).
            // Assert.Null(row);
            Assert.Fail("pending T231");
        }
    }
}
