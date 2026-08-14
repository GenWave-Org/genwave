// STORY-118 — Personas persist in their own station table
//
// BDD specification — xUnit (REAL Postgres via the shared DatabaseFixture harness —
// the S2/R8/Q2 fake-vs-wire lesson: schema-path convergence and UNIQUE teeth are
// invisible to fakes). T2 lands db/09 + PersonaRepository. See docs/PLAN.md Epic T.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePersonaStorage
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// gh-#462: <see cref="PersonaRepository.DeleteAsync"/> now ALWAYS pre-queries
    /// <c>station.schedule_special</c> too, alongside <c>station.segment_schedule</c> — so every
    /// scenario in this file that reaches <c>DeleteAsync</c>, not just the specials-specific ones
    /// below, now needs db/36's table to exist first. Runs it here (idempotent — CREATE TABLE IF NOT
    /// EXISTS — mirrors Story317_SpecialsStore.cs's own <c>Repo</c> helper) rather than only in the
    /// scenario classes that insert a special row.
    /// </summary>
    static PersonaRepository Repo(DatabaseFixture db)
    {
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "36-schedule-special-migration.sh"));
        return new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));
    }

    /// <summary>
    /// Returns (data_type, is_nullable, column_default) for the named column on
    /// <c>station.persona</c>, or null when the column does not exist. Queried via
    /// <see cref="DatabaseFixture.StationDataSource"/> (station_svc owns the table, so
    /// information_schema.columns is visible without the pg_catalog workaround Story042 needed).
    /// </summary>
    static async Task<(string DataType, string IsNullable, string? ColumnDefault)?> QueryColumnAsync(
        DatabaseFixture db, string columnName)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string data_type, string is_nullable, string? column_default)>(
            @"SELECT data_type, is_nullable, column_default
              FROM information_schema.columns
              WHERE table_schema = 'station'
                AND table_name   = 'persona'
                AND column_name  = @col",
            new { col = columnName });

        return row == default ? null : (row.data_type, row.is_nullable, row.column_default);
    }

    static async Task<bool> TableExistsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            @"SELECT count(*) FROM information_schema.tables
              WHERE table_schema = 'station' AND table_name = 'persona'");
        return count > 0;
    }

    static async Task<bool> HasUniqueNameConstraintAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var count = await conn.ExecuteScalarAsync<long>(
            @"SELECT count(*)
              FROM information_schema.table_constraints
              WHERE table_schema = 'station'
                AND table_name   = 'persona'
                AND constraint_type = 'UNIQUE'");
        return count > 0;
    }

    /// <summary>Runs db/09-persona-migration.sh against the test database via the fixture.</summary>
    static void RunMigrationScript(DatabaseFixture db)
    {
        var scriptPath = Path.Combine(db.RepoRoot, "db", "09-persona-migration.sh");
        db.RunFileInContainer(scriptPath);
    }

    static PersonaDraft Draft(string name, string backstory = "b", string style = "s", string voice = "") =>
        new(name, backstory, style, voice);

    // ---------------------------------------------------------------------
    // HAPPY PATH — schema shape and CRUD round-trip
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSchemaShapeOnAFreshDatabase(DatabaseFixture db)
    {
        [Fact]
        public async Task PersonaTableExistsWithTheF351Shape()
        {
            // id serial PK, name text unique not null, backstory/style/voice text not null
            // default '', created_at/updated_at timestamptz not null default now() (F35.1, AC1).
            Assert.True(await TableExistsAsync(db));

            var name = await QueryColumnAsync(db, "name");
            Assert.NotNull(name);
            Assert.Equal("text", name.Value.DataType);
            Assert.Equal("NO", name.Value.IsNullable);
            Assert.True(await HasUniqueNameConstraintAsync(db));

            foreach (var column in new[] { "backstory", "style", "voice" })
            {
                var col = await QueryColumnAsync(db, column);
                Assert.NotNull(col);
                Assert.Equal("text", col.Value.DataType);
                Assert.Equal("NO", col.Value.IsNullable);
                Assert.NotNull(col.Value.ColumnDefault);
                Assert.Contains("''", col.Value.ColumnDefault);
            }

            foreach (var column in new[] { "created_at", "updated_at" })
            {
                var col = await QueryColumnAsync(db, column);
                Assert.NotNull(col);
                Assert.Equal("timestamp with time zone", col.Value.DataType);
                Assert.Equal("NO", col.Value.IsNullable);
                Assert.NotNull(col.Value.ColumnDefault);
                Assert.Contains("now", col.Value.ColumnDefault, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task MigrationScriptIsIdempotent()
        {
            // db/09-persona-migration.sh applied twice → one table, both runs clean (F35.1, AC1).
            RunMigrationScript(db);
            RunMigrationScript(db);

            Assert.True(await TableExistsAsync(db));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — in-place migration (db/09-persona-migration.sh)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationAddsTheTableInPlace(DatabaseFixture db)
    {
        [Fact]
        public async Task MigrationCreatesTheTableOnAPreT2Schema()
        {
            // Simulate pre-migration: DROP TABLE IF EXISTS station.persona. Station_svc owns the
            // table (db/06 ran `set role station_svc` before creating it), so drop it over the same
            // station_svc-authenticated connection the repository itself uses. CASCADE (STORY-192):
            // once station.persona_memory exists (SPEC F71.1) its FK into station.persona blocks a
            // plain DROP; CASCADE drops that FK along with the table, exactly like a real operator
            // dropping and recreating a very old station.persona would need to.
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("DROP TABLE IF EXISTS station.persona CASCADE");

            Assert.False(await TableExistsAsync(db));

            // Run db/09-persona-migration.sh via DatabaseFixture.RunFileInContainer — this is the
            // CREATE TABLE branch db/06's cumulative fresh-init mount never exercises (the table
            // already exists by the time any other spec runs db/09, so `if not exists` always
            // no-ops there). Only this drop-then-run proves db/09's own column list is correct.
            RunMigrationScript(db);

            // STORY-192: db/09 alone only ever recreates the pre-F71.1 shape (it predates the card
            // schema) — a real operator upgrading a database this old would run every subsequent
            // numbered script in order, so re-apply db/11 too. This also restores
            // station.persona_memory (dropped above via CASCADE) for every other spec sharing this
            // database, regardless of test execution order.
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "11-persona-card-migration.sh"));

            // After: the table exists with the same F35.1 shape as fresh init (both paths converge).
            Assert.True(await TableExistsAsync(db));

            var name = await QueryColumnAsync(db, "name");
            Assert.NotNull(name);
            Assert.Equal("text", name.Value.DataType);
            Assert.Equal("NO", name.Value.IsNullable);
            Assert.True(await HasUniqueNameConstraintAsync(db));

            foreach (var column in new[] { "backstory", "style", "voice" })
            {
                var col = await QueryColumnAsync(db, column);
                Assert.NotNull(col);
                Assert.Equal("text", col.Value.DataType);
                Assert.Equal("NO", col.Value.IsNullable);
                Assert.NotNull(col.Value.ColumnDefault);
                Assert.Contains("''", col.Value.ColumnDefault);
            }

            foreach (var column in new[] { "created_at", "updated_at" })
            {
                var col = await QueryColumnAsync(db, column);
                Assert.NotNull(col);
                Assert.Equal("timestamp with time zone", col.Value.DataType);
                Assert.Equal("NO", col.Value.IsNullable);
                Assert.NotNull(col.Value.ColumnDefault);
                Assert.Contains("now", col.Value.ColumnDefault, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCrudRoundTrip(DatabaseFixture db)
    {
        [Fact]
        public async Task CreateThenReadReturnsTheRow()
        {
            // (AC2).
            await db.ResetStationAsync();
            var repo = Repo(db);

            var outcome = await repo.CreateAsync(
                new PersonaDraft("Radio Rick", "grew up on late-night AM", "wry", "warm-male"),
                CancellationToken.None);

            var created = Assert.IsType<PersonaWriteResult.Created>(outcome);
            Assert.Equal("Radio Rick", created.Persona.Name);
            Assert.Equal("grew up on late-night AM", created.Persona.Backstory);
            Assert.Equal("wry", created.Persona.Style);
            Assert.Equal("warm-male", created.Persona.Voice);

            var read = await repo.GetByIdAsync(created.Persona.Id, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(created.Persona, read);
        }

        [Fact]
        public async Task UpdateAdvancesUpdatedAt()
        {
            // rename/restyle → read reflects it and updated_at moves (AC2).
            await db.ResetStationAsync();
            var repo = Repo(db);

            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("Original Name"), CancellationToken.None));

            var outcome = await repo.UpdateAsync(
                created.Persona.Id,
                new PersonaDraft("Renamed", "new backstory", "new style", "new-voice"),
                CancellationToken.None);

            var updated = Assert.IsType<PersonaWriteResult.Updated>(outcome);
            Assert.Equal("Renamed", updated.Persona.Name);
            Assert.Equal("new backstory", updated.Persona.Backstory);
            Assert.Equal("new style", updated.Persona.Style);
            Assert.Equal("new-voice", updated.Persona.Voice);
            Assert.True(updated.Persona.UpdatedAt > created.Persona.UpdatedAt);

            var read = await repo.GetByIdAsync(created.Persona.Id, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal("Renamed", read.Name);
        }

        [Fact]
        public async Task DeleteRemovesTheRow()
        {
            // (AC2).
            await db.ResetStationAsync();
            var repo = Repo(db);

            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("To Be Deleted"), CancellationToken.None));

            var outcome = await repo.DeleteAsync(created.Persona.Id, CancellationToken.None);

            Assert.IsType<PersonaWriteResult.Deleted>(outcome);
            Assert.Null(await repo.GetByIdAsync(created.Persona.Id, CancellationToken.None));
        }

        [Fact]
        public async Task DeleteOfAnUnknownIdReportsNotFound()
        {
            await db.ResetStationAsync();
            var repo = Repo(db);

            var outcome = await repo.DeleteAsync(999_999L, CancellationToken.None);

            Assert.IsType<PersonaWriteResult.NotFound>(outcome);
        }

        [Fact]
        public async Task UpdateOfAnUnknownIdReportsNotFound()
        {
            await db.ResetStationAsync();
            var repo = Repo(db);

            var outcome = await repo.UpdateAsync(999_999L, Draft("Ghost"), CancellationToken.None);

            Assert.IsType<PersonaWriteResult.NotFound>(outcome);
        }

        [Fact]
        public async Task VoiceDefaultsToEmptyString()
        {
            // created without voice → reads '' (station-default sentinel, F35.1, AC3).
            await db.ResetStationAsync();

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var voice = await conn.ExecuteScalarAsync<string>(
                "insert into station.persona (name) values (@name) returning voice",
                new { name = "Default Voice DJ" });

            Assert.Equal("", voice);
        }

        [Fact]
        public async Task GetAllReturnsEveryPersonaOrderedByName()
        {
            await db.ResetStationAsync();
            var repo = Repo(db);
            await repo.CreateAsync(Draft("Zeta"), CancellationToken.None);
            await repo.CreateAsync(Draft("Alpha"), CancellationToken.None);

            var all = await repo.GetAllAsync(CancellationToken.None);

            Assert.Equal(2, all.Count);
            Assert.Equal("Alpha", all[0].Name);
            Assert.Equal("Zeta", all[1].Name);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the database defends the invariants
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDuplicateName(DatabaseFixture db)
    {
        [Fact]
        public async Task SecondInsertWithTheSameNameIsRejectedByUnique()
        {
            // UNIQUE(name) teeth at the DB, not just the API (F35.4, AC4).
            await db.ResetStationAsync();

            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "insert into station.persona (name) values (@name)", new { name = "Duplicate DJ" });

            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into station.persona (name) values (@name)", new { name = "Duplicate DJ" }));
        }

        [Fact]
        public async Task CreatingADuplicateNameThroughTheRepositoryReturnsNameConflictNotAnException()
        {
            // The repository's name-conflict outcome (create duplicate via repo -> conflict outcome).
            await db.ResetStationAsync();
            var repo = Repo(db);

            await repo.CreateAsync(Draft("Collision DJ"), CancellationToken.None);
            var outcome = await repo.CreateAsync(Draft("Collision DJ"), CancellationToken.None);

            Assert.IsType<PersonaWriteResult.NameConflict>(outcome);
        }

        [Fact]
        public async Task RenamingToAnExistingNameThroughTheRepositoryReturnsNameConflict()
        {
            await db.ResetStationAsync();
            var repo = Repo(db);

            await repo.CreateAsync(Draft("Existing Name"), CancellationToken.None);
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("To Rename"), CancellationToken.None));

            var outcome = await repo.UpdateAsync(
                created.Persona.Id, Draft("Existing Name"), CancellationToken.None);

            Assert.IsType<PersonaWriteResult.NameConflict>(outcome);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — F91.9's FK guard (PLAN T121; widened F120.1/gh-#462): DeleteAsync queries BOTH
    // station.segment_schedule AND station.schedule_special and names the offending slots/specials.
    // Not the FK exception path itself — that is a genuine query-then-delete race (a slot/special
    // painted between DeleteAsync's own SELECTs and its DELETE), which is not independently
    // reproducible here without a test-only hook into the repository; Story240's own
    // ScenarioPersonaForeignKeyHasTeeth already proves the database's ON DELETE RESTRICT fires given
    // a direct conflicting row, and PersonaRepository.DeleteAsync's own remarks document the race
    // backstop's shape (re-query both tables, possibly both empty) in full.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDeleteFkGuard(DatabaseFixture db)
    {
        static async Task InsertSlotAsync(DatabaseFixture db, long personaId, int dayOfWeek, int startMinute, int endMinute)
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                """
                insert into station.segment_schedule (day_of_week, start_minute, end_minute, persona_id)
                values (@dayOfWeek, @startMinute, @endMinute, @personaId)
                """,
                new { dayOfWeek, startMinute, endMinute, personaId });
        }

        // gh-#462's own db/36 fixture — mirrors InsertSlotAsync just above, one table over.
        static async Task InsertSpecialAsync(
            DatabaseFixture db, long personaId, DateOnly onDate, int startMinute, int endMinute)
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                """
                insert into station.schedule_special (on_date, start_minute, end_minute, persona_id)
                values (@onDate, @startMinute, @endMinute, @personaId)
                """,
                new { onDate, startMinute, endMinute, personaId });
        }

        [Fact]
        public async Task DeletingAPersonaWithOneScheduleRowReturnsItsSlot()
        {
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("Scheduled DJ"), CancellationToken.None));
            await InsertSlotAsync(db, created.Persona.Id, dayOfWeek: 1, startMinute: 540, endMinute: 720);

            var outcome = await repo.DeleteAsync(created.Persona.Id, CancellationToken.None);

            var scheduled = Assert.IsType<PersonaWriteResult.ScheduledElsewhere>(outcome);
            var slot = Assert.Single(scheduled.Slots);
            Assert.Equal(DayOfWeek.Monday, slot.Day);
            Assert.Equal(540, slot.StartMinute);
            Assert.Equal(720, slot.EndMinute);
        }

        [Fact]
        public async Task DeletingAPersonaWithMultipleScheduleRowsReturnsAllOfThemInDayThenStartOrder()
        {
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("Busy DJ"), CancellationToken.None));
            // Inserted out of order — proves the query orders by day/start_minute, not insert order.
            await InsertSlotAsync(db, created.Persona.Id, dayOfWeek: 2, startMinute: 840, endMinute: 960);
            await InsertSlotAsync(db, created.Persona.Id, dayOfWeek: 1, startMinute: 540, endMinute: 720);

            var outcome = await repo.DeleteAsync(created.Persona.Id, CancellationToken.None);

            var scheduled = Assert.IsType<PersonaWriteResult.ScheduledElsewhere>(outcome);
            Assert.Equal(2, scheduled.Slots.Count);
            Assert.Equal(DayOfWeek.Monday, scheduled.Slots[0].Day);
            Assert.Equal(DayOfWeek.Tuesday, scheduled.Slots[1].Day);
        }

        [Fact]
        public async Task DeletingAPersonaBlockedOnlyByADatedSpecialReturnsIt()
        {
            // gh-#462: a persona referenced ONLY by a dated special (no weekly slot at all) now names
            // it — DeleteAsync's own pre-query reaches station.schedule_special too, not just
            // station.segment_schedule, so this no longer falls through to the FK race-backstop path
            // with nothing to report.
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("Special-Only DJ"), CancellationToken.None));
            await InsertSpecialAsync(db, created.Persona.Id, new DateOnly(2026, 8, 20), 540, 720);

            var outcome = await repo.DeleteAsync(created.Persona.Id, CancellationToken.None);

            var scheduled = Assert.IsType<PersonaWriteResult.ScheduledElsewhere>(outcome);
            Assert.Empty(scheduled.Slots);
            var special = Assert.Single(scheduled.Specials);
            Assert.Equal(new DateOnly(2026, 8, 20), special.OnDate);
            Assert.Equal(540, special.StartMinute);
            Assert.Equal(720, special.EndMinute);
        }

        [Fact]
        public async Task DeletingAPersonaBlockedByBothASlotAndSpecialsReturnsBothCorrectlyOrdered()
        {
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("Double-Blocked DJ"), CancellationToken.None));
            await InsertSlotAsync(db, created.Persona.Id, dayOfWeek: 1, startMinute: 540, endMinute: 720);
            // Inserted out of order — proves the specials query orders by date/start_minute, not insert order.
            await InsertSpecialAsync(db, created.Persona.Id, new DateOnly(2026, 9, 1), 600, 660);
            await InsertSpecialAsync(db, created.Persona.Id, new DateOnly(2026, 8, 20), 540, 720);

            var outcome = await repo.DeleteAsync(created.Persona.Id, CancellationToken.None);

            var scheduled = Assert.IsType<PersonaWriteResult.ScheduledElsewhere>(outcome);
            Assert.Single(scheduled.Slots);
            Assert.Equal(2, scheduled.Specials.Count);
            Assert.Equal(new DateOnly(2026, 8, 20), scheduled.Specials[0].OnDate);
            Assert.Equal(new DateOnly(2026, 9, 1), scheduled.Specials[1].OnDate);
        }

        [Fact]
        public async Task RejectedDeleteLeavesThePersonaRowIntact()
        {
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("Untouchable DJ"), CancellationToken.None));
            await InsertSlotAsync(db, created.Persona.Id, dayOfWeek: 3, startMinute: 0, endMinute: 1440);

            await repo.DeleteAsync(created.Persona.Id, CancellationToken.None);

            var stillThere = await repo.GetByIdAsync(created.Persona.Id, CancellationToken.None);
            Assert.NotNull(stillThere);
            Assert.Equal("Untouchable DJ", stillThere.Name);
        }

        [Fact]
        public async Task ABenchedPersonaWithNoScheduleRowsStillDeletesNormally()
        {
            // The zero-rows path falls straight through to the plain DELETE — pinning that the guard's
            // pre-check never blocks a persona that genuinely holds no schedule row (already implied
            // by ScenarioCrudRoundTrip.DeleteRemovesTheRow above; explicit here alongside the guard's
            // own sad-path facts).
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var created = Assert.IsType<PersonaWriteResult.Created>(
                await repo.CreateAsync(Draft("Bench DJ"), CancellationToken.None));

            var outcome = await repo.DeleteAsync(created.Persona.Id, CancellationToken.None);

            Assert.IsType<PersonaWriteResult.Deleted>(outcome);
        }
    }
}
