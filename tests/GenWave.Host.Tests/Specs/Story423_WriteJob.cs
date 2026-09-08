// STORY-423 — "Write it for me" runs as a job (SPEC F174.3 · PLAN T441)

namespace GenWave.Host.Tests.Specs;

public static class FeatureWriteItForMeRunsAsAJob
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPostWriteEnqueues
    {
        [Fact]
        public void WriteIs202()
            => Assert.Fail("pending: T441 POST /api/ads/{id}/write — AC1");

        [Fact]
        public void TheRowHasJobKindWriteAndAStartedAt()
            => Assert.Fail("pending: T441 job stamps — AC1");
    }

    public sealed class ScenarioTheScriptLandsOnTheRowOnSuccess
    {
        [Fact]
        public void ScriptEqualsTheWritersOutput()
            => Assert.Fail("pending: T441 write job lands script — AC2");

        [Fact]
        public void SourceIsUnchanged()
            => Assert.Fail("pending: T441 write keeps source — AC2");

        [Fact]
        public void VoicePlanIsStillNull()
            => Assert.Fail("pending: T441 write does not stamp cast — AC2");

        [Fact]
        public void JobIsNullAfterCompletion()
            => Assert.Fail("pending: T441 write clears stamps — AC2");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingAndFailing
    {
        [Fact]
        public void WriteOutsideDraftIs409AdWriteNotDraft()
            => Assert.Fail("pending: T441 409 ad_write_not_draft — AC3");

        [Fact]
        public void WriterFailureStampsJobErrorAndClearsJobKind()
            => Assert.Fail("pending: T441 job_error sanitised — AC4");
    }
}
