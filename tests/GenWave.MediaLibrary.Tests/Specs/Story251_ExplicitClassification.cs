// STORY-251 — Know which tracks are explicit (SPEC F95.2, F95.3, F95.5, PLAN T110/T112/T113/T115)
//
// BDD specification — xUnit, pending. db/26 schema + the layered tag → LLM sweep → operator
// pipeline, driven against a real Postgres with the LLM faked at the HTTP boundary (T72
// mood-tagger idiom). The operator override endpoint's wire facts (T115) drive the real
// admin route via the Host factory.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureExplicitClassification
{
    public sealed class ScenarioTheSpineCarriesTheFlag
    {
        // Given db/26 applied (F95.2).

        [Fact(Skip = "Pending (T110)")]
        public void ExplicitBooleanExistsWithNullAsUnknown() { }

        [Fact(Skip = "Pending (T110)")]
        public void ExplicitSourceIsConstrainedToTagLlmOperator() { }
    }

    public sealed class ScenarioAdvisoryTagsStampFirst
    {
        // Given a file whose metadata carries an explicit/advisory flag, When enrichment runs (F95.3).

        [Fact(Skip = "Pending (T112)")]
        public void ExplicitIsStampedWithSourceTag() { }

        [Fact(Skip = "Pending (T112)")]
        public void UntaggedFilesStayNull() { }
    }

    public sealed class ScenarioTheSweepCoversTheRest
    {
        // Given unclassified tracks and a configured LLM, When the offline batch pass runs (F95.3).

        [Fact(Skip = "Pending (T113)")]
        public void YesAndNoStampSourceLlm() { }

        [Fact(Skip = "Pending (T113)")]
        public void UnknownStampsAMissNeverAPartialWrite() { }

        [Fact(Skip = "Pending (T113)")]
        public void AlreadyClassifiedRowsAreNotReAsked() { }
    }

    public sealed class ScenarioTheOperatorAlwaysWins
    {
        // Given an operator override (source operator) (F95.3), set via the real admin endpoint (T115).

        [Fact(Skip = "Pending (T115)")]
        public void OverrideEndpointStampsSourceOperator() { }

        [Fact(Skip = "Pending (T113)")]
        public void LaterSweepsNeverOverwriteOperatorRows() { }
    }

    public sealed class ScenarioNeverPlayStaysOrthogonal
    {
        // Given a track under a never-play verdict (F95.5): the flag classifies, the verdict rules.

        [Fact(Skip = "Pending (T115)")]
        public void VerdictOperatesUnchangedRegardlessOfClassification() { }
    }

    public sealed class ScenarioLlmDownSkipsCleanly
    {
        // Sad path — LLM unreachable (F95.3, F69 pattern).

        [Fact(Skip = "Pending (T113)")]
        public void SweepSkipsWithASingleLogLineAndNoPartialStamps() { }
    }
}
