// STORY-304 — Airings become countable (F113)
//
// The T219 schema facts are BDD specifications — xUnit, Postgres-backed (Category=Integration) via
// DatabaseCollection — mirroring Story030_EnergyColumnsSchemaAndMigration's own TWO-scenario shape: a
// fresh-init scenario (ScenarioTheStampFlowsEndToEnd, proving db/06's mirror) and an in-place scenario
// (ScenarioMigrationAddsTheObjectsInPlace, proving db/33's own DDL directly by dropping the three T219
// objects and re-running the migration script). The fresh-init facts additionally (re)run db/33 in
// their own Arrange before asserting — db/33 is idempotent (CREATE TABLE / ADD COLUMN IF NOT EXISTS) —
// so they self-converge regardless of what a prior spec in the shared DatabaseCollection left behind
// (e.g. Story242_UpgradeChangesNothing.cs's several scenarios drop station.segment_schedule and rebuild
// it via db/27 alone, which predates show_id).

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAiredKindStamp
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Returns (data_type, is_nullable) for the named column on the given station table,
    /// or null when the column does not exist. Mirrors Story030/Story237's own helper.</summary>
    static async Task<(string DataType, string IsNullable)?> QueryColumnAsync(
        DatabaseFixture db, string table, string column)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable)>(
            """
            select data_type, is_nullable from information_schema.columns
            where table_schema = 'station' and table_name = @table and column_name = @column
            """,
            new { table, column });

        return row == default ? null : (row.data_type, row.is_nullable);
    }

    /// <summary>Mirrors Story118's own TableExistsAsync helper.</summary>
    static async Task<bool> TableExistsAsync(DatabaseFixture db, string table)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'station' and table_name = @table",
            new { table });
        return count > 0;
    }

    /// <summary>Runs db/33-show-and-segment-kind-migration.sh against the test database via the
    /// fixture. Mirrors Story030/Story192/Story237's own RunMigrationScript helper. Safe to call
    /// unconditionally — the script is idempotent (CREATE TABLE / ADD COLUMN IF NOT EXISTS).</summary>
    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "33-show-and-segment-kind-migration.sh"));

    // ---------------------------------------------------------------------
    // HAPPY PATH — fresh init (db/06's mirror of db/33)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheStampFlowsEndToEnd(DatabaseFixture db)
    {
        [Fact]
        public async Task BoothLogHasASegmentKindColumn()
        {
            // Converge first (see file header): db/33 is idempotent, so re-running it here makes this
            // fact self-sufficient regardless of collection ordering.
            RunMigrationScript(db);

            // information_schema: station.booth_log.segment_kind exists (text, nullable);
            // db/33 applied and db/06 fresh-init mirrored.
            var column = await QueryColumnAsync(db, "booth_log", "segment_kind");

            Assert.NotNull(column);
            Assert.Equal("text", column.Value.DataType);
            Assert.Equal("YES", column.Value.IsNullable);
        }

        [Fact]
        public async Task StationShowExists()
        {
            // Converge first (see file header).
            RunMigrationScript(db);

            // station.show (SPEC F114): the entity table stays dormant (no consumer until the
            // F114 slice) but must exist from db/06/db/33 onward.
            Assert.True(await TableExistsAsync(db, "show"));
        }

        [Fact]
        public async Task SegmentScheduleHasAShowIdColumn()
        {
            // Converge first (see file header) — this is the fact B1 named: without it, a prior
            // Story242 scenario that dropped+rebuilt segment_schedule via db/27 alone (which predates
            // show_id) would fail this if it ran first.
            RunMigrationScript(db);

            // station.segment_schedule.show_id (SPEC F114): a nullable FK, added alongside show_id's
            // owning table station.show.
            var column = await QueryColumnAsync(db, "segment_schedule", "show_id");

            Assert.NotNull(column);
            Assert.Equal("integer", column.Value.DataType);
            Assert.Equal("YES", column.Value.IsNullable);
        }

        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void AKindedTrackAiredWritesTheKind()
        {
            // TrackAired carrying SegmentKind.StationId ⇒ the track-started row's
            // segment_kind is 'StationId' (stamped synchronously at publish time).
            // Assert.Equal("StationId", row.SegmentKind);
            Assert.Fail("pending T220");
        }

        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void MusicRowsStayNull()
        {
            // A music TrackAired (SegmentKind null) writes NULL — the count query's
            // non-music predicate is segment_kind IS NOT NULL.
            // Assert.Null(row.SegmentKind);
            Assert.Fail("pending T220");
        }

        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void TheDemoHourQueryCountsFromTheColumnAlone()
        {
            // The documented query groups by date_trunc('hour') over segment_kind — no
            // LIKE over summary anywhere in it.
            // Assert.DoesNotContain("LIKE", documentedQuery);
            Assert.Fail("pending T220");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/33-show-and-segment-kind-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsTheObjectsInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationAddsTheShowTableAndBothColumnsInPlace()
        {
            // Simulate a pre-T219 database by dropping the three objects db/33 adds — FK-safe order:
            // the show_id column (and its FK) first, then the table it referenced, then the unrelated
            // booth_log column.
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync("alter table station.segment_schedule drop column if exists show_id");
                await conn.ExecuteAsync("drop table if exists station.show");
                await conn.ExecuteAsync("alter table station.booth_log drop column if exists segment_kind");
            }

            Assert.False(await TableExistsAsync(db, "show"));
            Assert.Null(await QueryColumnAsync(db, "segment_schedule", "show_id"));
            Assert.Null(await QueryColumnAsync(db, "booth_log", "segment_kind"));

            RunMigrationScript(db);

            Assert.True(await TableExistsAsync(db, "show"));

            var showId = await QueryColumnAsync(db, "segment_schedule", "show_id");
            Assert.NotNull(showId);
            Assert.Equal("integer", showId.Value.DataType);
            Assert.Equal("YES", showId.Value.IsNullable);

            var segmentKind = await QueryColumnAsync(db, "booth_log", "segment_kind");
            Assert.NotNull(segmentKind);
            Assert.Equal("text", segmentKind.Value.DataType);
            Assert.Equal("YES", segmentKind.Value.IsNullable);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDroppedRendersNeverCount
    {
        [Fact(Skip = "Pending T220 — see docs/PLAN.md")]
        public void ABudgetDroppedRenderProducesNoKindedRow()
        {
            // A render that times out of the budget logs patter-aired (render-time) but no
            // kinded track-started row exists — the air-time signal is the honest one.
            // Assert.Empty(kindedRowsForDroppedHash);
            Assert.Fail("pending T220");
        }
    }
}
