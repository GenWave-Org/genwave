// STORY-240 — The grid holds the week: the store beneath the wire (SPEC F91.1, F91.3, F91.8, PLAN T118)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. This
// file owns ScheduleRepository/IScheduleStore semantics ONLY — snapshot load, atomic whole-week
// replace, app-side per-cell validation, the WeekChanged change-notification seam, and the schema's
// own EXCLUDE/FK constraint teeth (proven independently of the repository, by direct SQL).
// The T122 wire contract (GET/PUT /api/schedule through a real HTTP request) is Host.Tests' own
// Story240_GridHoldsTheWeek.cs — out of this file's scope entirely (mirrors Story224's own
// DB-half/API-half split: Story224_RequestStore.cs here, Story224_RequestIntake.cs in Host.Tests).

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScheduleStore
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static ScheduleRepository Repo(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static ScheduleSegment MusicOnly(DayOfWeek day, int start, int end) =>
        new(null, day, start, end, PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);

    static ScheduleSegment Staffed(DayOfWeek day, int start, int end, long personaId) =>
        new(null, day, start, end, personaId, Genres: null, EnergyMin: null, EnergyMax: null);

    static ScheduleSegment WithEnvelope(
        DayOfWeek day, int start, int end, string[]? genres, double? energyMin, double? energyMax) =>
        new(null, day, start, end, PersonaId: null, genres, energyMin, energyMax);

    // ---------------------------------------------------------------------
    // HAPPY PATH — LoadWeekAsync (SPEC F91.3, F91.4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioLoadEmptyWeek(DatabaseFixture db)
    {
        [Fact]
        public async Task AnEmptyGridLoadsAsAnEmptySnapshot()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var snapshot = await repo.LoadWeekAsync(CancellationToken.None);

            Assert.Empty(snapshot.Segments);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ReplaceWeekAsync round trip
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStoringAValidWeek(DatabaseFixture db)
    {
        [Fact]
        public async Task AStoredSegmentComesBackWithAStoreAssignedId()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 0, 1440)], CancellationToken.None);

            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(result);
            var stored = Assert.Single(replaced.Snapshot.Segments);
            Assert.NotNull(stored.Id);
            Assert.Equal(DayOfWeek.Monday, stored.Day);
        }

        [Fact]
        public async Task MusicOnlySegmentsCarryNullPersona()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Tuesday, 0, 1440)], CancellationToken.None);

            var snapshot = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Null(Assert.Single(snapshot.Segments).PersonaId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — genres/energy_min/energy_max round-trip through the write path (SPEC F91.4)
    //
    // Review finding: every other fact in this file passes NULL for all three envelope columns —
    // deleting genres/energy_min/energy_max from ReplaceWeekAsync's INSERT would still pass the whole
    // suite. These facts write real values and read them back, including energy_min/energy_max EXACT
    // value equality (0.3 == 0.3) — the columns are double precision (not real/float4), which would
    // silently round 0.3 to 0.30000001 on read-back.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnvelopeRoundTrip(DatabaseFixture db)
    {
        [Fact]
        public async Task GenresAndEnergyBoundsComeBackExactlyAsWritten()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            await repo.ReplaceWeekAsync(
                [WithEnvelope(DayOfWeek.Monday, 0, 600, ["jazz", "funk"], 0.3, 0.8)], CancellationToken.None);

            var stored = Assert.Single((await repo.LoadWeekAsync(CancellationToken.None)).Segments);
            Assert.Equal(["jazz", "funk"], stored.Genres);
            Assert.Equal(0.3, stored.EnergyMin);
            Assert.Equal(0.8, stored.EnergyMax);
        }

        [Fact]
        public async Task AWeekMixingNullAndNonNullEnvelopesRoundTripsEachRowIndependently()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            await repo.ReplaceWeekAsync(
                [
                    WithEnvelope(DayOfWeek.Monday, 0, 600, ["jazz", "funk"], 0.3, 0.8),
                    WithEnvelope(DayOfWeek.Tuesday, 0, 600, genres: null, energyMin: null, energyMax: null),
                ],
                CancellationToken.None);

            var segments = (await repo.LoadWeekAsync(CancellationToken.None)).Segments;
            var monday = segments.Single(s => s.Day == DayOfWeek.Monday);
            var tuesday = segments.Single(s => s.Day == DayOfWeek.Tuesday);

            Assert.Equal(["jazz", "funk"], monday.Genres);
            Assert.Equal(0.3, monday.EnergyMin);
            Assert.Equal(0.8, monday.EnergyMax);
            Assert.Null(tuesday.Genres);
            Assert.Null(tuesday.EnergyMin);
            Assert.Null(tuesday.EnergyMax);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the whole-week replace is atomic (SPEC F91.8)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAtomicReplace(DatabaseFixture db)
    {
        [Fact]
        public async Task ReplacingTheWeekLeavesExactlyTheNewRowsTheOldOnesAreGone()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            await repo.ReplaceWeekAsync(
                [MusicOnly(DayOfWeek.Sunday, 0, 1440), MusicOnly(DayOfWeek.Monday, 0, 1440)], CancellationToken.None);

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Saturday, 600, 660)], CancellationToken.None);

            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(result);
            var only = Assert.Single(replaced.Snapshot.Segments);
            Assert.Equal(DayOfWeek.Saturday, only.Day);

            // Straight from Postgres, not just the returned snapshot — proves the old rows are
            // actually gone from the table, not merely absent from this one projection.
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>("select count(*)::int from station.segment_schedule");
            Assert.Equal(1, count);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — change notification (SPEC F91.3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioChangeNotification(DatabaseFixture db)
    {
        [Fact]
        public async Task WeekChangedFiresAfterASuccessfulReplace()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var fired = 0;
            repo.WeekChanged += () => fired++;

            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Thursday, 0, 1440)], CancellationToken.None);

            Assert.Equal(1, fired);
        }

        [Fact]
        public async Task WeekChangedDoesNotFireWhenValidationRejectsTheWrite()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var fired = 0;
            repo.WeekChanged += () => fired++;

            // start_minute 15 is off the 30-minute grid — rejected before any statement runs.
            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Friday, 15, 1440)], CancellationToken.None);

            Assert.Equal(0, fired);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — app-side validation runs BEFORE any statement reaches the database
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRejectingInvalidWeeks(DatabaseFixture db)
    {
        [Fact]
        public async Task AnUndefinedDayValueIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            // (DayOfWeek)9 is off System.Text.Json's back: numeric enums deserialize any in-range
            // integer without validating it against the defined members (T122's own wire contract), so
            // this is a real value ValidateAsync must catch, not merely a C#-side impossibility.
            var result = await repo.ReplaceWeekAsync([MusicOnly((DayOfWeek)9, 0, 600)], CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.InvalidDay);
        }

        [Fact]
        public async Task AnOffGridStartMinuteIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 15, 1440)], CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.InvalidMinuteRange);
        }

        [Fact]
        public async Task AnEndMinuteNotAfterStartMinuteIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 600, 600)], CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.InvalidMinuteRange);
        }

        [Fact]
        public async Task OverlappingSegmentsOnTheSameDayAreRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync(
                [MusicOnly(DayOfWeek.Monday, 0, 600), MusicOnly(DayOfWeek.Monday, 300, 900)], CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.Overlap);
        }

        [Fact]
        public async Task ANestedIntervalOverlappingAnEarlierWiderSegmentIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            // [0,120] and [30,60] already overlap the obvious way (adjacent-row comparison alone would
            // catch that pair). [70,80] is the killing case: it does NOT overlap its immediately
            // preceding row [30,60] (70 >= 60), but it IS still nested inside the wider [0,120] — a
            // previous-row-only overlap check would let it through; only tracking the running MAX end
            // seen so far across the whole day catches it.
            var result = await repo.ReplaceWeekAsync(
                [
                    MusicOnly(DayOfWeek.Monday, 0, 120),
                    MusicOnly(DayOfWeek.Monday, 30, 60),
                    MusicOnly(DayOfWeek.Monday, 70, 80),
                ],
                CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.Overlap && e.StartMinute == 70);
        }

        [Fact]
        public async Task AnUnknownPersonaIdIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync([Staffed(DayOfWeek.Monday, 0, 1440, 999_999)], CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.UnknownPersona);
        }

        [Fact]
        public async Task RejectionLeavesTheStoredWeekUnchanged()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 0, 1440)], CancellationToken.None);

            // Off-grid start minute — rejected before anything is written.
            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Tuesday, 15, 1440)], CancellationToken.None);

            var snapshot = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(DayOfWeek.Monday, Assert.Single(snapshot.Segments).Day);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the schema's own constraints have teeth, independent of the repository
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioExcludeConstraintHasTeeth(DatabaseFixture db)
    {
        [Fact]
        public async Task ADirectInsertOfTwoOverlappingRowsOnTheSameDayIsRejectedByTheDatabaseItself()
        {
            await db.ResetScheduleAsync();
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                "insert into station.segment_schedule (day_of_week, start_minute, end_minute) values (1, 0, 600)");

            // When a second row overlapping the first on the SAME day is inserted directly (bypassing
            // ScheduleRepository's own validation entirely) — the database's EXCLUDE constraint alone
            // must reject it.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "insert into station.segment_schedule (day_of_week, start_minute, end_minute) values (1, 300, 900)"));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaForeignKeyHasTeeth(DatabaseFixture db)
    {
        [Fact]
        public async Task DeletingAScheduledPersonaIsRejectedByTheDatabaseItself()
        {
            await db.ResetStationAsync();
            await db.ResetScheduleAsync();
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Scheduled DJ");
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            await conn.ExecuteAsync(
                """
                insert into station.segment_schedule (day_of_week, start_minute, end_minute, persona_id)
                values (1, 0, 1440, @personaId)
                """,
                new { personaId });

            // When a DELETE targets a persona still holding a schedule slot (bypassing
            // ScheduleRepository entirely) — the ON DELETE RESTRICT foreign key alone must reject it.
            await Assert.ThrowsAsync<PostgresException>(() => conn.ExecuteAsync(
                "delete from station.persona where id = @personaId", new { personaId }));
        }
    }
}
