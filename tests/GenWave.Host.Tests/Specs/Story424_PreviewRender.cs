// STORY-424 — Preview renders on demand with a staleness key (SPEC F174.4 · PLAN T442)

namespace GenWave.Host.Tests.Specs;

public static class FeaturePreviewRendersOnDemandWithAStalenessKey
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPostPreviewEnqueuesAndStampsPreviewKey
    {
        [Fact]
        public void PreviewIs202()
            => Assert.Fail("pending: T442 POST /api/ads/{id}/preview — AC1");

        [Fact]
        public void TheRowHasPreviewPathAtAndKey()
            => Assert.Fail("pending: T442 preview stamps — AC1");

        [Fact]
        public void PreviewKeyIsSha256Hex()
            => Assert.Fail("pending: T442 preview_key shape — AC1");

        [Fact]
        public void CastAndMusicAreStampedByThePickers()
            => Assert.Fail("pending: T442 voice_plan + bed_media_id non-null — AC1");
    }

    public sealed class ScenarioPreviewFileLivesUnderThePreviewFolder
    {
        [Fact]
        public void ThePathMatchesLibraryRootPreviewSpotIdKeyWav()
            => Assert.Fail("pending: T442 {Ads:LibraryRoot}/preview/<spotId>-<key>.wav — AC2");

        [Fact]
        public void NoLibraryMediaRowExistsForIt()
            => Assert.Fail("pending: T442 preview lands no media row — AC2");
    }

    public sealed class ScenarioGetPreviewWavStreamsTheFile
    {
        [Fact]
        public void PreviewWavIs200AudioWav()
            => Assert.Fail("pending: T442 GET /api/ads/{id}/preview.wav — AC3");

        [Fact]
        public void TheBodyIsNonEmpty()
            => Assert.Fail("pending: T442 preview bytes — AC3");
    }

    public sealed class ScenarioAnEditInvalidatesThePreview
    {
        [Fact]
        public void TheRowReportsPreviewStaleTrue()
            => Assert.Fail("pending: T442 preview.stale after PATCH — AC4");

        [Fact]
        public void PreviewWavIs404WhenStale()
            => Assert.Fail("pending: T442 GET preview.wav 404 stale — AC4");
    }

    // ---------------------------------------------------------------------
    // SAD PATH (segregated)
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingWithoutAScript
    {
        [Fact]
        public void PreviewWithoutAScriptIs400ScriptRequired()
            => Assert.Fail("pending: T442 400 script_required — AC5");
    }
}
