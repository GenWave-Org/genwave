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
//
// The T220 write-path facts (AKindedTrackAiredWritesTheKind, MusicRowsStayNull,
// ABudgetDroppedRenderProducesNoKindedRow) drive real StationEvents through the REAL
// BoothLogWriter/BoothLogDrainService pipeline into the real (test) database — the same
// production-pipeline discipline Story215_BoothLogPersonaStamp.cs's own DriveThroughAsync uses —
// because the write-side types (BoothLogWriter, BoothLogDrainService, BoothLogEntryRequest) are
// internal to GenWave.MediaLibrary, and a fake store would never prove the real INSERT column-list
// wiring honestly. `segment_kind` is deliberately read back with a raw query rather than through
// BoothLogRepository.ReadAsync/BoothLogEntry — F113.3 keeps the read path untouched this cycle, so
// the column has no projection to assert against yet.

using Dapper;
using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

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

    /// <summary>No-op <see cref="IActivePersonaAccessor"/> double — these facts assert on
    /// <c>segment_kind</c>, not the persona stamp (Story215_BoothLogPersonaStamp.cs's own concern),
    /// so a fixed "no persona active" answer keeps every scenario below focused.</summary>
    sealed class NullPersonaAccessor : IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);

        public long? ActivePersonaId => null;
    }

    static BoothLogRepository Store(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new BoothLogOptions()));

    /// <summary>
    /// Publishes every <paramref name="events"/> through the real <see cref="BoothLogWriter"/> and
    /// drains each through the real <see cref="BoothLogDrainService.ProcessAsync"/> — the same
    /// production pipeline Story215_BoothLogPersonaStamp.cs's own DriveThroughAsync drives.
    /// </summary>
    static async Task DriveThroughAsync(DatabaseFixture db, params StationEvent[] events)
    {
        var channel = Channel.CreateBounded<BoothLogEntryRequest>(16);
        var writer = new BoothLogWriter(channel.Writer, new NullPersonaAccessor(), NullLogger<BoothLogWriter>.Instance);
        var drain = new BoothLogDrainService(channel.Reader, Store(db), NullLogger<BoothLogDrainService>.Instance);

        foreach (var evt in events)
            writer.Publish(evt);

        for (var i = 0; i < events.Length; i++)
            await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);
    }

    /// <summary>The persisted `segment_kind` for every `track-started` row, newest first — a raw
    /// query rather than <see cref="BoothLogRepository.ReadAsync"/> because the column has no
    /// projection on <see cref="BoothLogEntry"/> yet (F113.3: the read path is untouched this
    /// cycle).</summary>
    static async Task<List<string?>> TrackStartedSegmentKindsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<string?>(
            """
            select segment_kind from station.booth_log
            where kind = 'track-started'
            order by occurred_at desc, id desc
            """);
        return rows.ToList();
    }

    /// <summary>Every row's `kind`, newest first — used by the sad-path fact to prove a dropped
    /// render's `patter-aired` row is the ONLY row written, never a `track-started` sibling.</summary>
    static async Task<List<string>> AllKindsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<string>(
            "select kind from station.booth_log order by occurred_at desc, id desc");
        return rows.ToList();
    }

    /// <summary>Inserts one raw <c>station.booth_log</c> row with an explicit <c>occurred_at</c> — the
    /// demo-hour gate facts below need hour-bucket control the real writer/drain pipeline (which
    /// always stamps <c>now()</c>) cannot give them, and need a <c>segment_kind</c> value
    /// (<c>ContextSegment</c>) that has no <see cref="SegmentKind"/> enum member yet (see this file's
    /// header and <c>tools/demo_hour_gate.sql</c>'s own remarks) so <see cref="TrackAired"/> cannot
    /// carry it either.</summary>
    static async Task InsertBoothLogRowAsync(
        DatabaseFixture db, DateTimeOffset occurredAt, string kind, string? segmentKind)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into station.booth_log (occurred_at, kind, summary, segment_kind)
            values (@occurredAt, @kind, @summary, @segmentKind)
            """,
            new { occurredAt, kind, summary = $"{kind} row", segmentKind });
    }

    /// <summary>The top of an arbitrary, fixed UTC hour — the demo-hour gate facts' "qualifying"
    /// broadcast hour. A fixed instant (not <c>DateTimeOffset.UtcNow</c>) keeps every seeded row's
    /// hour bucket deterministic regardless of when the suite runs.</summary>
    static readonly DateTimeOffset QualifyingHour = new(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

    /// <summary>The "non-qualifying" hour — missing a ContextSegment row — three hours away from
    /// <see cref="QualifyingHour"/> so no seeded row's minute offset can ever cross into it.</summary>
    static readonly DateTimeOffset NonQualifyingHour = QualifyingHour.AddHours(3);

    /// <summary>Dapper's row shape for <c>tools/demo_hour_gate.sql</c>'s own SELECT list, in column
    /// order — mirrors <see cref="QueryColumnAsync"/>'s own tuple-projection convention.</summary>
    static async Task<List<(DateTime BroadcastHour, bool HasStationId, bool HasContextSegment,
        bool HasOtherNonMusicKind, long MusicRowCount, long KindedRowCount)>> RunDemoHourGateOverTwoHoursAsync(
            DatabaseFixture db)
    {
        // Given two candidate broadcast hours: QualifyingHour carries a StationId ident, a
        // ContextSegment, a further non-music kind (LeadIn), and a plain music row (segment_kind
        // NULL) — everything the gate's HAVING predicate requires. A patter-aired row also carries
        // segment_kind='StationId' in this hour to pin the gate's `kind = 'track-started'` filter:
        // if the gate ever dropped that filter, this row would inflate kinded_row_count from 3 to 4.
        // NonQualifyingHour is missing ContextSegment, so the gate's HAVING predicate must exclude it.
        await db.ResetBoothLogAsync();

        await InsertBoothLogRowAsync(db, QualifyingHour.AddMinutes(5), "track-started", "StationId");
        await InsertBoothLogRowAsync(db, QualifyingHour.AddMinutes(15), "track-started", "ContextSegment");
        await InsertBoothLogRowAsync(db, QualifyingHour.AddMinutes(25), "track-started", "LeadIn");
        await InsertBoothLogRowAsync(db, QualifyingHour.AddMinutes(35), "track-started", null);
        await InsertBoothLogRowAsync(db, QualifyingHour.AddMinutes(45), "patter-aired", "StationId");

        await InsertBoothLogRowAsync(db, NonQualifyingHour.AddMinutes(5), "track-started", "StationId");
        await InsertBoothLogRowAsync(db, NonQualifyingHour.AddMinutes(15), "track-started", "LeadIn");
        await InsertBoothLogRowAsync(db, NonQualifyingHour.AddMinutes(25), "track-started", null);

        // When the shipped gate (SPEC F113.2, PLAN T220) runs against them — the real file, executed
        // through Npgsql, never merely read as text.
        var sql = await File.ReadAllTextAsync(Path.Combine(db.RepoRoot, "tools", "demo_hour_gate.sql"));

        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<(DateTime BroadcastHour, bool HasStationId, bool HasContextSegment,
            bool HasOtherNonMusicKind, long MusicRowCount, long KindedRowCount)>(sql);
        return rows.ToList();
    }

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

        [Fact]
        public async Task AKindedTrackAiredWritesTheKind()
        {
            // Given a TrackAired event carrying SegmentKind.StationId — PlayoutFeeder's own
            // forwarded MediaItem.SegmentKind (SPEC F113.1)...
            await db.ResetBoothLogAsync();
            var stationIdAiring = new TrackAired(
                "tts:abc123", "GenWave", "GenWave", -2.0, DateTimeOffset.UtcNow, 4_000,
                SegmentKind: SegmentKind.StationId);

            // When it flows through the real writer/drain pipeline...
            await DriveThroughAsync(db, stationIdAiring);

            // Then the persisted track-started row's segment_kind is the enum's own token name —
            // stamped synchronously at publish time, never re-derived.
            var kinds = await TrackStartedSegmentKindsAsync(db);
            Assert.Equal(["StationId"], kinds);
        }

        [Fact]
        public async Task MusicRowsStayNull()
        {
            // Given a music TrackAired — SegmentKind unset, the only shape
            // MediaReferenceExtensions.ToMediaItem (the sole music-selection mapping) ever produces...
            await db.ResetBoothLogAsync();
            var musicAiring = new TrackAired("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000);

            // When it flows through the real writer/drain pipeline...
            await DriveThroughAsync(db, musicAiring);

            // Then the persisted row's segment_kind stays NULL — the demo-hour gate's non-music
            // predicate (segment_kind IS NOT NULL) never counts a music row.
            var kinds = await TrackStartedSegmentKindsAsync(db);
            Assert.Equal([null], kinds);
        }

        [Fact]
        public async Task TheDemoHourGateReturnsOnlyTheQualifyingHour()
        {
            var rows = await RunDemoHourGateOverTwoHoursAsync(db);

            // Then only the hour with a StationId, a ContextSegment, AND a further non-music kind
            // survives the gate's HAVING predicate — the non-qualifying hour (missing ContextSegment)
            // never appears, and no third, unrelated hour is fabricated either.
            Assert.Equal([QualifyingHour.UtcDateTime], rows.Select(row => row.BroadcastHour));
        }

        [Fact]
        public async Task TheDemoHourGateCountsKindedRowsFromSegmentKindAlone()
        {
            var rows = await RunDemoHourGateOverTwoHoursAsync(db);

            // Three track-started rows carry a non-null segment_kind (StationId, ContextSegment,
            // LeadIn) — the patter-aired row's own segment_kind='StationId' never counts, proving
            // the gate's `kind = 'track-started'` predicate rather than a bare `segment_kind IS NOT
            // NULL` scan across every row.
            Assert.Equal(3L, rows.Single().KindedRowCount);
        }

        [Fact]
        public async Task TheDemoHourGateCountsMusicRowsSeparatelyFromKindedRows()
        {
            var rows = await RunDemoHourGateOverTwoHoursAsync(db);

            // Exactly the one segment_kind IS NULL track-started row in the qualifying hour — the
            // gate's music_row_count and kinded_row_count are a partition, never overlapping.
            Assert.Equal(1L, rows.Single().MusicRowCount);
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
            // booth_log column. station.schedule_special (PLAN T258 addition) is dropped WHOLESALE
            // immediately before station.show for the identical FK-safe reason — it is a sibling table
            // in the SAME DatabaseCollection that may already exist by the time this fact runs
            // (Story317_SpecialsStore.cs's own facts create it) and carries its own FK into
            // station.show. A bare CASCADE on the station.show drop would only strip that FK
            // CONSTRAINT, leaving the table itself behind in a state db/36's idempotent CREATE TABLE IF
            // NOT EXISTS can never repair (unlike station.show/segment_schedule.show_id here, which
            // db/33's own CREATE/ADD COLUMN restores below) — dropping the whole table instead keeps it
            // self-healing the same way every other table in this file already is.
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync("drop table if exists station.schedule_special");
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

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDroppedRendersNeverCount(DatabaseFixture db)
    {
        [Fact]
        public async Task ABudgetDroppedRenderProducesNoKindedRow()
        {
            // Given a render that succeeded (SegmentGenerated published) but the segment it produced
            // never actually reached air — no corresponding TrackAired ever follows it, the exact
            // "budget-dropped" shape (the boundary producer's own null-piece degrade path never
            // reaches PlayoutFeeder at all)...
            await db.ResetBoothLogAsync();
            var droppedRender = new SegmentGenerated("tts:dropped123", "StationId", "af_heart");

            // When it flows through the real writer/drain pipeline...
            await DriveThroughAsync(db, droppedRender);

            // Then the render-time signal still logs (patter-aired keeps its existing meaning,
            // F113.3)...
            var kinds = await AllKindsAsync(db);
            Assert.Equal(["patter-aired"], kinds);

            // ...but no track-started row — kinded or otherwise — was ever written: the air-time
            // stamp only ever comes from an observed TrackAired, which this scenario never publishes.
            var kindedTrackStartedRows = await TrackStartedSegmentKindsAsync(db);
            Assert.Empty(kindedTrackStartedRows);
        }
    }
}
