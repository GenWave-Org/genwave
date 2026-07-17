// STORY-039 — Catalog write contract + schema (the schema half)
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection.
// Mirrors Story030_EnergyColumnsSchemaAndMigration but for the catalog-write columns
// (eligible, tags_edited_at) and db/05-catalog-writes-migration.sh.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureCatalogWriteColumnsSchemaAndMigration
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns (data_type, is_nullable, column_default) for the named column on library.media,
    /// or null when the column does not exist.
    /// </summary>
    static async Task<(string DataType, string IsNullable, string? ColumnDefault)?> QueryColumnAsync(
        DatabaseFixture db, string columnName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable, string? column_default)>(
            @"SELECT data_type, is_nullable, column_default
              FROM information_schema.columns
              WHERE table_schema = 'library'
                AND table_name   = 'media'
                AND column_name  = @col",
            new { col = columnName });

        return row == default ? null : (row.data_type, row.is_nullable, row.column_default);
    }

    /// <summary>
    /// Executes <c>db/05-catalog-writes-migration.sh</c> against the test database by piping
    /// the script file to <c>bash -s</c> inside the compose testdb container via the fixture.
    /// </summary>
    static void RunMigrationScript(DatabaseFixture db)
    {
        var scriptPath = Path.Combine(db.RepoRoot, "db", "05-catalog-writes-migration.sh");
        db.RunFileInContainer(scriptPath);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init (db/01-library.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioColumnsExistOnLibraryMediaAfterInit(DatabaseFixture db)
    {
        [Fact]
        public async Task EligibleColumnExistsAsBooleanNotNullDefaultTrue()
        {
            var col = await QueryColumnAsync(db, "eligible");
            Assert.NotNull(col);
            Assert.Equal("boolean", col.Value.DataType);
            Assert.Equal("NO", col.Value.IsNullable);
            Assert.NotNull(col.Value.ColumnDefault);
            Assert.Contains("true", col.Value.ColumnDefault, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TagsEditedAtColumnExistsAsTimestamptzNullable()
        {
            var col = await QueryColumnAsync(db, "tags_edited_at");
            Assert.NotNull(col);
            Assert.Equal("timestamp with time zone", col.Value.DataType);
            Assert.Equal("YES", col.Value.IsNullable);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/05-catalog-writes-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsColumnsInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationAddsBothColumns()
        {
            // Simulate a pre-migration state by dropping the two catalog-write columns.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                @"ALTER TABLE library.media
                    DROP COLUMN IF EXISTS eligible,
                    DROP COLUMN IF EXISTS tags_edited_at");

            // Verify they are gone.
            var before = await QueryColumnAsync(db, "eligible");
            Assert.Null(before);

            // Run the migration script.
            RunMigrationScript(db);

            // Assert both columns now exist with the expected shape.
            var eligibleCol = await QueryColumnAsync(db, "eligible");
            Assert.NotNull(eligibleCol);
            Assert.Equal("boolean", eligibleCol.Value.DataType);
            Assert.Equal("NO", eligibleCol.Value.IsNullable);
            Assert.NotNull(eligibleCol.Value.ColumnDefault);
            Assert.Contains("true", eligibleCol.Value.ColumnDefault, StringComparison.OrdinalIgnoreCase);

            var tagsEditedAtCol = await QueryColumnAsync(db, "tags_edited_at");
            Assert.NotNull(tagsEditedAtCol);
            Assert.Equal("timestamp with time zone", tagsEditedAtCol.Value.DataType);
            Assert.Equal("YES", tagsEditedAtCol.Value.IsNullable);
        }

        [Fact]
        public async Task ExistingRowsDefaultToEligibleTrue()
        {
            // Drop eligible, insert a row, run the migration, then assert the pre-existing row has eligible = true.
            await using var conn = await db.DataSource.OpenConnectionAsync();

            await conn.ExecuteAsync(
                "ALTER TABLE library.media DROP COLUMN IF EXISTS eligible, DROP COLUMN IF EXISTS tags_edited_at");

            // Insert a row while the column does not yet exist.
            await conn.ExecuteAsync(
                @"INSERT INTO library.media (path, format, size_bytes, mtime, state, library_id)
                  VALUES ('/test/eligible-default.flac', 'flac', 1024, now(), 'discovered', 1)");

            // Run the migration — adds eligible NOT NULL DEFAULT true.
            RunMigrationScript(db);

            // All pre-existing rows must have eligible = true.
            var eligible = await conn.ExecuteScalarAsync<bool>(
                "SELECT eligible FROM library.media WHERE path = '/test/eligible-default.flac'");
            Assert.True(eligible);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — idempotency
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationIsIdempotent(DatabaseFixture db)
    {
        [Fact]
        public async Task RerunningTheMigrationDoesNotErrorOrChangeAnything()
        {
            // Columns already exist (from 01-library.sh or a prior migration run).
            // Running the migration a second time must exit 0 and leave the schema unchanged.
            var before = await QueryColumnAsync(db, "eligible");
            Assert.NotNull(before);

            // First run — succeeds even if columns already exist (ADD COLUMN IF NOT EXISTS).
            RunMigrationScript(db);

            // Second run — must also succeed without error.
            RunMigrationScript(db);

            // Column shape must be unchanged.
            var after = await QueryColumnAsync(db, "eligible");
            Assert.NotNull(after);
            Assert.Equal(before.Value.DataType, after.Value.DataType);
            Assert.Equal(before.Value.IsNullable, after.Value.IsNullable);
        }
    }
}
