// STORY-117 — Copy-writer seam + tag widening land without behavior change
//
// BDD specification — xUnit (real Postgres via the shared DatabaseFixture harness).
// The catalog mapping stamps MediaItem.Album/Genre/Year so the blurb prompt has tags
// beyond title/artist (F34.7). Proves the REAL production narrowing: IMediaCatalog's
// MediaReference (repository mapping) through MediaReferenceExtensions.ToMediaItem() (the
// shared narrowing every music-selection call site — Orchestrator, RandomSelectionProvider,
// the safe-track endpoint — uses), not a dead/orphaned mapping method.
// See docs/PLAN.md Epic T.

using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMediaItemTagWidening
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Reads the row back through the real <see cref="IMediaCatalog"/> read path and narrows
    /// it via the shared <see cref="MediaReferenceExtensions.ToMediaItem"/> — the same production
    /// mapping Orchestrator/RandomSelectionProvider/InternalEndpoints use (SPEC F34.7).</summary>
    static async Task<MediaItem> LoadItemAsync(DatabaseFixture db, long id)
    {
        var repo = Harness.Repo(db);
        var catalog = (IMediaCatalog)repo;
        var reference = await catalog.GetByIdAsync(
            new LibraryScope([1L]), id.ToString(CultureInfo.InvariantCulture), CancellationToken.None);
        return reference is null
            ? throw new InvalidOperationException($"arrange failure: row {id} not found by GetByIdAsync")
            : reference.ToMediaItem();
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — tags ride the mapping
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMappingARowWithFullTags(DatabaseFixture db)
    {
        [Fact]
        public async Task AlbumIsStampedOnTheMediaItem()
        {
            // Row with album populated → MediaItem.Album carries it (F34.7, AC2).
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/tags-full-album.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var item = await LoadItemAsync(db, id);

            Assert.Equal("al", item.Album);
        }

        [Fact]
        public async Task GenreIsStampedOnTheMediaItem()
        {
            // (F34.7, AC2).
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/tags-full-genre.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var item = await LoadItemAsync(db, id);

            Assert.Equal("g", item.Genre);
        }

        [Fact]
        public async Task YearIsStampedOnTheMediaItem()
        {
            // (F34.7, AC2).
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/tags-full-year.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var item = await LoadItemAsync(db, id);

            Assert.Equal(2020, item.Year);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — sparse tags stay null, nothing breaks
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMappingARowWithNullTags(DatabaseFixture db)
    {
        // A freshly discovered row carries no tags until enrichment writes them — no throw, no ""
        // coercion, just null (F34.7, AC2).

        [Fact]
        public async Task NullAlbumColumnStampsNullNotAPlaceholder()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/tags-null-album.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

            var item = await LoadItemAsync(db, id);

            Assert.Null(item.Album);
        }

        [Fact]
        public async Task NullGenreColumnStampsNullNotAPlaceholder()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/tags-null-genre.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

            var item = await LoadItemAsync(db, id);

            Assert.Null(item.Genre);
        }

        [Fact]
        public async Task NullYearColumnStampsNullNotAPlaceholder()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/tags-null-year.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

            var item = await LoadItemAsync(db, id);

            Assert.Null(item.Year);
        }
    }
}
