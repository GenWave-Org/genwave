// STORY-317 — Dated specials shadow the grid (F120) — store half · 🪂 DROPPABLE SLICE
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. Implements
// PLAN T258's store half: db/36 (station.schedule_special — its OWN migration, F120.1/F120.5, NO
// db/01/db/06 mirror — see this file's own ScenarioMigrationConvergence and db/36's own header for the
// honest fresh-install mechanism) + SpecialsRepository/IScheduleSpecialStore. Mirrors
// Story240_ScheduleStore.cs's own shape (SelectColumns join, EXCLUDE-has-teeth idiom) throughout; the
// resolver rung half lives in Orchestration.Tests/Story317_SpecialsRung.cs.
//
// DROPPABILITY INVENTORY (F120.5, PLAN T258 review should-fix 8): dropping this slice removes db/36,
// this file, ScheduleResolver's specials rung + Story317_SpecialsRung.cs, SpecialsRepository/
// SpecialsServiceCollectionExtensions/IScheduleSpecialStore/ScheduleSpecial, DateOnlyTypeHandler (no
// other DateOnly-typed column exists yet), and its two registration call sites
// (MediaLibraryServiceCollectionExtensions.AddMediaLibrary, DatabaseFixture.InitializeAsync) — plus one
// easy-to-miss TEST-ONLY piece of collateral: DatabaseFixture.ResetSpecialsAsync, which has no
// production counterpart to key the search off of.

using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureSpecialsStore
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Runs db/36 (idempotent — CREATE EXTENSION/TABLE IF NOT EXISTS) before wiring the repository,
    /// mirroring Story240_ScheduleStore.cs's own <c>Repo</c> helper — db/33 + db/35 are re-run first for
    /// the identical reason that file's own header documents (a sibling in-place scenario elsewhere in
    /// this DatabaseCollection may have transiently altered station.show's columns; SpecialsRepository's
    /// own SelectColumns LEFT JOINs against it exactly like ScheduleRepository does). Callers reset
    /// station.schedule_special AFTER calling this helper, never before — the table does not exist on a
    /// database that has never run db/36 even once.
    /// </summary>
    static SpecialsRepository Repo(DatabaseFixture db)
    {
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "33-show-and-segment-kind-migration.sh"));
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "35-show-identity-migration.sh"));
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "36-schedule-special-migration.sh"));
        return new SpecialsRepository(
            new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<SpecialsRepository>.Instance);
    }

    static async Task<long> InsertShowAsync(DatabaseFixture db, string name, string slug)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into station.show (name, slug) values (@name, @slug) returning id::bigint",
            new { name, slug });
    }

    /// <summary>PLAN T360 review MED-3 — <see cref="InsertShowAsync"/>'s own sibling for seeding
    /// <c>envelope</c> directly (raw SQL — <c>SpecialsRepository</c> has no writer for it at all).</summary>
    static async Task<long> InsertShowWithEnvelopeAsync(DatabaseFixture db, string name, string slug, string envelopeJson)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into station.show (name, slug, envelope) values (@name, @slug, @envelope::jsonb) returning id::bigint",
            new { name, slug, envelope = envelopeJson });
    }

    static ScheduleSpecial Draft(
        DateOnly onDate, int start, int end, long? personaId = null, long? showId = null,
        string[]? genres = null, double? energyMin = null, double? energyMax = null) =>
        new(null, onDate, start, end, personaId, genres, energyMin, energyMax, ShowId: showId);

    /// <summary>
    /// Unwraps a HAPPY-PATH <see cref="SpecialsRepository.CreateAsync"/> call down to the persisted
    /// row (PLAN T259 correction: <c>CreateAsync</c> now returns <see cref="ScheduleSpecialCreateResult"/>,
    /// not a bare <see cref="ScheduleSpecial"/> — see that type's own remarks) — every Fact in this
    /// file that expects a create to SUCCEED calls through here rather than pattern-matching the
    /// result itself at each call site; <see cref="ScenarioRejectingOverlap"/>'s own sad-path Fact
    /// asserts the <see cref="ScheduleSpecialCreateResult.Overlap"/> case directly instead, since a
    /// REJECTED create is exactly the claim under test there.
    /// </summary>
    static async Task<ScheduleSpecial> CreateSpecialAsync(SpecialsRepository repo, ScheduleSpecial draft)
    {
        var result = await repo.CreateAsync(draft, CancellationToken.None);
        return Assert.IsType<ScheduleSpecialCreateResult.Created>(result).Special;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — round trip (SPEC F120.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDatedRows(DatabaseFixture db)
    {
        [Fact]
        public async Task ASpecialRoundTripsWithDateSpanPersonaShowEnvelope()
        {
            // Given a special for 2026-12-24 19:00-21:00 with persona/show/envelope
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Holiday DJ");
            var showId = await InsertShowAsync(db, "Christmas Eve Countdown", "christmas-eve-countdown");
            var onDate = new DateOnly(2026, 12, 24);
            var draft = Draft(onDate, 19 * 60, 21 * 60, personaId, showId, ["holiday", "jazz"], 0.2, 0.7);

            // When it is written and re-read
            var created = await CreateSpecialAsync(repo, draft);
            var upcoming = await repo.ListUpcomingAsync(onDate, CancellationToken.None);

            // Then every field round-trips; minutes obey the 30-min steps (F91 mirrored)
            var stored = Assert.Single(upcoming);
            Assert.Equal(created.Id, stored.Id);
            Assert.NotNull(stored.Id);
            Assert.Equal(onDate, stored.OnDate);
            Assert.Equal(19 * 60, stored.StartMinute);
            Assert.Equal(21 * 60, stored.EndMinute);
            Assert.Equal(personaId, stored.PersonaId);
            Assert.Equal(["holiday", "jazz"], stored.Genres);
            Assert.Equal(0.2, stored.EnergyMin);
            Assert.Equal(0.7, stored.EnergyMax);
            Assert.Equal(showId, stored.ShowId);
            Assert.NotNull(stored.Show);
            Assert.Equal("Christmas Eve Countdown", stored.Show.Name);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the show's own rotation rule rides the specials join too (PLAN T360 review MED-3)
    //
    // The production READ path (sh.envelope ->> 'rotation' in SpecialsRepository.SelectColumns/
    // CreateAsync's own CTE) had zero live-Postgres coverage — every prior specials-side rotation fact
    // either hand-built ShowSummary in memory (Orchestration.Tests) or never touched Rotation at all.
    // Both facts below share one show so the pairing is non-vacuous: the SAME code path returns a real
    // predicate when envelope carries a "rotation" key and null when it carries something else.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheRotationRuleRidesTheJoin(DatabaseFixture db)
    {
        [Fact]
        public async Task ARealRotationKeyReadsBackThroughListUpcomingAsync()
        {
            // Given a special naming a show whose envelope carries {"rotation":{"maxPlays":0}}
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var showId = await InsertShowWithEnvelopeAsync(
                db, "Deep Cuts Special", "deep-cuts-special", """{"rotation":{"maxPlays":0}}""");
            var onDate = new DateOnly(2026, 12, 24);
            var draft = Draft(onDate, 19 * 60, 21 * 60, showId: showId);

            // When it is written and re-read through the real repository
            await CreateSpecialAsync(repo, draft);
            var upcoming = await repo.ListUpcomingAsync(onDate, CancellationToken.None);

            // Then the resolved show carries the rotation rule
            var stored = Assert.Single(upcoming);
            Assert.Equal(new RotationPredicate(MaxPlays: 0), stored.Show?.Rotation);
        }

        [Fact]
        public async Task ANonRotationEnvelopeKeyLeavesRotationNull()
        {
            // Given a DIFFERENT special naming a show whose envelope carries a key that is NOT
            // "rotation" (SPEC F115.2's dormant-columns-unread pin — this is the "inverse" pairing:
            // envelope is non-null, but Rotation must still read back null)
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var showId = await InsertShowWithEnvelopeAsync(
                db, "Ordinary Special", "ordinary-special", """{"genres":["Jazz"]}""");
            var onDate = new DateOnly(2026, 12, 25);
            var draft = Draft(onDate, 19 * 60, 21 * 60, showId: showId);

            // When it is written and re-read through the real repository
            await CreateSpecialAsync(repo, draft);
            var upcoming = await repo.ListUpcomingAsync(onDate, CancellationToken.None);

            // Then the resolved show's Rotation is null — the read path genuinely looks at the
            // "rotation" key specifically, not just "envelope is non-null"
            var stored = Assert.Single(upcoming);
            Assert.Null(stored.Show?.Rotation);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the schema's own per-date EXCLUDE has teeth (SPEC F120.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRejectingOverlap(DatabaseFixture db)
    {
        [Fact]
        public async Task OverlappingSpecialsOnADateAreRejectedByTheDatabase()
        {
            // Given a special already covering a span on a date
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var onDate = new DateOnly(2026, 7, 4);
            await CreateSpecialAsync(repo, Draft(onDate, 600, 900));

            // When a second special overlaps it — the per-date EXCLUDE guard rejects at the database
            // (F120.1), translated by CreateAsync itself into ScheduleSpecialCreateResult.Overlap
            // (PLAN T259 — see that type's own remarks); the weekly table's own invariant is untouched
            // by construction (a wholly separate constraint on a wholly separate table, never
            // consulted here).
            var result = await repo.CreateAsync(Draft(onDate, 750, 1050), CancellationToken.None);
            Assert.IsType<ScheduleSpecialCreateResult.Overlap>(result);
        }

        [Fact]
        public async Task TheSameSpanOnADifferentDateDoesNotConflict()
        {
            // Given a special covering a span on one date — the per-date EXCLUDE guard's "per-date"
            // half: the identical span on a DIFFERENT date must never collide with it.
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            await CreateSpecialAsync(repo, Draft(new DateOnly(2026, 7, 4), 600, 900));

            // When a second special repeats the exact same start/end on a different date
            var second = await CreateSpecialAsync(repo, Draft(new DateOnly(2026, 7, 5), 600, 900));

            // Then it is accepted — no rejection, and both rows persist independently
            Assert.NotNull(second.Id);
            var upcoming = await repo.ListUpcomingAsync(new DateOnly(2026, 7, 4), CancellationToken.None);
            Assert.Equal(2, upcoming.Count);
        }

        [Fact]
        public async Task AdjacentSpecialsOnTheSameDateDoNotConflict()
        {
            // Given a special ending exactly at 900 (15:00) on a date — int4range's own half-open
            // [start, end) semantics (PLAN T258 review should-fix 6): a second span whose start EQUALS
            // the first's own end must never collide, the same "touching, not overlapping" boundary
            // station.segment_schedule's own EXCLUDE already carries for the weekly grid.
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var onDate = new DateOnly(2026, 7, 4);
            await CreateSpecialAsync(repo, Draft(onDate, 600, 900));

            // When a second special starts exactly where the first ends
            var second = await CreateSpecialAsync(repo, Draft(onDate, 900, 1200));

            // Then it is accepted — no rejection, and both rows persist independently on the same date
            Assert.NotNull(second.Id);
            var upcoming = await repo.ListUpcomingAsync(onDate, CancellationToken.None);
            Assert.Equal(2, upcoming.Count);
        }
    }

    // ---------------------------------------------------------------------
    // CRUD — delete + the change-notification seam (mirrors Story240_ScheduleStore's own idiom)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDelete(DatabaseFixture db)
    {
        [Fact]
        public async Task DeletingARemovedSpecialLeavesNoTraceAndRaisesTheChangeEvent()
        {
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var fired = 0;
            repo.SpecialsChanged += () => fired++;
            var created = await CreateSpecialAsync(repo, Draft(new DateOnly(2026, 3, 1), 60, 120));
            Assert.NotNull(created.Id);
            fired = 0; // isolate the delete's own notification from the create above

            var deleted = await repo.DeleteAsync(created.Id.Value, CancellationToken.None);

            Assert.True(deleted);
            Assert.Equal(1, fired);
            Assert.Empty(await repo.ListUpcomingAsync(new DateOnly(2026, 3, 1), CancellationToken.None));
        }

        [Fact]
        public async Task DeletingAnUnknownIdReportsFalseAndRaisesNoEvent()
        {
            var repo = Repo(db);
            await db.ResetSpecialsAsync();
            var fired = 0;
            repo.SpecialsChanged += () => fired++;

            var deleted = await repo.DeleteAsync(999_999, CancellationToken.None);

            Assert.False(deleted);
            Assert.Equal(0, fired);
        }
    }

    // ---------------------------------------------------------------------
    // MIGRATION CONVERGENCE (SPEC F120.1/F120.5, PLAN T258 gate) — the db/35 precedent, adapted: db/36
    // ships with NO db/01/db/06 mirror at all (unlike db/35, which widens a table db/06 already
    // creates), so there is no second DDL copy to prove agreement against. The honest mechanism instead
    // (see db/36's own header): migrate.sh applies EVERY db/*-migration.sh, this one included, on every
    // launch.sh run — fresh volume or existing one — because only db/01/db/06 are mounted as Postgres's
    // own docker-entrypoint-initdb.d scripts. A fresh install and an upgrading install therefore reach
    // station.schedule_special through the exact same single code path; "converge" here means "there is
    // only ever one DDL statement to run," proven by (a) the fresh-init snapshot carrying no trace of
    // this table at all, and (b) the migration script itself being safe to (re-)run against that
    // snapshot and producing the shape SPEC F120.1 promises.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationConvergence(DatabaseFixture db)
    {
        [Fact]
        public void TheFreshInitSnapshotCarriesNoTraceOfScheduleSpecial()
        {
            // DatabaseFixture.InitialSchema is captured once, immediately after Postgres finishes
            // running ONLY db/01 + db/06 (db-compose.yaml's own docker-entrypoint-initdb.d mount) and
            // before any spec class — this one included — ever runs db/36. SPEC F120.5's "ships ONLY
            // with this slice" promise turns red here the instant anyone adds a db/06 mirror.
            Assert.DoesNotContain(db.InitialSchema.Keys, key => key.Table == "schedule_special");
        }

        [Fact]
        public async Task TheMigrationScriptIsIdempotentAndCreatesThePromisedShape()
        {
            // Running it twice must be a safe no-op the second time (every migration file's own "safe
            // to run multiple times" promise) — proven here directly, independent of Repo()'s own
            // per-fact re-run elsewhere in this file.
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "36-schedule-special-migration.sh"));
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "36-schedule-special-migration.sh"));

            await using var conn = await db.StationDataSource.OpenConnectionAsync();

            // The per-date EXCLUDE guard (SPEC F120.1) — a GiST exclusion constraint (contype 'x')
            // exists on the table at all, independent of ScenarioRejectingOverlap's own behavioral proof
            // above.
            var hasExclude = await conn.ExecuteScalarAsync<bool>(
                "select exists(select 1 from pg_constraint where conrelid = 'station.schedule_special'::regclass and contype = 'x')");
            Assert.True(hasExclude, "station.schedule_special is missing its per-date EXCLUDE constraint.");

            // Both FKs are RESTRICT (confdeltype 'r') — SPEC F120.1's own "RESTRICT FKs" promise: a
            // persona/show a future special still names can never be deleted out from under it, the
            // same protection station.segment_schedule's own FKs already carry (db/06).
            var restrictFkCount = await conn.ExecuteScalarAsync<int>(
                """
                select count(*)::int from pg_constraint
                where conrelid = 'station.schedule_special'::regclass and contype = 'f' and confdeltype = 'r'
                """);
            Assert.Equal(2, restrictFkCount);
        }
    }
}
