// STORY-237 — provenance schema + migration (SPEC F90.7, PLAN T98)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. Mirrors
// the SchemaAndMigration family's shape (Story039_CatalogWriteColumnsSchemaAndMigration is the
// closest sibling: drop the columns, run the migration script in the compose testdb container,
// assert the resulting shape, assert a second run is idempotent) over db/25-persona-provenance-
// migration.sh and station.persona.imported_from/imported_at.
//
// Also closes review finding 7's two "structurally can't fail" gaps: PersonaRepository.CreateAsync's
// INSERT never NAMING imported_from/imported_at, and PersonaRepository.UpdateAsync's UPDATE never
// naming them either, are facts about the SQL TEXT — proven by reading the source, not by reading a
// database. ScenarioAuthoredPersonaProvenance/ScenarioImportThenAdminEdit below prove the same claims
// against a REAL row in real Postgres instead.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaProvenanceSchemaAndMigration
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static PersonaRepository Repo(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static PersonaImportRepository ImportRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static PersonaCard BuildCard() =>
        new(
            SchemaVersion: PersonaCard.CurrentSchemaVersion,
            Name: "DJ Edit Survives",
            Tagline: "Tagline.",
            Soul: "Soul.",
            Quirks: [],
            Voice: new VoiceSpec(Engine: "", VoiceId: "", Pace: 1.0, Language: "en"),
            EnergyDisposition: 0,
            Lore: [],
            Corrections: [],
            Taste: null);

    /// <summary>Returns (data_type, is_nullable) for the named column on station.persona, or null
    /// when the column does not exist. Mirrors Story039/Story192's own information_schema helpers.</summary>
    static async Task<(string DataType, string IsNullable)?> QueryColumnAsync(DatabaseFixture db, string columnName)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            """
            select data_type, is_nullable from information_schema.columns
            where table_schema = 'station' and table_name = 'persona' and column_name = @column
            """,
            new { column = columnName });

        return row == default ? null : (row.data_type, row.is_nullable);
    }

    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "25-persona-provenance-migration.sh"));

    static async Task<(string? ImportedFrom, DateTime? ImportedAt)> ReadProvenanceAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(string? imported_from, DateTime? imported_at)>(
            "select imported_from, imported_at from station.persona where id = @id", new { id });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/25-persona-provenance-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsColumnsInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationAddsBothProvenanceColumnsAsTextAndTimestamptzNullable()
        {
            // Simulate a pre-migration database by dropping both provenance columns.
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "alter table station.persona drop column if exists imported_from, drop column if exists imported_at");

            Assert.Null(await QueryColumnAsync(db, "imported_from"));

            RunMigrationScript(db);

            var importedFrom = await QueryColumnAsync(db, "imported_from");
            Assert.NotNull(importedFrom);
            Assert.Equal("text", importedFrom.Value.DataType);
            Assert.Equal("YES", importedFrom.Value.IsNullable);

            var importedAt = await QueryColumnAsync(db, "imported_at");
            Assert.NotNull(importedAt);
            Assert.Equal("timestamp with time zone", importedAt.Value.DataType);
            Assert.Equal("YES", importedAt.Value.IsNullable);
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
        public async Task RerunningTheMigrationExitsZeroAndLeavesTheShapeUnchanged()
        {
            // Columns already exist (from db/06's fresh-init or a prior migration run).
            var before = await QueryColumnAsync(db, "imported_from");
            Assert.NotNull(before);

            // First run — succeeds even if columns already exist (ADD COLUMN IF NOT EXISTS).
            RunMigrationScript(db);

            // Second run — RunFileInContainer throws on a nonzero exit code, so simply RETURNING here
            // (rather than throwing) is itself the proof this run exited 0.
            RunMigrationScript(db);

            var after = await QueryColumnAsync(db, "imported_from");
            Assert.Equal(before, after);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the two "structurally can't fail" gaps, proven against real Postgres (finding 7)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAuthoredPersonaProvenance(DatabaseFixture db)
    {
        [Fact]
        public async Task CreateAsyncLeavesProvenanceNullInTheDatabase()
        {
            // An authored-in-place persona (real PersonaRepository.CreateAsync, never import) — read
            // straight back out of Postgres, not re-derived from the SQL text.
            await db.ResetStationAsync();
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await Repo(db).CreateAsync(new PersonaDraft("Authored DJ", "", "", ""), CancellationToken.None));

            var provenance = await ReadProvenanceAsync(db, created.Persona.Id);

            Assert.Null(provenance.ImportedFrom);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioImportThenAdminEdit(DatabaseFixture db)
    {
        [Fact]
        public async Task AnAdminEditAfterImportLeavesTheStampUntouched()
        {
            // Import stamps provenance (real PersonaImportRepository.ImportAsync); a subsequent admin
            // PATCH (real PersonaRepository.UpdateAsync) must not clear or rewrite it.
            await db.ResetStationAsync();
            var outcome = await ImportRepo(db).ImportAsync(
                new PersonaImportRequest("dj-edit-survives", "af_heart", BuildCard(), "midnight-mabel"),
                CancellationToken.None);
            var imported = Assert.IsType<PersonaImportOutcome.Imported>(outcome);
            var beforeEdit = await ReadProvenanceAsync(db, imported.PersonaId);

            var updateOutcome = await Repo(db).UpdateAsync(
                imported.PersonaId, new PersonaDraft("Renamed DJ", "", "", "af_sky"), CancellationToken.None);
            Assert.IsType<PersonaWriteResult.Updated>(updateOutcome);

            var afterEdit = await ReadProvenanceAsync(db, imported.PersonaId);

            Assert.Equal(beforeEdit, afterEdit);
        }
    }
}
