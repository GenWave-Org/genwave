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

    /// <summary>Reads back the tag pass's own two columns for one row. Shared by every scenario in
    /// this feature that drives a real file through <see cref="EnrichmentService.EnrichOneAsync"/>
    /// and asserts what landed.</summary>
    static async Task<(bool? Explicit, string? ExplicitSource)> ExplicitColumnsOfAsync(DatabaseFixture f, long id)
    {
        await using var conn = await f.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<(bool?, string?)>(
            "select explicit, explicit_source from library.media where id = @id", new { id });
    }

    /// <summary>Seeds <c>explicit</c>/<c>explicit_source</c> directly in Postgres — stands in for a
    /// prior tag/LLM/operator pass that isn't (yet, or ever, in this test) reached via the real code
    /// path, e.g. T113's LLM sweep or T115's operator override endpoint.</summary>
    static async Task SeedExplicitAsync(DatabaseFixture db, long id, bool value, string source)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set explicit = @value, explicit_source = @source where id = @id",
            new { id, value, source });
    }

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

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAdvisoryTagsStampFirst(DatabaseFixture db)
    {
        // Given a file whose metadata carries an explicit/advisory flag, When enrichment runs (F95.3).

        [Fact]
        public async Task ExplicitIsStampedWithSourceTag()
        {
            // Drives the real enrichment path end to end (TestMedia → Enricher's TagLib read →
            // MediaRepository.WriteEnrichmentAsync), never a raw SQL seed — proves the tag pass this
            // task adds, not just the schema T110 already pinned. Both real-world advisory
            // conventions (F95.3) are exercised: an ID3v2 TXXX user-text frame for mp3, a Vorbis
            // comment field for flac — the same ITUNESADVISORY key, value "1" = explicit.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);

                var mp3Path = TestMedia.CreateTone(dir, "explicit.mp3", itunesAdvisory: "1");
                var mp3Id = await repo.InsertDiscoveredAsync(
                    mp3Path, "mp3", new FileInfo(mp3Path).Length, Harness.Mtime, CancellationToken.None);
                await Harness.Enrichment(repo).EnrichOneAsync(mp3Id, CancellationToken.None);

                var mp3Columns = await ExplicitColumnsOfAsync(db, mp3Id);
                Assert.Equal(true, mp3Columns.Explicit);
                Assert.Equal("tag", mp3Columns.ExplicitSource);

                var flacPath = TestMedia.CreateTone(dir, "explicit.flac", itunesAdvisory: "1");
                var flacId = await repo.InsertDiscoveredAsync(
                    flacPath, "flac", new FileInfo(flacPath).Length, Harness.Mtime, CancellationToken.None);
                await Harness.Enrichment(repo).EnrichOneAsync(flacId, CancellationToken.None);

                var flacColumns = await ExplicitColumnsOfAsync(db, flacId);
                Assert.Equal(true, flacColumns.Explicit);
                Assert.Equal("tag", flacColumns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task UntaggedFilesStayNull()
        {
            // No advisory tag at all (SPEC F95.2) — a miss, never a sentinel false: both columns
            // stay NULL exactly like a freshly discovered, unenriched row.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "plain.flac");
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Null(columns.Explicit);
                Assert.Null(columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CleanAdvisoryTagStampsExplicitFalseWithSourceTag()
        {
            // ITUNESADVISORY=2 ("clean") is itself a positive result (F95.3), not a miss like an
            // absent tag — it must land explicit=false/'tag', never stay NULL.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "clean.mp3", itunesAdvisory: "2");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Equal(false, columns.Explicit);
                Assert.Equal("tag", columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ZeroAdvisoryValueIsAMissAndStaysNull()
        {
            // ITUNESADVISORY=0 is outside the tag pass's vocabulary ("1"/"2" only, F95.3) — a miss,
            // exactly like an absent tag: stays NULL/NULL, never stamped.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "zero-advisory.mp3", itunesAdvisory: "0");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Null(columns.Explicit);
                Assert.Null(columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ChangedAdvisoryTagRestampsOnReEnrich()
        {
            // A prior LLM sweep (T113, not yet built) stamped this row explicit=true/'llm' — seeded
            // directly since no real sweep exists to drive. The file's own ITUNESADVISORY tag then
            // changes on disk (1 -> 2) and the row is rediscovered for re-enrichment: the tag pass is
            // the ONLY place a re-scan lands (WriteEnrichmentAsync's own doc comment), so it must
            // supersede the LLM guess — stronger evidence wins regardless of the prior source, this
            // is the llm -> tag supersede specifically.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "llm-then-tag.mp3", itunesAdvisory: "1");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await SeedExplicitAsync(db, id, value: true, source: "llm");

                // The advisory tag flips on disk (explicit -> clean) — same file, rewritten.
                TestMedia.CreateTone(dir, "llm-then-tag.mp3", itunesAdvisory: "2");

                // Mirrors Story041's re-enrich idiom: reset state to discovered before re-running
                // EnrichOneAsync directly (EnrichOneAsync itself does not filter on state).
                await using (var conn = await db.DataSource.OpenConnectionAsync())
                    await conn.ExecuteAsync("update library.media set state = 'discovered' where id = @id", new { id });

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Equal(false, columns.Explicit);
                Assert.Equal("tag", columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
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

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheOperatorAlwaysWins(DatabaseFixture db)
    {
        // Given an operator override (source operator) (F95.3), set via the real admin endpoint (T115).

        [Fact(Skip = "Pending (T115)")]
        public void OverrideEndpointStampsSourceOperator() { }

        [Fact(Skip = "Pending (T113)")]
        public void LaterSweepsNeverOverwriteOperatorRows() { }

        [Fact]
        public async Task TagPassNeverOverwritesOperatorRows()
        {
            // The T115 override endpoint doesn't exist yet, but the write-guard this task (T112) pins
            // lives in WriteEnrichmentAsync's own CASE, not the endpoint — seed operator ownership
            // directly in Postgres, then drive a REAL file with a contradicting advisory tag through
            // the real enrichment path (Harness.Enrichment -> EnrichOneAsync) and assert the
            // operator's stamp survives untouched.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var repo = Harness.Repo(db);
                var path = TestMedia.CreateTone(dir, "operator-owned.mp3", itunesAdvisory: "2");
                var id = await repo.InsertDiscoveredAsync(
                    path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                await SeedExplicitAsync(db, id, value: true, source: "operator");

                await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

                var columns = await ExplicitColumnsOfAsync(db, id);
                Assert.Equal(true, columns.Explicit);
                Assert.Equal("operator", columns.ExplicitSource);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
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
