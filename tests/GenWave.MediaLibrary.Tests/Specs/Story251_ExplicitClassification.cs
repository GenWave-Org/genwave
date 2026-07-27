// STORY-251 — Know which tracks are explicit (SPEC F95.2, F95.3, F95.5, PLAN T110/T112/T113/T115)
//
// BDD specification — xUnit, pending. db/26 schema + the layered tag → LLM sweep → operator
// pipeline, driven against a real Postgres with the LLM faked at the HTTP boundary (T72
// mood-tagger idiom). The operator override endpoint's wire facts (T115) drive the real
// admin route via the Host factory.
//
// T110 is Postgres-backed (Category=Integration) via DatabaseCollection, mirroring the
// SchemaAndMigration family's shape (Story039_CatalogWriteColumnsSchemaAndMigration and
// Story237_ProvenanceSchemaAndMigration are the closest siblings): ScenarioTheSpineCarriesTheFlag
// asserts the column shape db/01-library.sh's fresh init leaves in place and proves the
// explicit_source CHECK accepts its vocabulary; ScenarioMigrationAddsColumnsInPlace drops both
// columns, runs db/26-explicit-classification-migration.sh in the compose testdb container, and
// asserts the resulting shape, the CHECK's teeth, and a pre-existing row's NULL/NULL;
// ScenarioRejectingUnknownSources and ScenarioMigrationIsIdempotent are this family's sad paths.

using Dapper;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureExplicitClassification
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns (data_type, is_nullable) for the named column on library.media, or null when the
    /// column does not exist. Mirrors Story039/Story237's own information_schema helpers.
    /// </summary>
    static async Task<(string DataType, string IsNullable)?> QueryColumnAsync(DatabaseFixture db, string columnName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            """
            select data_type, is_nullable from information_schema.columns
            where table_schema = 'library' and table_name = 'media' and column_name = @column
            """,
            new { column = columnName });

        return row == default ? null : (row.data_type, row.is_nullable);
    }

    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, library_id)
            values (@path, 'flac', 1024, now(), 'discovered', 1)
            returning id
            """,
            new { path });
    }

    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "26-explicit-classification-migration.sh"));

    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init (db/01-library.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSpineCarriesTheFlag(DatabaseFixture db)
    {
        // Given db/26 applied (F95.2).

        [Fact]
        public async Task ExplicitBooleanExistsWithNullAsUnknown()
        {
            // A freshly discovered row is unclassified — NULL, never a sentinel false.
            await db.ResetAsync();

            var column = await QueryColumnAsync(db, "explicit");
            Assert.NotNull(column);
            Assert.Equal("boolean", column.Value.DataType);
            Assert.Equal("YES", column.Value.IsNullable);

            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-flag-unknown.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var explicitValue = await conn.ExecuteScalarAsync<bool?>(
                "select explicit from library.media where id = @mediaId", new { mediaId });
            Assert.Null(explicitValue);
        }

        [Fact]
        public async Task ExplicitSourceIsConstrainedToTagLlmOperator()
        {
            await db.ResetAsync();

            var column = await QueryColumnAsync(db, "explicit_source");
            Assert.NotNull(column);
            Assert.Equal("text", column.Value.DataType);
            Assert.Equal("YES", column.Value.IsNullable);

            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-source-check.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();

            // Each of the three known origins (F95.3) is accepted by the CHECK.
            foreach (var source in new[] { "tag", "llm", "operator" })
            {
                await conn.ExecuteAsync(
                    "update library.media set explicit = true, explicit_source = @source where id = @mediaId",
                    new { mediaId, source });

                var stored = await conn.ExecuteScalarAsync<string>(
                    "select explicit_source from library.media where id = @mediaId", new { mediaId });
                Assert.Equal(source, stored);
            }
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/26-explicit-classification-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsColumnsInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationAddsBothColumnsWithTheCheckAndLeavesExistingRowsNullNull()
        {
            // Simulate a pre-migration database by dropping both explicit-classification columns.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "alter table library.media drop column if exists explicit, drop column if exists explicit_source");

            Assert.Null(await QueryColumnAsync(db, "explicit"));

            // Insert a row while the columns do not yet exist.
            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-migration-preexisting.flac");

            RunMigrationScript(db);

            var explicitCol = await QueryColumnAsync(db, "explicit");
            Assert.NotNull(explicitCol);
            Assert.Equal("boolean", explicitCol.Value.DataType);
            Assert.Equal("YES", explicitCol.Value.IsNullable);

            var explicitSourceCol = await QueryColumnAsync(db, "explicit_source");
            Assert.NotNull(explicitSourceCol);
            Assert.Equal("text", explicitSourceCol.Value.DataType);
            Assert.Equal("YES", explicitSourceCol.Value.IsNullable);

            // The CHECK has teeth — anything outside the vocabulary (F95.3) is rejected.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "update library.media set explicit_source = 'guess' where id = @mediaId", new { mediaId }));

            // The pre-existing row is untouched by the migration — NULL/NULL, never backfilled.
            var explicitValue = await conn.ExecuteScalarAsync<bool?>(
                "select explicit from library.media where id = @mediaId", new { mediaId });
            Assert.Null(explicitValue);

            var explicitSourceValue = await conn.ExecuteScalarAsync<string?>(
                "select explicit_source from library.media where id = @mediaId", new { mediaId });
            Assert.Null(explicitSourceValue);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unknown sources are rejected by the CHECK
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRejectingUnknownSources(DatabaseFixture db)
    {
        [Fact]
        public async Task AnUnknownSourceIsRejectedByTheCheckConstraint()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/test/explicit-source-rejected.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();

            // Anything outside the vocabulary (tag, llm, operator — F95.3) is rejected by the CHECK.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "update library.media set explicit_source = 'guess' where id = @mediaId", new { mediaId }));
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
            // Columns already exist (from db/01's fresh init or a prior migration run).
            var before = await QueryColumnAsync(db, "explicit");
            Assert.NotNull(before);

            // First run — succeeds even if columns already exist (ADD COLUMN IF NOT EXISTS).
            RunMigrationScript(db);

            // Second run — RunFileInContainer throws on a nonzero exit code, so simply RETURNING here
            // (rather than throwing) is itself the proof this run exited 0.
            RunMigrationScript(db);

            var after = await QueryColumnAsync(db, "explicit");
            Assert.Equal(before, after);
        }
    }

    public sealed class ScenarioAdvisoryTagsStampFirst
    {
        // Given a file whose metadata carries an explicit/advisory flag, When enrichment runs (F95.3).

        [Fact(Skip = "Pending (T112)")]
        public void ExplicitIsStampedWithSourceTag() { }

        [Fact(Skip = "Pending (T112)")]
        public void UntaggedFilesStayNull() { }
    }

    public sealed class ScenarioTheSweepCoversTheRest
    {
        // Given unclassified tracks and a configured LLM, When the offline batch pass runs (F95.3).

        [Fact(Skip = "Pending (T113)")]
        public void YesAndNoStampSourceLlm() { }

        [Fact(Skip = "Pending (T113)")]
        public void UnknownStampsAMissNeverAPartialWrite() { }

        [Fact(Skip = "Pending (T113)")]
        public void AlreadyClassifiedRowsAreNotReAsked() { }
    }

    public sealed class ScenarioTheOperatorAlwaysWins
    {
        // Given an operator override (source operator) (F95.3), set via the real admin endpoint (T115).

        [Fact(Skip = "Pending (T115)")]
        public void OverrideEndpointStampsSourceOperator() { }

        [Fact(Skip = "Pending (T113)")]
        public void LaterSweepsNeverOverwriteOperatorRows() { }
    }

    public sealed class ScenarioNeverPlayStaysOrthogonal
    {
        // Given a track under a never-play verdict (F95.5): the flag classifies, the verdict rules.

        [Fact(Skip = "Pending (T115)")]
        public void VerdictOperatesUnchangedRegardlessOfClassification() { }
    }

    public sealed class ScenarioLlmDownSkipsCleanly
    {
        // Sad path — LLM unreachable (F95.3, F69 pattern).

        [Fact(Skip = "Pending (T113)")]
        public void SweepSkipsWithASingleLogLineAndNoPartialStamps() { }
    }
}
