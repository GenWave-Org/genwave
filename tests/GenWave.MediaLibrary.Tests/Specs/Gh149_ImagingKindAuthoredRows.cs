// gh-#149 — Authored segments carry a Station Imaging content kind (storage + read half).
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection.
// library.media.imaging_kind: stamped by InsertAuthoredAsync on every authored insert ('liner'
// by default — today's behavior), NULL for scanned rows and pre-#149 authored rows, projected by
// the admin queries (AdminMediaDto.ImagingKind) so the Station Imaging page can badge and filter.
// METADATA-ONLY: playout/safe-track selection never reads the column — no playout fact changes
// here. The db/30 in-place migration facts mirror Gh113_UnavailableHiddenAndStamped's
// drop-then-migrate idiom (itself the Story242 idiom).

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureImagingKindAuthoredRows
{
    // ---------------------------------------------------------------------
    // Helpers (spec-local, the Story242 convention)
    // ---------------------------------------------------------------------

    static readonly LibraryScope DefaultScope = new([1L]);

    static async Task<string?> ImagingKindOfAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "select imaging_kind from library.media where id = @id", new { id });
    }

    /// <summary>Seeds a bare discovered row directly — the shape a scanned file lands in.</summary>
    static async Task<long> InsertScannedRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime)
            values (@path, 'flac', 100, now())
            returning id
            """, new { path });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — author → stored → listed round-trip
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioKindRoundTripsFromInsertToProjection(DatabaseFixture db)
    {
        [Fact]
        public async Task AJingleInsertStoresTheJingleToken()
        {
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(
                Harness.AuthoredInsert(kind: ImagingKind.Jingle), CancellationToken.None);

            Assert.Equal("jingle", await ImagingKindOfAsync(db, id));
        }

        [Fact]
        public async Task TheStoredKindIsProjectedByTheByIdLookup()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            IAuthoredCatalogWriter writer = repo;

            var id = await writer.InsertAuthoredAsync(
                Harness.AuthoredInsert(kind: ImagingKind.StationId), CancellationToken.None);

            var (dto, _) = (await repo.GetByIdWithLibraryAsync(id, CancellationToken.None))
                ?? throw new InvalidOperationException("expected the authored row to round-trip");
            Assert.Equal("station_id", dto.ImagingKind);
        }

        [Fact]
        public async Task TheStoredKindIsProjectedByTheAdminBrowse()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            IAuthoredCatalogWriter writer = repo;

            await writer.InsertAuthoredAsync(
                Harness.AuthoredInsert(kind: ImagingKind.Promo), CancellationToken.None);

            var page = await repo.ListAdminAsync(DefaultScope, new MediaQuery(), CancellationToken.None);

            var row = Assert.Single(page.Items);
            Assert.Equal("promo", row.ImagingKind);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — defaults: Liner for authored, NULL for scanned
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDefaultsPreserveTodaysBehavior(DatabaseFixture db)
    {
        [Fact]
        public async Task AnAuthoredInsertWithoutAKindStoresLiner()
        {
            // Harness.AuthoredInsert() names no kind — the AuthoredMediaInsert record default
            // (Liner, gh-#149) is what lands, exactly what the boot seed and pre-kind callers get.
            await db.ResetAsync();
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(Harness.AuthoredInsert(), CancellationToken.None);

            Assert.Equal("liner", await ImagingKindOfAsync(db, id));
        }

        [Fact]
        public async Task AScannedRowCarriesNoKindAtAll()
        {
            // Scanned music is not Station Imaging — the discovery/enrichment paths never touch
            // the column, so it stays NULL (and the admin projection surfaces that NULL as-is).
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await InsertScannedRowAsync(db, "/media/song.flac");

            Assert.Null(await ImagingKindOfAsync(db, id));
            var page = await repo.ListAdminAsync(DefaultScope, new MediaQuery(), CancellationToken.None);
            var row = Assert.Single(page.Items);
            Assert.Equal(id.ToString(), row.MediaId);
            Assert.Null(row.ImagingKind);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the check constraint refuses unknown tokens
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnknownTokensAreRefusedBySchema(DatabaseFixture db)
    {
        [Fact]
        public async Task AnOutOfVocabularyKindViolatesTheCheckConstraint()
        {
            await db.ResetAsync();
            var id = await InsertScannedRowAsync(db, "/media/song.flac");
            await using var conn = await db.DataSource.OpenConnectionAsync();

            var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                conn.ExecuteAsync(
                    "update library.media set imaging_kind = 'sweeper' where id = @id", new { id }));

            Assert.Equal("23514", ex.SqlState); // check_violation
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/30)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioInPlaceMigration(DatabaseFixture db)
    {
        static void RunMigrationScript(DatabaseFixture db) =>
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "30-imaging-kind-migration.sh"));

        static async Task DropColumnAsync(DatabaseFixture db)
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("alter table library.media drop column if exists imaging_kind");
        }

        static async Task SetKindAsync(DatabaseFixture db, long id, string kind)
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "update library.media set imaging_kind = @kind where id = @id", new { id, kind });
        }

        [Fact]
        public async Task MigratingAPreGh149DatabaseAddsTheColumnWithNoBackfill()
        {
            // Pre-#149 rows (authored or scanned alike) come back NULL — deliberately no backfill:
            // nothing structurally distinguishes an old authored row from a scanned one, so the
            // display layer defaults NULL to Liner instead of the schema guessing.
            await db.ResetAsync();
            await DropColumnAsync(db);
            var preExisting = await InsertScannedRowAsync(db, "/media/pre-existing.flac");

            RunMigrationScript(db);

            Assert.Null(await ImagingKindOfAsync(db, preExisting));
        }

        [Fact]
        public async Task RerunningTheMigrationChangesNothing()
        {
            // Idempotency: ADD COLUMN IF NOT EXISTS, no backfill — a stored kind survives re-runs.
            await db.ResetAsync();
            var id = await InsertScannedRowAsync(db, "/media/kept.flac");
            await SetKindAsync(db, id, "jingle");

            RunMigrationScript(db);

            Assert.Equal("jingle", await ImagingKindOfAsync(db, id));
        }

        [Fact]
        public async Task TheMigratedColumnAcceptsAuthoredInsertsImmediately()
        {
            // The upgraded schema must be byte-compatible with what InsertAuthoredAsync writes.
            await db.ResetAsync();
            await DropColumnAsync(db);
            RunMigrationScript(db);
            IAuthoredCatalogWriter writer = Harness.Repo(db);

            var id = await writer.InsertAuthoredAsync(
                Harness.AuthoredInsert(kind: ImagingKind.StationId), CancellationToken.None);

            Assert.Equal("station_id", await ImagingKindOfAsync(db, id));
        }
    }
}
