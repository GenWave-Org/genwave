// STORY-372 — The pool honours the rotation predicate (SPEC F152.2 · PLAN T359)
//
// BDD specification — xUnit. PENDING until T359. Arrange sketch: DatabaseFixture — seed ready +
// measurable + eligible rows in library 1 via MediaRepository (Story212_EnvelopeCandidateQuery.cs's
// InsertReadyAsync idiom) plus a library.media_rotation row per play-count/last-aired-at case,
// then call GetEnvelopeCandidatePoolAsync with a SegmentEnvelope carrying Rotation and assert the
// returned id set.
namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureThePoolHonoursTheRotationPredicate
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — MaxPlays and NotAiredWithinDays, by construction
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePoolHonoursMaxPlays
    {
        // Given an envelope with Rotation MaxPlays 0 and a library where 6 rows never aired and
        // 4 did, When the candidate pool is queried.
        [Fact(Skip = "pending T359 (STORY-372 AC2)")]
        public void OnlyTheSixNeverAiredRowsAreReturned() => Assert.Fail("pending T359");
    }

    public sealed class ScenarioThePoolHonoursNotAiredWithinDays
    {
        // Given Rotation NotAiredWithinDays 30 and rows last aired 10, 40, never, When the pool
        // is queried.
        [Fact(Skip = "pending T359 (STORY-372 AC3)")]
        public void TheFortyDayAndNeverRowsAreReturned() => Assert.Fail("pending T359");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no predicate, no drift
    // ---------------------------------------------------------------------

    public sealed class ScenarioNoPredicateNoStamp
    {
        // Given a show without a rotation rule, When picks run.
        [Fact(Skip = "pending T359 (STORY-372 AC10)")]
        public void RotationRelaxIsAbsentFromEveryStamp() => Assert.Fail("pending T359");

        [Fact(Skip = "pending T359 (STORY-372 AC10)")]
        public void ThePoolSqlIsByteIdenticalToPreF152() => Assert.Fail("pending T359");
    }
}
