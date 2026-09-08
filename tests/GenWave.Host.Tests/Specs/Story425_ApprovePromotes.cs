// STORY-425 — Approve promotes the preview to on-air (SPEC F174.5 · PLAN T445)

namespace GenWave.Host.Tests.Specs;

public static class FeatureApprovePromotesThePreviewToOnAir
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioApprovePromotesWhenThePreviewIsCurrent
    {
        [Fact]
        public void ApproveIs200WithStateReady()
            => Assert.Fail("pending: T445 POST /api/ads/{id}/approve promotes — AC1");

        [Fact]
        public void MediaIdPointsAtThePromotedFileNotThePreviewPath()
            => Assert.Fail("pending: T445 landed media row — AC1");

        [Fact]
        public void PreviewStampsAreCleared()
            => Assert.Fail("pending: T445 preview_path/key cleared — AC1");
    }

    public sealed class ScenarioTheAiredTitleMatchesWhatThePreviewSays
    {
        [Fact]
        public void TitleAndArtistAreTheF161LandingShape()
            => Assert.Fail("pending: T445 same stamps as a worker render — AC2");
    }

    public sealed class ScenarioApproveWithoutAPreviewBehavesAsToday
    {
        [Fact]
        public void ApproveIs200WithStateApproved()
            => Assert.Fail("pending: T445 approve → approved — AC3");

        [Fact]
        public void NoMediaIdIsSet()
            => Assert.Fail("pending: T445 worker renders later — AC3");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRefusingAndFallingBack
    {
        [Fact]
        public void ApproveOnAStalePreviewIs409PreviewStale()
            => Assert.Fail("pending: T445 409 preview_stale — AC4");

        [Fact]
        public void ALandingFailureIs500()
            => Assert.Fail("pending: T445 promotion failure status — AC5");

        [Fact]
        public void TheRowIsLeftApprovedWithNoMediaId()
            => Assert.Fail("pending: T445 fallback to approved — AC5");

        [Fact]
        public void NoPartialMediaRowRemains()
            => Assert.Fail("pending: T445 no torn landing — AC5");
    }
}
