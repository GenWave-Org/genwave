// STORY-309 — Show-branded idents (F117) — pool-query + authored-scope half
//
// BDD specification — xUnit. library.media.show_id lands at T238, the scoped query at T250 (still
// PENDING below), the authored-insert scope at T246 — implemented here, live-PG (Category=Integration,
// shared DatabaseFixture), mirroring Gh149_ImagingKindAuthoredRows.cs's own insert/project pattern
// (that file's own precedent for imaging_kind, applied here to show_id). No FK across the
// schema/grant boundary (the db/22 precedent) — station.show lives in a different schema this
// project has no grant into, so these facts stamp an arbitrary show id with no need for a real
// station.show row to exist, exactly like Story310_ShowStamp.cs's own booth_log.show_id facts.
// The drain-preference half lives in Orchestration.Tests/Story309_ShowIdentDrain.cs.

namespace GenWave.MediaLibrary.Tests.Specs;

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Xunit;

public static class FeatureScopedImagingPool
{
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioScopedQuery(DatabaseFixture db)
    {
        [Fact]
        public async Task ScopedRowsPreferredWhenAShowIsActive()
        {
            // Given ready station_id rows both scoped to show 7 and unscoped
            await db.ResetAsync();
            var scopedId = await InsertReadyStationIdAsync(db, "/imaging/scoped.wav", showId: 7);
            await InsertReadyStationIdAsync(db, "/imaging/unscoped.wav");

            var catalog = (IMediaCatalog)Harness.Repo(db);

            // When the pool query runs for show 7 (repeatedly — proving it, not luck)...
            for (var i = 0; i < 10; i++)
            {
                var result = await catalog.GetRandomReadyByImagingKindAsync(
                    DefaultScope, ImagingKind.StationId, showId: 7, CancellationToken.None);

                // Then only scoped rows are candidates in the scoped-first pass
                Assert.NotNull(result);
                Assert.Equal(scopedId.ToString(), result.MediaId);
            }
        }

        [Fact]
        public async Task UnscopedFallbackWhenNoScopedRows()
        {
            // Given only unscoped ready station_id rows
            await db.ResetAsync();
            var unscopedId = await InsertReadyStationIdAsync(db, "/imaging/unscoped-only.wav");

            var catalog = (IMediaCatalog)Harness.Repo(db);

            // When the pool query runs for a show...
            var result = await catalog.GetRandomReadyByImagingKindAsync(
                DefaultScope, ImagingKind.StationId, showId: 7, CancellationToken.None);

            // Then the unscoped fallback pass serves them (the station-wide pool survives)
            Assert.NotNull(result);
            Assert.Equal(unscopedId.ToString(), result.MediaId);
        }

        [Fact]
        public async Task ScopedRowsNeverServeOutsideTheirShow()
        {
            // Given a ready row scoped to show 7
            await db.ResetAsync();
            await InsertReadyStationIdAsync(db, "/imaging/scoped-only.wav", showId: 7);

            var catalog = (IMediaCatalog)Harness.Repo(db);

            // When the pool query runs with no show active...
            var noShow = await catalog.GetRandomReadyByImagingKindAsync(
                DefaultScope, ImagingKind.StationId, showId: null, CancellationToken.None);
            // ...or another show active...
            var otherShow = await catalog.GetRandomReadyByImagingKindAsync(
                DefaultScope, ImagingKind.StationId, showId: 99, CancellationToken.None);

            // Then the scoped row is not a candidate — scoped means scoped (F117.1)
            Assert.Null(noShow);
            Assert.Null(otherShow);
        }
    }

    // -----------------------------------------------------------------
    // Helpers (spec-local, the Gh149_ImagingKindAuthoredRows/Story301_ImagingPoolQuery convention)
    // -----------------------------------------------------------------

    static readonly LibraryScope DefaultScope = new([1L]);

    /// <summary>Authors a ready <c>station_id</c> row at <paramref name="path"/> (unique path per
    /// row — <c>library.media.path</c> is unique), optionally scoped to <paramref name="showId"/>.</summary>
    static async Task<long> InsertReadyStationIdAsync(DatabaseFixture db, string path, long? showId = null) =>
        await ((IAuthoredCatalogWriter)Harness.Repo(db)).InsertAuthoredAsync(
            Harness.AuthoredInsert(path: path, kind: ImagingKind.StationId, showId: showId), CancellationToken.None);

    static async Task<long?> ShowIdOfAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long?>(
            "select show_id from library.media where id = @id", new { id });
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAuthoringWithScope(DatabaseFixture db)
    {
        [Fact]
        public async Task InsertAuthoredAcceptsAShowScope()
        {
            // Given the authoring path with a show scope selected...
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);
            const long showId = 7;

            // When InsertAuthoredAsync runs...
            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(showId: showId), CancellationToken.None);

            // Then the row lands with show_id set.
            Assert.Equal(showId, await ShowIdOfAsync(db, id));
        }

        [Fact]
        public async Task AnInsertWithNoScopeNamedStaysStationWideNull()
        {
            // Harness.AuthoredInsert() names no showId — the AuthoredMediaInsert record default
            // (null, F117.1) is what lands, exactly what every pre-F117 caller (including the boot
            // seed) still gets.
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(), CancellationToken.None);

            Assert.Null(await ShowIdOfAsync(db, id));
        }

        [Fact]
        public async Task TheStoredScopeIsProjectedByTheByIdLookup()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            IAuthoredCatalogWriter writer = repo;
            const long showId = 12;

            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(showId: showId), CancellationToken.None);

            var (dto, _) = (await repo.GetByIdWithLibraryAsync(id, CancellationToken.None))
                ?? throw new InvalidOperationException("expected the authored row to round-trip");
            Assert.Equal(showId, dto.ShowId);
        }

        [Fact]
        public async Task TheStoredScopeIsProjectedByTheAdminBrowse()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            IAuthoredCatalogWriter writer = repo;
            const long showId = 34;

            await writer.InsertAuthoredAsync(Harness.AuthoredInsert(showId: showId), CancellationToken.None);

            var page = await repo.ListAdminAsync(new LibraryScope([1L]), new MediaQuery(), CancellationToken.None);

            var row = Assert.Single(page.Items);
            Assert.Equal(showId, row.ShowId);
        }
    }
}
