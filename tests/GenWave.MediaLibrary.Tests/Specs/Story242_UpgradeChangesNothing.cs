// STORY-242 — Upgrading changes nothing on the air (SPEC F91.6, PLAN T118)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. Mirrors
// the SchemaAndMigration family's shape (Story237_ProvenanceSchemaAndMigration.cs is the closest
// sibling): drop the table to simulate a pre-migration database, run db/27 in the compose testdb
// container, assert the resulting shape. Unlike db/25 (a column ADD on an existing table),
// station.segment_schedule is a brand-new table — the "pre-migration" simulation is DROP TABLE, not
// DROP COLUMN, and re-running the migration always recreates it (CREATE TABLE IF NOT EXISTS). Every
// scenario below that drops the table goes on to run a migration script expected to exit 0, which
// recreates it — so the table is present again by the time each such fact finishes. (A migration that
// itself throws would leave the drop uncorrected; no fact here exercises that case, so it never
// arises.) Story304_AiredKindStamp.cs's fresh-init facts read this same table's show_id column — a
// column db/27 alone (this file's own migration script) predates, so those scenarios here would leave
// it missing for whichever Story304 fact ran next in the shared DatabaseCollection. Story304 guards
// against that itself, by (re)running db/33 in its own Arrange before every assertion — so a scenario
// here leaving segment_schedule mid-upgrade-shape is safe to the rest of the suite.
//
// The allowlist-retirement half (AC3) lives in Story242_ActiveIdKeyRetired.cs (Host.Tests) — it
// drives PUT /api/settings and is out of this task's scope (PLAN T120).

using Dapper;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureUpgradeChangesNothing
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static void RunMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "27-segment-schedule-migration.sh"));

    /// <summary>db/33 (STORY-304, PLAN T219) is the next migration to touch this table after db/27 —
    /// it adds segment_schedule.show_id. An upgrading box that already ran db/27 reaches db/06's own
    /// shape only once db/33 has also run, so <see cref="ScenarioFreshInstallAndUpgradeProduceTheIdenticalShape"/>
    /// below chains this after <see cref="RunMigrationScript"/>.</summary>
    static void RunShowAndSegmentKindMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "33-show-and-segment-kind-migration.sh"));

    /// <summary>db/41 (the Gardener migration) is the in-place-upgrade path for two indexes db/01/db/06
    /// ALSO ship fresh (STORY-373's own <c>booth_log_show_track_started</c>, STORY-376's own
    /// <c>media_dup_keys</c>) — <see cref="ScenarioIndexMirrorMatchesTheUpgradePath"/> below drops each
    /// and reruns this script to prove the SAME "db/06/db/01 fresh vs. db/NN in-place-upgrade must build
    /// the byte-identical shape" claim <see cref="ScenarioFreshInstallAndUpgradeProduceTheIdenticalShape"/>
    /// already pins for <c>station.segment_schedule</c>'s own columns/constraints, extended to
    /// <c>pg_indexes</c> — a shape neither <see cref="CaptureColumnShapeAsync"/> nor
    /// <see cref="CaptureConstraintShapeAsync"/> can see (PLAN T363 carry-forward from T362 review).
    /// Idempotent (<c>IF NOT EXISTS</c> throughout, Story376_TheSameSongTwice.cs's own
    /// <c>ScenarioMigrationConvergence</c> already proves a rerun converges), so rerunning it here is
    /// safe against every other fact in this shared DatabaseCollection.</summary>
    static void RunGardenerMigrationScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "41-gardener-migration.sh"));

    static void RunFreshInstallScript(DatabaseFixture db) =>
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "06-station-settings-migration.sh"));

    static async Task DropScheduleTableAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("drop table if exists station.segment_schedule");
    }

    static async Task ClearActiveIdSettingAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("delete from station.settings where key = 'Station:Persona:ActiveId'");
    }

    static async Task SetActiveIdSettingAsync(DatabaseFixture db, long value)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into station.settings (key, value) values ('Station:Persona:ActiveId', to_jsonb(@value::bigint))",
            new { value });
    }

    static async Task<bool> ActiveIdKeyExistsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "select exists(select 1 from station.settings where key = 'Station:Persona:ActiveId')");
    }

    /// <summary>Settable-property Dapper projection — mirrors Story224's own RequestRow remarks:
    /// Npgsql reports the <c>genres</c> text[] column as the general Array CLR type, which Dapper's
    /// stricter positional-record constructor matching rejects.</summary>
    sealed record ScheduleRow
    {
        public int DayOfWeek { get; init; }
        public int StartMinute { get; init; }
        public int EndMinute { get; init; }
        public long? PersonaId { get; init; }
        public string[]? Genres { get; init; }
        public double? EnergyMin { get; init; }
        public double? EnergyMax { get; init; }
    }

    static async Task<IReadOnlyList<ScheduleRow>> ReadAllScheduleRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ScheduleRow>(
            """
            select day_of_week, start_minute, end_minute, persona_id::bigint as persona_id, genres,
                   energy_min::double precision as energy_min, energy_max::double precision as energy_max
            from station.segment_schedule
            order by day_of_week
            """);
        return rows.ToList();
    }

    /// <summary>Settable-property Dapper projection over <c>information_schema.columns</c> — same
    /// shape discipline as <see cref="ScheduleRow"/> above. One row per column, in ordinal order: this
    /// IS the "drift detector" shape for review finding F1 — a byte-for-byte column-shape comparison
    /// between db/06's fresh-install copy of station.segment_schedule and db/27's in-place-upgrade
    /// copy.</summary>
    sealed record ColumnShape
    {
        public string ColumnName { get; init; } = "";
        public string DataType { get; init; } = "";
        public string? UdtName { get; init; }
        public string IsNullable { get; init; } = "";
        public string? ColumnDefault { get; init; }
    }

    static async Task<IReadOnlyList<ColumnShape>> CaptureColumnShapeAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ColumnShape>(
            """
            select column_name, data_type, udt_name, is_nullable, column_default
            from information_schema.columns
            where table_schema = 'station' and table_name = 'segment_schedule'
            order by ordinal_position
            """);
        return rows.ToList();
    }

    /// <summary>One CHECK/EXCLUDE/FK/PK constraint on station.segment_schedule, rendered by
    /// <c>pg_get_constraintdef</c> — the textual definition (bounds, opclasses, ON DELETE action) that
    /// <c>information_schema.columns</c> alone cannot see. Ordered by (contype, definition) rather than
    /// constraint name: names are Postgres auto-generated and irrelevant to the actual shape being
    /// compared.</summary>
    sealed record ConstraintShape
    {
        public string Contype { get; init; } = "";
        public string Definition { get; init; } = "";
    }

    static async Task<IReadOnlyList<ConstraintShape>> CaptureConstraintShapeAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<ConstraintShape>(
            """
            select c.contype::text as contype, pg_get_constraintdef(c.oid) as definition
            from pg_constraint c
            join pg_class t on t.oid = c.conrelid
            join pg_namespace n on n.oid = t.relnamespace
            where n.nspname = 'station' and t.relname = 'segment_schedule'
            order by contype, definition
            """);
        return rows.ToList();
    }

    /// <summary>One index, captured via <c>pg_indexes</c> — <c>indexdef</c> is Postgres's own
    /// reconstructed <c>CREATE INDEX ...</c> text (the full column list, WHERE clause, and opclasses,
    /// folded into one canonical string regardless of the case/whitespace the DDL that created it
    /// used), so a byte-for-byte compare here is the index-shaped sibling of <see cref="ColumnShape"/>/
    /// <see cref="ConstraintShape"/> above (PLAN T363 carry-forward — this file's own parity pin never
    /// covered <c>pg_indexes</c> before this scenario, per the T362 review note that flagged it).
    /// </summary>
    sealed record IndexShape
    {
        public string Indexname { get; init; } = "";
        public string Indexdef { get; init; } = "";
    }

    static async Task<IndexShape> CaptureIndexAsync(
        NpgsqlDataSource dataSource, string schema, string table, string indexName)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<IndexShape>(
            """
            select indexname, indexdef from pg_indexes
            where schemaname = @schema and tablename = @table and indexname = @indexName
            """,
            new { schema, table, indexName });
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the index mirror: PLAN T363's own T362 carry-forward. This file's own parity pin
    // above (ScenarioFreshInstallAndUpgradeProduceTheIdenticalShape) only ever compared columns/
    // constraints on ONE table, never a single pg_indexes row anywhere — so db/06's own copy of
    // station.booth_log_show_track_started (SPEC F152.5, STORY-373) and db/01's own copy of
    // library.media_dup_keys (SPEC F153.5, STORY-376) could drift from db/41's in-place-upgrade
    // recreation of either and nothing here would ever have caught it. Each fact captures the
    // fresh-install index (the CURRENT db/01/db/06's own CREATE INDEX — DatabaseFixture boots from
    // only those two files, so this instant IS the fresh-install world), drops it (simulating a
    // pre-Gardener upgrade box that never had it), reruns db/41 (idempotent — proven convergent
    // already), and asserts db/41's own recreation is byte-identical to what shipped fresh — expected
    // GREEN today; a red here means the two scripts' CREATE INDEX text has drifted and the MIRROR
    // needs fixing, never this fact.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIndexMirrorMatchesTheUpgradePath(DatabaseFixture db)
    {
        [Fact]
        public async Task BoothLogShowTrackStartedSurvivesADropAndDb41Rerun()
        {
            var fresh = await CaptureIndexAsync(
                db.StationDataSource, "station", "booth_log", "booth_log_show_track_started");

            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("drop index station.booth_log_show_track_started");

            RunGardenerMigrationScript(db);

            var upgraded = await CaptureIndexAsync(
                db.StationDataSource, "station", "booth_log", "booth_log_show_track_started");

            Assert.Equal(fresh, upgraded);
        }

        [Fact]
        public async Task MediaDupKeysSurvivesADropAndDb41Rerun()
        {
            var fresh = await CaptureIndexAsync(db.DataSource, "library", "media", "media_dup_keys");

            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("drop index library.media_dup_keys");

            RunGardenerMigrationScript(db);

            var upgraded = await CaptureIndexAsync(db.DataSource, "library", "media", "media_dup_keys");

            Assert.Equal(fresh, upgraded);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — seeding from an active persona (F91.6)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSeedingFromActiveId(DatabaseFixture db)
    {
        // Given Station:Persona:ActiveId > 0 referencing an existing persona, When db/27 runs.

        async Task<long> ArrangeAsync()
        {
            await db.ResetStationAsync();
            await ClearActiveIdSettingAsync(db);
            await DropScheduleTableAsync(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Seeded DJ");
            await SetActiveIdSettingAsync(db, personaId);

            RunMigrationScript(db);
            return personaId;
        }

        [Fact]
        public async Task SevenAllDayRowsExistForThatPersona()
        {
            var personaId = await ArrangeAsync();

            var rows = await ReadAllScheduleRowsAsync(db);

            Assert.Equal(7, rows.Count);
            Assert.Equal([0, 1, 2, 3, 4, 5, 6], rows.Select(r => r.DayOfWeek).OrderBy(d => d));
            Assert.All(rows, r =>
            {
                Assert.Equal(0, r.StartMinute);
                Assert.Equal(1440, r.EndMinute);
                Assert.Equal(personaId, r.PersonaId);
            });
        }

        [Fact]
        public async Task SeededRowsCarryNullEnvelopeFields()
        {
            await ArrangeAsync();

            var rows = await ReadAllScheduleRowsAsync(db);

            Assert.All(rows, r =>
            {
                Assert.Null(r.Genres);
                Assert.Null(r.EnergyMin);
                Assert.Null(r.EnergyMax);
            });
        }

        [Fact]
        public async Task TheSettingsKeyRowIsDeleted()
        {
            await ArrangeAsync();

            Assert.False(await ActiveIdKeyExistsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no active DJ to migrate (F91.4's "empty grid" state)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEmptyWhenNoActiveDj(DatabaseFixture db)
    {
        // Given Station:Persona:ActiveId absent or 0, When db/27 runs. Exercised here with the key
        // explicitly present and 0 — a stronger case than "never written" (which trivially satisfies
        // "the key is gone" without the migration having to do anything).

        async Task ArrangeAsync()
        {
            await db.ResetStationAsync();
            await ClearActiveIdSettingAsync(db);
            await DropScheduleTableAsync(db);
            await SetActiveIdSettingAsync(db, 0);

            RunMigrationScript(db);
        }

        [Fact]
        public async Task ScheduleTableIsEmpty()
        {
            await ArrangeAsync();

            Assert.Empty(await ReadAllScheduleRowsAsync(db));
        }

        [Fact]
        public async Task TheSettingsKeyRowIsStillDeleted()
        {
            await ArrangeAsync();

            Assert.False(await ActiveIdKeyExistsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a stale/dangling ActiveId must not abort the migration (review finding F2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDanglingActiveIdIsIgnored(DatabaseFixture db)
    {
        // Given Station:Persona:ActiveId holds an id that names NO persona row (e.g. the persona was
        // deleted some other way after the key was last written — a real state a long-lived
        // installation can reach before this release's ON DELETE RESTRICT existed to prevent it), When
        // db/27 runs. The seed step's persona-EXISTS guard must skip the INSERT entirely: an unguarded
        // insert would violate segment_schedule's persona_id foreign key, aborting the whole migration
        // (exit 3) and taking migrate.sh's fail-fast down with it on every real upgrade that still
        // carries a stale key — this fact is what pins that guard in place.

        async Task ArrangeAsync()
        {
            await db.ResetStationAsync();
            await ClearActiveIdSettingAsync(db);
            await DropScheduleTableAsync(db);
            await SetActiveIdSettingAsync(db, 999_999);

            // RunFileInContainer throws on a nonzero exit code, so simply RETURNING here — rather than
            // throwing — is itself the proof this run exited 0.
            RunMigrationScript(db);
        }

        [Fact]
        public async Task MigrationExitsZeroAndLeavesAnEmptyGrid()
        {
            await ArrangeAsync();

            Assert.Empty(await ReadAllScheduleRowsAsync(db));
        }

        [Fact]
        public async Task TheDanglingKeyRowIsStillDeleted()
        {
            await ArrangeAsync();

            Assert.False(await ActiveIdKeyExistsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — migration house rule: a second run is a safe no-op
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioIdempotentReRun(DatabaseFixture db)
    {
        [Fact]
        public async Task SecondRunChangesNothingAndDoesNotError()
        {
            // Given a first run that seeded seven rows from an active persona...
            await db.ResetStationAsync();
            await ClearActiveIdSettingAsync(db);
            await DropScheduleTableAsync(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Idempotent DJ");
            await SetActiveIdSettingAsync(db, personaId);
            RunMigrationScript(db);

            var before = await ReadAllScheduleRowsAsync(db);
            Assert.Equal(7, before.Count);

            // When the migration runs again (RunFileInContainer throws on a nonzero exit code, so
            // simply RETURNING here — rather than throwing — is itself the proof this run exited 0)...
            RunMigrationScript(db);

            // Then the grid is untouched — no second set of seven rows on top of the first — and the
            // settings key stays gone.
            var after = await ReadAllScheduleRowsAsync(db);
            Assert.Equal(7, after.Count);
            Assert.False(await ActiveIdKeyExistsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the ultimate drift detector: db/06 and db/27 must build the IDENTICAL table
    // (review finding F1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshInstallAndUpgradeProduceTheIdenticalShape(DatabaseFixture db)
    {
        // Every OTHER fact in this file only ever exercises db/27, never db/06 — a real fresh install
        // never runs db/27 at all; it picks up station.segment_schedule from db/06's own copy of the
        // CREATE TABLE instead (see this file's own header remarks). That leaves db/06's copy asserted
        // by NOTHING: it could drift from db/27's — a column's type, a CHECK bound, the EXCLUDE opclass
        // list, the FK's ON DELETE action — and every other fact here would keep passing, because they
        // all read the db/27-created table. This fact drops the table, builds it via db/06, captures
        // its exact shape, proves the EXCLUDE + FK constraints have teeth on THAT copy, then drops and
        // rebuilds it via db/27 THEN db/33 (STORY-304/T219 added segment_schedule.show_id after db/27's
        // original CREATE TABLE — an upgrading box reaches db/06's shape only once both have run) and
        // asserts the two captured shapes are equal.

        [Fact]
        public async Task Db06AndDb27CreateByteIdenticalColumnsAndConstraints()
        {
            await db.ResetStationAsync();
            await DropScheduleTableAsync(db);

            RunFreshInstallScript(db);
            var db06Columns = await CaptureColumnShapeAsync(db);
            var db06Constraints = await CaptureConstraintShapeAsync(db);

            // Teeth, against the db/06-created copy specifically — proves the constraints captured
            // above actually enforce, not merely that their pg_get_constraintdef TEXT looks right.
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Db06 Teeth DJ");
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync(
                    """
                    insert into station.segment_schedule (day_of_week, start_minute, end_minute, persona_id)
                    values (1, 0, 600, @personaId)
                    """,
                    new { personaId });

                await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                    "insert into station.segment_schedule (day_of_week, start_minute, end_minute) values (1, 300, 900)"));

                await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                    "delete from station.persona where id = @personaId", new { personaId }));
            }

            await DropScheduleTableAsync(db);
            RunMigrationScript(db);
            RunShowAndSegmentKindMigrationScript(db);
            var db27Columns = await CaptureColumnShapeAsync(db);
            var db27Constraints = await CaptureConstraintShapeAsync(db);

            Assert.Equal(db06Columns, db27Columns);
            Assert.Equal(db06Constraints, db27Constraints);

            // Table is left present (db/27-then-db/33-created) — matches every other scenario in this
            // file's own convention of leaving the table present.
        }
    }
}
