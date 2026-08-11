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
    public sealed class ScenarioScopedQuery
    {
        [Fact(Skip = "Pending (T250)")]
        public void ScopedRowsPreferredWhenAShowIsActive()
        {
            // Given ready station_id rows both scoped to show 7 and unscoped
            // When  the pool query runs for show 7
            // Then  only scoped rows are candidates in the scoped-first pass
        }

        [Fact(Skip = "Pending (T250)")]
        public void UnscopedFallbackWhenNoScopedRows()
        {
            // Given only unscoped ready station_id rows
            // When  the pool query runs for a show
            // Then  the unscoped fallback pass serves them (the station-wide pool survives)
        }

        [Fact(Skip = "Pending (T250)")]
        public void ScopedRowsNeverServeOutsideTheirShow()
        {
            // Given a ready row scoped to show 7
            // When  the pool query runs with no show (or another show) active
            // Then  the scoped row is not a candidate — scoped means scoped (F117.1)
        }
    }

    // -----------------------------------------------------------------
    // Helpers (spec-local, the Gh149_ImagingKindAuthoredRows convention)
    // -----------------------------------------------------------------

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
