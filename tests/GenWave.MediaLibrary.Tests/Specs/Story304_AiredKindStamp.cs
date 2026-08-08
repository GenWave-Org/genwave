// STORY-304 — Airings become countable (F113)

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAiredKindStamp
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheStampFlowsEndToEnd
    {
        [Fact(Skip = "Pending T219 — see docs/PLAN.md")]
        public void BoothLogHasASegmentKindColumn()
        {
            // information_schema: station.booth_log.segment_kind exists (text, nullable);
            // db/33 applied and db/06 fresh-init mirrored.
            // Assert.True(columnExists);
            Assert.Fail("pending T219");
        }

        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void AKindedTrackAiredWritesTheKind()
        {
            // TrackAired carrying SegmentKind.StationId ⇒ the track-started row's
            // segment_kind is 'StationId' (stamped synchronously at publish time).
            // Assert.Equal("StationId", row.SegmentKind);
            Assert.Fail("pending T220");
        }

        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void MusicRowsStayNull()
        {
            // A music TrackAired (SegmentKind null) writes NULL — the count query's
            // non-music predicate is segment_kind IS NOT NULL.
            // Assert.Null(row.SegmentKind);
            Assert.Fail("pending T220");
        }

        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void TheDemoHourQueryCountsFromTheColumnAlone()
        {
            // The documented query groups by date_trunc('hour') over segment_kind — no
            // LIKE over summary anywhere in it.
            // Assert.DoesNotContain("LIKE", documentedQuery);
            Assert.Fail("pending T220");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDroppedRendersNeverCount
    {
        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void ABudgetDroppedRenderProducesNoKindedRow()
        {
            // A render that times out of the budget logs patter-aired (render-time) but no
            // kinded track-started row exists — the air-time signal is the honest one.
            // Assert.Empty(kindedRowsForDroppedHash);
            Assert.Fail("pending T220");
        }
    }
}
