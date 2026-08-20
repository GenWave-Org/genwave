// STORY-355 — The time signal tells the truth late (SPEC F141.1, PLAN T326) — the DATA migration half.
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. Review
// finding F1 (PLAN T326): the F141.1 key rename (Station:Imaging:TimeAnnouncementStaleMinutes →
// TimeAnnouncementBudgetSeconds) orphans any operator's persisted station.settings row at boot with no
// WARN (StationSettingsConfigurationProvider.Load skips any key not on StationSettingsAllowlist,
// gh-#412), invisible on GET /api/settings, and un-deletable through the product.
// db/39-time-announcement-budget-migration.sh converts the row in place; this file pins that script's
// behavior the same "drop/set prior state, run the migration script, assert the resulting row" shape
// Story242_UpgradeChangesNothing.cs's own family already established for db/27's settings-key
// retirement, scoped down to a single-row VALUE conversion (rename + unit convert) rather than a
// whole-table seed-and-delete.
//
// Fresh installs never run this script's rename at all: db/06-station-settings-migration.sh only
// creates the EMPTY station.settings table — no row for this (or any) key is ever seeded there. A row
// for the old key exists ONLY when an operator actually saved a custom TimeAnnouncementStaleMinutes
// value through PUT /api/settings before this release; ScenarioNoPriorRow below is that fresh-install
// (and "never touched this setting") case.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTimeAnnouncementBudgetMigration
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    const string OldKey = "Station:Imaging:TimeAnnouncementStaleMinutes";
    const string NewKey = "Station:Imaging:TimeAnnouncementBudgetSeconds";

    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "39-time-announcement-budget-migration.sh"));

    static async Task InsertOldRowAsync(DatabaseFixture db, int minutes)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into station.settings (key, value) values (@key, to_jsonb(@minutes::int))",
            new { key = OldKey, minutes });
    }

    static async Task<bool> RowExistsAsync(DatabaseFixture db, string key)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from station.settings where key = @key)", new { key });
    }

    static async Task<int> ReadIntValueAsync(DatabaseFixture db, string key)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select (value #>> '{}')::int from station.settings where key = @key", new { key });
    }

    static async Task<int> CountRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.settings");
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — an operator's persisted override survives the rename, converted
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAPersistedOverrideIsConvertedInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task TheNewKeyHoldsTheValueMultipliedBySixty()
        {
            // Given an operator once saved 10 (minutes) under the retired key...
            await db.ResetSettingsAsync();
            await InsertOldRowAsync(db, 10);

            // When the migration runs...
            RunMigrationScript(db);

            // Then the new key holds 600 (seconds) — F141.1's own unit change.
            Assert.Equal(600, await ReadIntValueAsync(db, NewKey));
        }

        [Fact]
        public async Task TheOldKeyIsGone()
        {
            await db.ResetSettingsAsync();
            await InsertOldRowAsync(db, 10);

            RunMigrationScript(db);

            Assert.False(await RowExistsAsync(db, OldKey));
        }

        [Fact]
        public async Task TheRowCountIsUnchangedARenameNotAnAddition()
        {
            await db.ResetSettingsAsync();
            await InsertOldRowAsync(db, 10);

            RunMigrationScript(db);

            Assert.Equal(1, await CountRowsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no operator override ever existed (the fresh-install / never-touched case)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNoPriorRow(DatabaseFixture db)
    {
        [Fact]
        public async Task RunningAgainstAnEmptyTableIsANoOp()
        {
            // Given no row at all — db/06's own fresh-install shape (this file's own header note: it
            // seeds nothing for this key)...
            await db.ResetSettingsAsync();

            // When the migration runs...
            RunMigrationScript(db);

            // Then nothing is created and nothing errors.
            Assert.Equal(0, await CountRowsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — migration house rule: a second run is a safe no-op (no double-multiply)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIdempotentReRun(DatabaseFixture db)
    {
        [Fact]
        public async Task SecondRunDoesNotDoubleConvertTheValue()
        {
            // Given a first run that already converted 10 minutes to 600 seconds...
            await db.ResetSettingsAsync();
            await InsertOldRowAsync(db, 10);
            RunMigrationScript(db);
            Assert.Equal(600, await ReadIntValueAsync(db, NewKey));

            // When the migration runs again (RunFileInContainer throws on a nonzero exit code, so
            // simply reaching the assertions below is itself proof this run exited 0)...
            RunMigrationScript(db);

            // Then the value is untouched — 600, not 36000 — the old key stays gone, and there is
            // still exactly one row.
            Assert.Equal(600, await ReadIntValueAsync(db, NewKey));
            Assert.False(await RowExistsAsync(db, OldKey));
            Assert.Equal(1, await CountRowsAsync(db));
        }
    }
}
