// gh-#113 — Hide unavailable rows from the catalog view (commit 1: the library half).
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. The
// live motivation: the demo library shrank 1500+ tracks → 50, and the catalog browse still showed
// 1500+ dead rows. Three behaviors land here:
//   • unavailable_since — stamped by the scan flip (MarkUnavailableAsync), cleared on resurrection
//     (MarkDiscoveredAsync, the gh-#112 path), never crept forward by a re-mark (COALESCE).
//   • ListAdminAsync hides state='unavailable' by default; IncludeUnavailable=true or an explicit
//     state filter reveals them (MediaQuery.HidesUnavailable, browse-only — bulk paths untouched).
//   • CountUnavailableAsync — the "N unavailable tracks hidden" figure, same filters + scope.
// The db/28 in-place migration facts mirror Story242_UpgradeChangesNothing's drop-then-migrate
// idiom; scan facts mirror Issue112_ScanResurrection's real-files harness.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureUnavailableHiddenAndStamped
{
    // ---------------------------------------------------------------------
    // Helpers (spec-local, the Story242 convention)
    // ---------------------------------------------------------------------

    static async Task<DateTime?> UnavailableSinceOfAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<DateTime?>(
            "select unavailable_since from library.media where id = @id", new { id });
    }

    static async Task SetUnavailableAsync(DatabaseFixture db, long id, string sinceInterval)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set state = 'unavailable', unavailable_since = now() - @since::interval where id = @id",
            new { id, since = sinceInterval });
    }

    /// <summary>Seeds a bare discovered row directly (browse facts don't need real files).</summary>
    static async Task<long> InsertRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime)
            values (@path, 'flac', 100, now())
            returning id
            """, new { path });
    }

    static readonly LibraryScope DefaultScope = new([1L]);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the scan stamps and clears unavailable_since
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioScanStampsTheTransition(DatabaseFixture db)
    {
        [Fact]
        public async Task A_row_flipped_unavailable_by_the_scan_carries_a_stamp_cleared_again_on_resurrection()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            var parking = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac", seconds: 2.0);
                var parked = Path.Combine(parking, "a.flac");
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 1);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));
                Assert.Null(await UnavailableSinceOfAsync(db, id));

                // Gone → unavailable, stamped.
                File.Move(path, parked);
                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));
                Assert.NotNull(await UnavailableSinceOfAsync(db, id));

                // Restored (the gh-#112 resurrection path) → stamp cleared with the state flip,
                // so the revived row can never look purge-eligible to the gh-#113 age filter.
                File.Move(parked, path);
                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));
                Assert.Null(await UnavailableSinceOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
                Directory.Delete(parking, recursive: true);
            }
        }

        [Fact]
        public async Task Remarking_an_already_unavailable_row_keeps_the_earliest_stamp()
        {
            // COALESCE in MarkUnavailableAsync: "unavailable since" must never creep forward
            // while a row stays gone — the purge age filter reads this column.
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await InsertRowAsync(db, "/media/gone.flac");

            await SetUnavailableAsync(db, id, "10 days");
            var original = await UnavailableSinceOfAsync(db, id);

            await repo.MarkUnavailableAsync([id], CancellationToken.None);

            Assert.Equal(original, await UnavailableSinceOfAsync(db, id));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the admin browse hides unavailable rows by default
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBrowseHidesUnavailableByDefault(DatabaseFixture db)
    {
        async Task<(long readyId, long unavailableId)> SeedAsync()
        {
            await db.ResetAsync();
            var readyId = await InsertRowAsync(db, "/media/here.flac");
            var unavailableId = await InsertRowAsync(db, "/media/gone.flac");
            await SetUnavailableAsync(db, unavailableId, "1 day");
            return (readyId, unavailableId);
        }

        [Fact]
        public async Task The_default_browse_excludes_unavailable_rows_from_items_and_total()
        {
            var (readyId, _) = await SeedAsync();
            var repo = Harness.Repo(db);

            var page = await repo.ListAdminAsync(DefaultScope, new MediaQuery(), CancellationToken.None);

            var row = Assert.Single(page.Items);
            Assert.Equal(readyId.ToString(), row.MediaId);
            Assert.Equal(1, page.Total);
        }

        [Fact]
        public async Task IncludeUnavailable_true_reveals_the_hidden_rows()
        {
            await SeedAsync();
            var repo = Harness.Repo(db);

            var page = await repo.ListAdminAsync(
                DefaultScope, new MediaQuery(IncludeUnavailable: true), CancellationToken.None);

            Assert.Equal(2, page.Total);
        }

        [Fact]
        public async Task An_explicit_state_filter_still_reaches_unavailable_rows()
        {
            // state=unavailable must match its rows, never be cancelled out by the default hiding.
            var (_, unavailableId) = await SeedAsync();
            var repo = Harness.Repo(db);

            var page = await repo.ListAdminAsync(
                DefaultScope, new MediaQuery(State: "unavailable"), CancellationToken.None);

            var row = Assert.Single(page.Items);
            Assert.Equal(unavailableId.ToString(), row.MediaId);
        }

        [Fact]
        public async Task Bulk_writes_sharing_the_where_builder_still_reach_unavailable_rows()
        {
            // The browse/bulk asymmetry is deliberate (IAdminMediaQuery doc): hiding is a view
            // default, not an operator filter — an empty-filter eligibility sweep must keep
            // touching every in-scope row, unavailable included.
            await SeedAsync();
            var repo = Harness.Repo(db);

            var affected = await repo.SetEligibilityAsync(
                new MediaQuery(), eligible: false, DefaultScope, CancellationToken.None);

            Assert.Equal(2, affected);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the hidden-row count
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioHiddenRowsAreCounted(DatabaseFixture db)
    {
        [Fact]
        public async Task The_count_names_every_unavailable_row_the_other_filters_match()
        {
            await db.ResetAsync();
            await InsertRowAsync(db, "/media/here.flac");
            var goneA = await InsertRowAsync(db, "/media/gone-a.flac");
            var goneB = await InsertRowAsync(db, "/media/gone-b.flac");
            await SetUnavailableAsync(db, goneA, "1 day");
            await SetUnavailableAsync(db, goneB, "2 days");
            var repo = Harness.Repo(db);

            var hidden = await repo.CountUnavailableAsync(DefaultScope, new MediaQuery(), CancellationToken.None);

            Assert.Equal(2, hidden);
        }

        [Fact]
        public async Task The_count_respects_the_browse_filters()
        {
            // "N hidden" must answer "how many MORE rows would THIS browse show" — a q filter
            // that matches neither unavailable row counts zero, not the library-wide figure.
            await db.ResetAsync();
            var gone = await InsertRowAsync(db, "/media/gone.flac");
            await SetUnavailableAsync(db, gone, "1 day");
            var repo = Harness.Repo(db);

            var hidden = await repo.CountUnavailableAsync(
                DefaultScope, new MediaQuery(Q: "no-such-title"), CancellationToken.None);

            Assert.Equal(0, hidden);
        }

        [Fact]
        public async Task An_empty_scope_short_circuits_to_zero()
        {
            await db.ResetAsync();
            var gone = await InsertRowAsync(db, "/media/gone.flac");
            await SetUnavailableAsync(db, gone, "1 day");
            var repo = Harness.Repo(db);

            var hidden = await repo.CountUnavailableAsync(
                new LibraryScope([]), new MediaQuery(), CancellationToken.None);

            Assert.Equal(0, hidden);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/28)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioInPlaceMigration(DatabaseFixture db)
    {
        static void RunMigrationScript(DatabaseFixture db) =>
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "28-unavailable-since-migration.sh"));

        static async Task DropColumnAsync(DatabaseFixture db)
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("alter table library.media drop column if exists unavailable_since");
        }

        static async Task SetStateAsync(DatabaseFixture db, long id, string state)
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "update library.media set state = @state where id = @id", new { id, state });
        }

        [Fact]
        public async Task Migrating_a_pre_gh113_database_adds_the_column_and_backfills_already_unavailable_rows()
        {
            // Simulate the upgrade that motivated gh-#113: rows already unavailable before the
            // column existed (the demo box's 1500-row shrink). They are stamped now() — purge-
            // eligible only after they age past the window from today, never retroactively.
            await db.ResetAsync();
            await DropColumnAsync(db);
            var gone = await InsertRowAsync(db, "/media/gone.flac");
            var here = await InsertRowAsync(db, "/media/here.flac");
            await SetStateAsync(db, gone, "unavailable");

            RunMigrationScript(db);

            Assert.NotNull(await UnavailableSinceOfAsync(db, gone));
            Assert.Null(await UnavailableSinceOfAsync(db, here));
        }

        [Fact]
        public async Task Rerunning_the_migration_changes_nothing()
        {
            // Idempotency: ADD COLUMN IF NOT EXISTS, and the backfill only ever touches NULL
            // stamps — a real stamp survives any number of re-runs.
            await db.ResetAsync();
            var gone = await InsertRowAsync(db, "/media/gone.flac");
            await SetUnavailableAsync(db, gone, "10 days");
            var original = await UnavailableSinceOfAsync(db, gone);

            RunMigrationScript(db);

            Assert.Equal(original, await UnavailableSinceOfAsync(db, gone));
        }
    }
}
