// STORY-212 — The envelope is law, and silence is forbidden
//
// BDD specification — xUnit (SPEC F81.1, F81.3, F81.4). PLAN T61 — the catalog side of the law:
// filtering happens by construction in the candidate query, never by post-filtering a wider set.
// (The provider/ladder half of this story is Story212_EnvelopeProviderAndLadder.cs in
// Orchestration.Tests.)

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEnvelopeCandidateQuery
{
    public static class ScenarioFilterByConstruction
    {
        // Arrange (T61): library seeded with tracks in/out of genre and in/out of an
        // EnergyRange; query with a SegmentEnvelope.

        [Fact(Skip = "Pending T61 — see docs/PLAN.md")]
        public static void NoTrackOutsideTheGenreListEntersThePool()
        {
            // F81.4 by-construction: the SQL predicate excludes them, not a later filter
            Assert.Fail("pending T61");
        }

        [Fact(Skip = "Pending T61 — see docs/PLAN.md")]
        public static void NoTrackOutsideTheEnergyRangeEntersThePool()
        {
            Assert.Fail("pending T61");
        }

        [Fact(Skip = "Pending T61 — see docs/PLAN.md")]
        public static void RotationWindowStillApplies()
        {
            // envelope filtering composes with the existing rotation window, not replaces it (F81.1)
            Assert.Fail("pending T61");
        }
    }

    public static class ScenarioStationDefaultEnvelope
    {
        // Arrange (T61): station settings carrying the default 24/7 envelope.

        [Fact(Skip = "Pending T61 — see docs/PLAN.md")]
        public static void TheDefaultEnvelopeIsReadFromStationSettings()
        {
            // F81.3 v1 scope: one envelope, settings-resident, no schedule grid
            Assert.Fail("pending T61");
        }
    }
}
