// STORY-371 — The aggregate is bounded and remembers (SPEC F150.9 · PLAN T365)
//
// BDD specification — xUnit. PENDING until T365. Arrange sketch: DatabaseFixture (ephemeral
// Postgres, see existing tests/GenWave.MediaLibrary.Tests/Specs/Story212_EnvelopeCandidateQuery.cs
// for the [Collection(DatabaseCollection.Name)] + DatabaseFixture ctor idiom) — seed
// library.media_thumb rows at controlled ages against a ready row, run IThumbStore's aggregate
// recompute (or the T365 retention sweep), and read library.media_rotation.nudge/thumbs_up/
// thumbs_down back with a raw SQL read.
namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureThumbsAggregateIsBounded
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the formula, its decay, and its clamp
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheAggregateFormula
    {
        // Given five up-thumbs on a track within the hour, HalfLifeDays 30, Saturation 5, When
        // the aggregate is recomputed.
        [Fact(Skip = "pending T365 (STORY-371 AC1)")]
        public void NudgeIsOne() => Assert.Fail("pending T365");
    }

    public sealed class ScenarioDecay
    {
        // Given one up-thumb aged exactly 30 days, When the aggregate is recomputed.
        [Fact(Skip = "pending T365 (STORY-371 AC2)")]
        public void NudgeIsZeroPointOne() => Assert.Fail("pending T365");
    }

    public sealed class ScenarioTheClamp
    {
        // Given twelve up-thumbs within the hour, When the aggregate is recomputed.
        [Fact(Skip = "pending T365 (STORY-371 AC3)")]
        public void NudgeClampsToOneNotTwoPointFour() => Assert.Fail("pending T365");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the sweep never touches the counters
    // ---------------------------------------------------------------------

    public sealed class ScenarioRetentionKeepsTheCounters
    {
        // Given thumb rows older than ThumbRetentionDays, When the sweep runs.
        [Fact(Skip = "pending T365 (STORY-371 AC10)")]
        public void TheOldRowsAreGone() => Assert.Fail("pending T365");

        [Fact(Skip = "pending T365 (STORY-371 AC10)")]
        public void ThumbsUpDownAndNudgeOnMediaRotationAreUnchanged() => Assert.Fail("pending T365");
    }
}
