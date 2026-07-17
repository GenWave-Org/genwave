// STORY-030 — Schema: intro_energy, outro_energy, energy_analyzed_at columns
//
// BDD specification — xUnit. Integration: hits real Postgres via DatabaseCollection.
// Mirrors Story017_CueColumnsSchemaAndMigration but for the energy columns.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureEnergyColumnsSchemaAndMigration
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns (data_type, is_nullable) for the named column on library.media,
    /// or null when the column does not exist.
    /// </summary>
    static async Task<(string DataType, string IsNullable)?> QueryColumnAsync(
        DatabaseFixture db, string columnName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        // information_schema.columns is accessible to library_svc for its own schema objects.
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            @"SELECT data_type, is_nullable
              FROM information_schema.columns
              WHERE table_schema = 'library'
                AND table_name   = 'media'
                AND column_name  = @col",
            new { col = columnName });

        // QuerySingleOrDefaultAsync returns default for a value-tuple when nothing is found.
        return row == default ? null : (row.data_type, row.is_nullable);
    }

    /// <summary>
    /// Executes the real <c>db/04-energy-migration.sh</c> against the test database by piping
    /// the script file to <c>bash -s</c> inside the compose testdb container via the fixture.
    /// The fixture owns container identity; the script path is resolved from the repo root the
    /// fixture already discovered, so the test never hardcodes a container name or SQL copy.
    /// </summary>
    static void RunMigrationScript(DatabaseFixture db)
    {
        var scriptPath = Path.Combine(db.RepoRoot, "db", "04-energy-migration.sh");
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
        public async Task IntroEnergyColumnExistsAsDoublePrecisionNullable()
        {
            var col = await QueryColumnAsync(db, "intro_energy");
            Assert.NotNull(col);
            Assert.Equal("double precision", col.Value.DataType);
            Assert.Equal("YES", col.Value.IsNullable);
        }

        [Fact]
        public async Task OutroEnergyColumnExistsAsDoublePrecisionNullable()
        {
            var col = await QueryColumnAsync(db, "outro_energy");
            Assert.NotNull(col);
            Assert.Equal("double precision", col.Value.DataType);
            Assert.Equal("YES", col.Value.IsNullable);
        }

        [Fact]
        public async Task EnergyAnalyzedAtColumnExistsAsTimestamptzNullable()
        {
            var col = await QueryColumnAsync(db, "energy_analyzed_at");
            Assert.NotNull(col);
            Assert.Equal("timestamp with time zone", col.Value.DataType);
            Assert.Equal("YES", col.Value.IsNullable);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/04-energy-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsColumnsInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationAddsTheThreeEnergyColumns()
        {
            // The test database is already initialised with 01-library.sh (which now includes the
            // energy columns). To simulate a pre-energy DB we drop the three columns, run the
            // migration, and assert they come back with the correct types and nullability.

            await using var conn = await db.DataSource.OpenConnectionAsync();

            // Simulate a pre-migration state by dropping the energy columns.
            await conn.ExecuteAsync(
                @"ALTER TABLE library.media
                    DROP COLUMN IF EXISTS intro_energy,
                    DROP COLUMN IF EXISTS outro_energy,
                    DROP COLUMN IF EXISTS energy_analyzed_at");

            // Verify they are gone.
            var before = await QueryColumnAsync(db, "intro_energy");
            Assert.Null(before);

            // Run the migration script.
            RunMigrationScript(db);

            // Assert all three columns now exist with the expected shape.
            var introCol = await QueryColumnAsync(db, "intro_energy");
            Assert.NotNull(introCol);
            Assert.Equal("double precision", introCol.Value.DataType);
            Assert.Equal("YES", introCol.Value.IsNullable);

            var outroCol = await QueryColumnAsync(db, "outro_energy");
            Assert.NotNull(outroCol);
            Assert.Equal("double precision", outroCol.Value.DataType);
            Assert.Equal("YES", outroCol.Value.IsNullable);

            var analyzedAtCol = await QueryColumnAsync(db, "energy_analyzed_at");
            Assert.NotNull(analyzedAtCol);
            Assert.Equal("timestamp with time zone", analyzedAtCol.Value.DataType);
            Assert.Equal("YES", analyzedAtCol.Value.IsNullable);
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
            var before = await QueryColumnAsync(db, "intro_energy");
            Assert.NotNull(before);

            // First run — succeeds even if columns already exist (ADD COLUMN IF NOT EXISTS).
            RunMigrationScript(db);

            // Second run — must also succeed without error.
            RunMigrationScript(db);

            // Column shape must be unchanged.
            var after = await QueryColumnAsync(db, "intro_energy");
            Assert.NotNull(after);
            Assert.Equal(before.Value.DataType, after.Value.DataType);
            Assert.Equal(before.Value.IsNullable, after.Value.IsNullable);
        }
    }
}
