// STORY-376 — The same song twice (SPEC F153.5 · PLAN T354, T374)
//
// BDD specification — xUnit. PENDING until T354 (AC1–AC2, the fold_key/title_variant STORED
// columns) and T374 (AC3–AC5, AC7, the near_duplicate pass over find_near_duplicates). AC6 (Keep
// this one) is a Host/Jest concern — the bulk-eligibility click lives in the Admin UI test suite,
// not here; no fact for it in this file. Arrange sketch: DatabaseFixture — AC1/AC2 call fold_key/
// title_variant directly via SQL (select fold_key('...')); AC3–AC5/AC7 seed ready rows through
// MediaRepository and call library.find_near_duplicates, reading library.rot_finding back.
namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTheSameSongTwice
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the fold, the variant tail, and the duplicate group
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheFold
    {
        // Given titles "Héllo, World!" and "hello world", When fold_key runs.
        [Fact(Skip = "pending T354 (STORY-376 AC1)")]
        public void BothYieldHelloWorld() => Assert.Fail("pending T354");
    }

    public sealed class ScenarioTheVariantTail
    {
        // Given "Song (feat. X)", "Song [Live]", "Song (2011 Remaster)", "Song", When
        // title_key and title_variant are computed.
        [Fact(Skip = "pending T354 (STORY-376 AC2)")]
        public void AllFourShareTitleKeySong() => Assert.Fail("pending T354");

        [Fact(Skip = "pending T354 (STORY-376 AC2)")]
        public void TheVariantsAreFeatXLiveRemasterAndNull() => Assert.Fail("pending T354");
    }

    public sealed class ScenarioADuplicateGroup
    {
        // Given two ready rows, same artist_key/title_key, same variant, durations 200,000 and
        // 201,500 ms, When the near_duplicate pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC3)")]
        public void BothRowsHaveAnOpenFindingSharingOneGroupKey() => Assert.Fail("pending T374");
    }

    public sealed class ScenarioVersionsAreNotFlagged
    {
        // Given "Song" and "Song [Live]" by the same artist, When the pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC4)")]
        public void NoFindingIsOpened() => Assert.Fail("pending T374");

        [Fact(Skip = "pending T374 (STORY-376 AC4)")]
        public void TheLiveRowIsListedInTheOthersEvidenceVersions() => Assert.Fail("pending T374");
    }

    public sealed class ScenarioDurationTolerance
    {
        // Given same keys, durations 200,000 and 203,000 ms, When the pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC5)")]
        public void NoFindingIsOpened() => Assert.Fail("pending T374");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no filesystem, no excuse
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePassReadsNoFiles
    {
        // Given a library on an unreachable mount, When the pass runs.
        [Fact(Skip = "pending T374 (STORY-376 AC7)")]
        public void ThePassCompletesSqlOnly() => Assert.Fail("pending T374");

        [Fact(Skip = "pending T374 (STORY-376 AC7)")]
        public void FindingsOpenFromCatalogDataAlone() => Assert.Fail("pending T374");
    }
}
