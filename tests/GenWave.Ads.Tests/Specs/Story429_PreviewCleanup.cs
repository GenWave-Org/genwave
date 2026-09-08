// STORY-429 — Preview files clean up automatically (SPEC F174.9 · PLAN T442)

namespace GenWave.Ads.Tests.Specs;

public static class FeaturePreviewFilesCleanUpAutomatically
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPromotionMovesTheFileNotCopies
    {
        [Fact]
        public void ThePreviewPathIsGoneAfterPromotion()
            => Assert.Fail("pending: T442 move not copy — AC1 (T445)");

        [Fact]
        public void ThePromotedFileExists()
            => Assert.Fail("pending: T442 move not copy — AC1 (T445)");
    }

    public sealed class ScenarioAReadyOrRetiredSpotsPreviewIsDeletedOnTheGuardianTick
    {
        [Fact]
        public void TheFileIsDeleted()
            => Assert.Fail("pending: T442 AdSpotLifecycleGuardianService preview sweep — AC2");

        [Fact]
        public void ThePreviewStampsAreCleared()
            => Assert.Fail("pending: T442 guardian clears preview_path/key/at — AC2");
    }

    public sealed class ScenarioRetentionWindowPrunesOldPreviewFiles
    {
        [Fact]
        public void AFileOlderThanPreviewRetentionDaysIsDeleted()
            => Assert.Fail("pending: T442 Ads:PreviewRetentionDays — AC3");

        [Fact]
        public void TheRowsPreviewStampsAreCleared()
            => Assert.Fail("pending: T442 guardian clears stamps — AC3");
    }
}
