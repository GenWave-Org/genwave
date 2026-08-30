// STORY-377 — Stale metadata and shelf-dust (SPEC F153.6–F153.7 · PLAN T375)
//
// BDD specification — xUnit. PENDING until T375. Arrange sketch: DatabaseFixture — seed ready
// rows through MediaRepository with the enrichment/tag shapes each AC needs (blank artist, the
// "Track NN" title family, year/mood miss timestamps, tags_edited_at, discovered_at ages, an
// existing library.media_rotation row or its absence), run the stale_metadata/shelf_dust passes,
// and read library.rot_finding back.
namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureStaleMetadataAndShelfDust
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — fixable rows and forgotten rows, surfaced
    // ---------------------------------------------------------------------

    public sealed class ScenarioBlankArtist
    {
        // Given a ready row with artist null, When the stale_metadata pass runs.
        [Fact(Skip = "pending T375 (STORY-377 AC1)")]
        public void AnOpenFindingNamesArtistInEvidenceFields() => Assert.Fail("pending T375");
    }

    public sealed class ScenarioTheTrackNnFamily
    {
        // Given titles "Track 07", "track 7", "Track07", When the pass runs.
        [Fact(Skip = "pending T375 (STORY-377 AC2)")]
        public void EachHasAFindingNamingTitle() => Assert.Fail("pending T375");
    }

    public sealed class ScenarioEnrichmentMisses
    {
        // Given a row with year null and year_lookup_missed_at set, moods null and
        // mood_tag_missed_at set, When the pass runs.
        [Fact(Skip = "pending T375 (STORY-377 AC3)")]
        public void OneFindingNamesYearAndMoods() => Assert.Fail("pending T375");
    }

    public sealed class ScenarioOperatorEditsAreExempt
    {
        // Given a row with tags_edited_at set and artist deliberately blank, When the pass
        // runs.
        [Fact(Skip = "pending T375 (STORY-377 AC4)")]
        public void NoFindingNamesArtist() => Assert.Fail("pending T375");
    }

    public sealed class ScenarioShelfDust
    {
        // Given a playable row discovered 91 days ago with no ledger row, When the shelf_dust
        // pass runs.
        [Fact(Skip = "pending T375 (STORY-377 AC5)")]
        public void AnOpenShelfDustFindingExists() => Assert.Fail("pending T375");
    }

    public sealed class ScenarioShelfDustExcludesUnreachable
    {
        // Given the same row with an open unreachable finding, When the pass runs.
        [Fact(Skip = "pending T375 (STORY-377 AC6)")]
        public void NoShelfDustFindingIsOpened() => Assert.Fail("pending T375");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a fresh row earns no finding
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFreshRowIsNotDust
    {
        // Given a playable row discovered yesterday, When the pass runs.
        [Fact(Skip = "pending T375 (STORY-377 AC7)")]
        public void NoFindingIsOpened() => Assert.Fail("pending T375");
    }
}
