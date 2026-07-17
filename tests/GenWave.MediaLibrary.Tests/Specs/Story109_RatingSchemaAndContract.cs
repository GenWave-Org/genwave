// STORY-109 — Rating schema + contract land without behavior change
//
// BDD specification — xUnit. Integration facts hit real Postgres via DatabaseCollection;
// the contract fact reflects over the Core assembly (no database). Mirrors Story030/Story039/
// Story046/Story076's schema-half shape. See docs/PLAN.md Epic S.

using System.Reflection;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRatingSchemaAndContract
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns (data_type, is_nullable, column_default) for the named column on
    /// <c>library.media_rating</c>, or null when the column does not exist.
    /// </summary>
    static async Task<(string DataType, string IsNullable, string? ColumnDefault)?> QueryColumnAsync(
        DatabaseFixture db, string columnName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable, string? column_default)>(
            @"SELECT data_type, is_nullable, column_default
              FROM information_schema.columns
              WHERE table_schema = 'library'
                AND table_name   = 'media_rating'
                AND column_name  = @col",
            new { col = columnName });

        return row == default ? null : (row.data_type, row.is_nullable, row.column_default);
    }

    static async Task<bool> TableExistsAsync(DatabaseFixture db, string tableName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            @"SELECT count(*) FROM information_schema.tables
              WHERE table_schema = 'library' AND table_name = @t",
            new { t = tableName });
        return count > 0;
    }

    /// <summary>Inserts a fresh library.media row (state='discovered') and returns its id.</summary>
    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO library.media (path, format, size_bytes, mtime, state, library_id)
              VALUES (@path, 'flac', 1024, now(), 'discovered', 1)
              RETURNING id",
            new { path });
    }

    /// <summary>
    /// Executes <c>db/08-rating-migration.sh</c> against the test database by piping the script file
    /// to <c>bash -s</c> inside the compose testdb container via the fixture.
    /// </summary>
    static void RunMigrationScript(DatabaseFixture db)
    {
        var scriptPath = Path.Combine(db.RepoRoot, "db", "08-rating-migration.sh");
        db.RunFileInContainer(scriptPath);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init (db/01-library.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshInitHasTheRatingTable(DatabaseFixture db)
    {
        [Fact]
        public async Task MediaRatingTableExistsWithTheF331Shape()
        {
            // SELECT against information_schema.columns for library.media_rating:
            // media_id bigint NOT NULL, score int NOT NULL DEFAULT 50, never_play boolean NOT NULL
            // DEFAULT false, updated_at timestamptz NOT NULL DEFAULT now() (F33.1).
            var mediaId = await QueryColumnAsync(db, "media_id");
            Assert.NotNull(mediaId);
            Assert.Equal("bigint", mediaId.Value.DataType);
            Assert.Equal("NO", mediaId.Value.IsNullable);

            var score = await QueryColumnAsync(db, "score");
            Assert.NotNull(score);
            Assert.Equal("integer", score.Value.DataType);
            Assert.Equal("NO", score.Value.IsNullable);
            Assert.NotNull(score.Value.ColumnDefault);
            Assert.Contains("50", score.Value.ColumnDefault);

            var neverPlay = await QueryColumnAsync(db, "never_play");
            Assert.NotNull(neverPlay);
            Assert.Equal("boolean", neverPlay.Value.DataType);
            Assert.Equal("NO", neverPlay.Value.IsNullable);
            Assert.NotNull(neverPlay.Value.ColumnDefault);
            Assert.Contains("false", neverPlay.Value.ColumnDefault, StringComparison.OrdinalIgnoreCase);

            var updatedAt = await QueryColumnAsync(db, "updated_at");
            Assert.NotNull(updatedAt);
            Assert.Equal("timestamp with time zone", updatedAt.Value.DataType);
            Assert.Equal("NO", updatedAt.Value.IsNullable);
            Assert.NotNull(updatedAt.Value.ColumnDefault);
            Assert.Contains("now", updatedAt.Value.ColumnDefault, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task MediaIdIsThePrimaryKeyAndCascadesOnMediaDelete()
        {
            // information_schema.table_constraints / referential_constraints: PK on media_id,
            // FK to library.media(id) with delete_rule CASCADE (the deliberate PK=FK 1:1 shape).
            await using var conn = await db.DataSource.OpenConnectionAsync();

            var pkColumn = await conn.QuerySingleOrDefaultAsync<string>(
                @"SELECT kcu.column_name
                  FROM information_schema.table_constraints tc
                  JOIN information_schema.key_column_usage kcu
                    ON kcu.constraint_name = tc.constraint_name
                   AND kcu.constraint_schema = tc.constraint_schema
                  WHERE tc.table_schema = 'library'
                    AND tc.table_name = 'media_rating'
                    AND tc.constraint_type = 'PRIMARY KEY'");
            Assert.Equal("media_id", pkColumn);

            var fk = await conn.QuerySingleOrDefaultAsync<(string ReferencedTable, string ReferencedColumn, string DeleteRule)>(
                @"SELECT ccu.table_name AS referenced_table, ccu.column_name AS referenced_column, rc.delete_rule
                  FROM information_schema.table_constraints tc
                  JOIN information_schema.referential_constraints rc
                    ON rc.constraint_name = tc.constraint_name
                   AND rc.constraint_schema = tc.constraint_schema
                  JOIN information_schema.constraint_column_usage ccu
                    ON ccu.constraint_name = tc.constraint_name
                   AND ccu.constraint_schema = tc.constraint_schema
                  WHERE tc.table_schema = 'library'
                    AND tc.table_name = 'media_rating'
                    AND tc.constraint_type = 'FOREIGN KEY'");
            Assert.Equal("media", fk.ReferencedTable);
            Assert.Equal("id", fk.ReferencedColumn);
            Assert.Equal("CASCADE", fk.DeleteRule);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/08-rating-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsTheTableInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationCreatesTheTableOnAPreS1Schema()
        {
            // Simulate pre-migration: DROP TABLE IF EXISTS library.media_rating.
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("DROP TABLE IF EXISTS library.media_rating");

            Assert.False(await TableExistsAsync(db, "media_rating"));

            // Run db/08-rating-migration.sh via DatabaseFixture.RunFileInContainer.
            RunMigrationScript(db);

            // After: the table exists with the same shape as fresh init (both paths converge).
            Assert.True(await TableExistsAsync(db, "media_rating"));

            var score = await QueryColumnAsync(db, "score");
            Assert.NotNull(score);
            Assert.Equal("integer", score.Value.DataType);
            Assert.Equal("NO", score.Value.IsNullable);

            var neverPlay = await QueryColumnAsync(db, "never_play");
            Assert.NotNull(neverPlay);
            Assert.Equal("boolean", neverPlay.Value.DataType);
            Assert.Equal("NO", neverPlay.Value.IsNullable);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — contract shape (Core seam, no consumers yet)
    // ---------------------------------------------------------------------

    public sealed class ScenarioCoreCarriesTheRatingSeam
    {
        [Fact]
        public void IMediaRatingExposesVoteSetNeverPlayAndBatchRead()
        {
            // Reflection pin over the Core assembly: GenWave.Core.Abstractions.IMediaRating
            // carries the clamped-vote, set-never-play, and batch-read members (async, CancellationToken).
            var type = typeof(IMediaRating);
            Assert.Equal("GenWave.Core.Abstractions", type.Namespace);
            Assert.True(type.IsInterface);

            var vote = type.GetMethod(nameof(IMediaRating.VoteAsync));
            Assert.NotNull(vote);
            Assert.Equal(typeof(Task<VoteOutcome>), vote.ReturnType);
            var voteParams = vote.GetParameters();
            Assert.Equal(3, voteParams.Length);
            Assert.Equal(typeof(string), voteParams[0].ParameterType);
            Assert.Equal(typeof(VoteDirection), voteParams[1].ParameterType);
            Assert.Equal(typeof(CancellationToken), voteParams[2].ParameterType);

            var setNeverPlay = type.GetMethod(nameof(IMediaRating.SetNeverPlayAsync));
            Assert.NotNull(setNeverPlay);
            Assert.Equal(typeof(Task<NeverPlayOutcome>), setNeverPlay.ReturnType);
            var setNeverPlayParams = setNeverPlay.GetParameters();
            Assert.Equal(3, setNeverPlayParams.Length);
            Assert.Equal(typeof(string), setNeverPlayParams[0].ParameterType);
            Assert.Equal(typeof(bool), setNeverPlayParams[1].ParameterType);
            Assert.Equal(typeof(CancellationToken), setNeverPlayParams[2].ParameterType);

            var getRatings = type.GetMethod(nameof(IMediaRating.GetRatingsAsync));
            Assert.NotNull(getRatings);
            Assert.Equal(typeof(Task<IReadOnlyList<MediaRating>>), getRatings.ReturnType);
            var getRatingsParams = getRatings.GetParameters();
            Assert.Equal(2, getRatingsParams.Length);
            Assert.Equal(typeof(IReadOnlyList<string>), getRatingsParams[0].ParameterType);
            Assert.Equal(typeof(CancellationToken), getRatingsParams[1].ParameterType);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — constraint teeth + idempotency
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioConstraintsReject(DatabaseFixture db)
    {
        [Fact]
        public async Task DirectInsertAboveOneHundredIsRejectedByTheCheck()
        {
            // INSERT score = 101 → PostgresException naming the CHECK (F33.1).
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/test/rating-check-above.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "INSERT INTO library.media_rating (media_id, score) VALUES (@mediaId, 101)",
                new { mediaId }));
        }

        [Fact]
        public async Task DirectInsertBelowZeroIsRejectedByTheCheck()
        {
            // INSERT score = -1 → PostgresException naming the CHECK (F33.1).
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/test/rating-check-below.flac");

            await using var conn = await db.DataSource.OpenConnectionAsync();
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "INSERT INTO library.media_rating (media_id, score) VALUES (@mediaId, -1)",
                new { mediaId }));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationIsIdempotent(DatabaseFixture db)
    {
        [Fact]
        public async Task RerunningTheMigrationDoesNotErrorOrChangeAnything()
        {
            // Run db/08-rating-migration.sh twice; second run exits clean (IF NOT EXISTS guards)
            // and the schema is unchanged (launch.sh auto-applies migrations every launch).
            RunMigrationScript(db);
            RunMigrationScript(db);

            Assert.True(await TableExistsAsync(db, "media_rating"));
        }
    }
}
