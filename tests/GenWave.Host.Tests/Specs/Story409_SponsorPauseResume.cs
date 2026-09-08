// STORY-409 — Pause and resume a sponsor (SPEC F171.4 · PLAN T434)

namespace GenWave.Host.Tests.Specs;

public static class FeaturePauseAndResumeASponsor
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPauseIsIdempotent
    {
        [Fact]
        public void TheFirstPauseIs200WithPausedTrue()
            => Assert.Fail("pending: T434 POST /api/sponsors/{id}/pause — AC1");

        [Fact]
        public void TheSecondPauseIs200Too()
            => Assert.Fail("pending: T434 POST pause idempotent — AC1");
    }

    public sealed class ScenarioResumeIsIdempotent
    {
        [Fact]
        public void TheFirstResumeIs200WithPausedFalse()
            => Assert.Fail("pending: T434 POST /api/sponsors/{id}/resume — AC2");

        [Fact]
        public void TheSecondResumeIs200Too()
            => Assert.Fail("pending: T434 POST resume idempotent — AC2");
    }

    public sealed class ScenarioPausedAtIsStampedAndCleared
    {
        [Fact]
        public void PauseStampsPausedAt()
            => Assert.Fail("pending: T434 paused_at set — AC3");

        [Fact]
        public void ResumeClearsPausedAt()
            => Assert.Fail("pending: T434 paused_at null — AC3");
    }
}
