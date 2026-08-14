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
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScheduleStore
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// PLAN T241 review: <see cref="ScheduleRepository"/>'s own load query now LEFT JOINs
    /// <c>station.show</c> keyed on <c>segment_schedule.show_id</c> (SPEC F116.1), so every fact in
    /// this file gained an implicit dependency on BOTH columns existing that it never had before. This
    /// class carries no ordering guarantee against two sibling files' own in-place scenarios in the
    /// same DatabaseCollection: Story242_UpgradeChangesNothing.cs's several scenarios drop
    /// <c>station.segment_schedule</c> and rebuild it via db/27 ALONE (predates <c>show_id</c>
    /// entirely — see that file's own header, which already documents this exact hazard for
    /// Story304_AiredKindStamp.cs and names db/33 as the guard); Story305_ShowRepository.cs's own
    /// in-place scenario drops <c>station.show</c>'s db/35 columns (no <c>tagline</c>/<c>flavor</c>).
    /// Running BOTH idempotent migration scripts here, in order — db/33 first (restores
    /// <c>segment_schedule.show_id</c> and a bare <c>station.show</c> if either is missing), db/35
    /// second (widens <c>station.show</c> to its full identity shape) — right before the repository's
    /// own connection is even built, makes every fact in this file self-sufficient regardless of
    /// xUnit's class scheduling, mirroring Story304's own "(re)running db/33 in its own Arrange before
    /// every assertion" guard and Story305_ShowRepository.cs's own db/35 guard, combined.
    /// </summary>
    static ScheduleRepository Repo(DatabaseFixture db)
    {
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "33-show-and-segment-kind-migration.sh"));
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "35-show-identity-migration.sh"));
        return new ScheduleRepository(
            new Lazy<NpgsqlDataSource>(() => db.StationDataSource), NullLogger<ScheduleRepository>.Instance);
    }

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

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 0, 1440)], expectedVersion: null, CancellationToken.None);

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

            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Tuesday, 0, 1440)], expectedVersion: null, CancellationToken.None);

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
                [WithEnvelope(DayOfWeek.Monday, 0, 600, ["jazz", "funk"], 0.3, 0.8)], expectedVersion: null, CancellationToken.None);

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
                expectedVersion: null, CancellationToken.None);

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
    // HAPPY PATH — show identity rides the load, dormant bundle columns unread (SPEC F115.2, F116.1,
    // STORY-306, PLAN T241)
    //
    // ARCHITECTURE.md's own guidance for this pin: "put the live pin where it can be real ... extend
    // the schedule repository's spec: populate dormant columns via SQL, reload the week, assert the
    // loaded model is identical." ScheduleRepository has no writer for show_id/station.show at all
    // (T243/T239 are the write-side seams), so both are populated by direct SQL here, mirroring this
    // file's own ScenarioPersonaForeignKeyHasTeeth idiom.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShowIdentityRidesTheLoad(DatabaseFixture db)
    {
        [Fact]
        public async Task HandPopulatingShowPersonaIdAndEnvelopeChangesNothingAboutTheLoadedWeek()
        {
            // Given a show (tagline + flavor set) referenced by one schedule block naming its OWN
            // block-level persona, and a SECOND real persona standing by to hand-populate the show's
            // own DORMANT persona_id column with.
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var blockPersonaId = await ScheduleTestPersonas.InsertAsync(db, "Block DJ");
            var dormantShowPersonaId = await ScheduleTestPersonas.InsertAsync(db, "Dormant Show DJ");

            long showId;
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                showId = await conn.ExecuteScalarAsync<long>(
                    """
                    insert into station.show (name, slug, tagline, flavor)
                    values ('Night Moves', 'night-moves', 'Late-night deep cuts', 'moody, sparse')
                    returning id
                    """);
                await conn.ExecuteAsync(
                    """
                    insert into station.segment_schedule (day_of_week, start_minute, end_minute, persona_id, show_id)
                    values (1, 540, 720, @blockPersonaId, @showId)
                    """,
                    new { blockPersonaId, showId });
            }
            var repo = Repo(db);

            // When the week is loaded BEFORE the dormant columns are ever touched...
            var before = await repo.LoadWeekAsync(CancellationToken.None);

            // ...then station.show's own DORMANT persona_id/envelope columns are hand-populated
            // directly (SPEC F115.2's pin — ShowRepository has no parameter for either; a raw UPDATE
            // is the only way to even attempt setting them)...
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    """
                    update station.show
                    set persona_id = @dormantShowPersonaId,
                        envelope = '{"genres": ["Jazz"], "energyMin": 0.1, "energyMax": 0.9}'::jsonb
                    where id = @showId
                    """,
                    new { dormantShowPersonaId, showId });

            // ...and the week is loaded again.
            var after = await repo.LoadWeekAsync(CancellationToken.None);

            // Then the loaded model is IDENTICAL — hand-populating the dormant bundle columns changed
            // NO v1 behavior (sequence-compared, not whole-snapshot: ScheduleWeekSnapshot's own
            // compiler-generated Equals compares its Segments list by reference, the same
            // Genres-by-reference gotcha Story241_StationFollowsTheClock.cs's own facts document).
            Assert.Equal(before.Segments, after.Segments);

            // And the loaded show identity itself carries only the four public fields (SPEC F115.2's
            // pin enforced by ShowSummary's own shape — there is no PersonaId/Envelope member to have
            // picked either dormant value up even if the query tried).
            var block = Assert.Single(after.Segments);
            Assert.Equal(blockPersonaId, block.PersonaId);
            Assert.NotNull(block.Show);
            Assert.Equal(new ShowSummary(showId, "Night Moves", "Late-night deep cuts", "moody, sparse"), block.Show);
        }

        [Fact]
        public async Task UnnamedBlockLoadsWithNoShow()
        {
            // Given a schedule block with no show_id at all
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 0, 1440)], expectedVersion: null, CancellationToken.None);

            // When the week is loaded
            var snapshot = await repo.LoadWeekAsync(CancellationToken.None);

            // Then the block's own Show is null — the LEFT JOIN finds no matching station.show row
            Assert.Null(Assert.Single(snapshot.Segments).Show);
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
                [MusicOnly(DayOfWeek.Sunday, 0, 1440), MusicOnly(DayOfWeek.Monday, 0, 1440)], expectedVersion: null, CancellationToken.None);

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Saturday, 600, 660)], expectedVersion: null, CancellationToken.None);

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

            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Thursday, 0, 1440)], expectedVersion: null, CancellationToken.None);

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
            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Friday, 15, 1440)], expectedVersion: null, CancellationToken.None);

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
            var result = await repo.ReplaceWeekAsync([MusicOnly((DayOfWeek)9, 0, 600)], expectedVersion: null, CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.InvalidDay);
        }

        [Fact]
        public async Task AnOffGridStartMinuteIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 15, 1440)], expectedVersion: null, CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.InvalidMinuteRange);
        }

        [Fact]
        public async Task AnEndMinuteNotAfterStartMinuteIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 600, 600)], expectedVersion: null, CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.InvalidMinuteRange);
        }

        [Fact]
        public async Task OverlappingSegmentsOnTheSameDayAreRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync(
                [MusicOnly(DayOfWeek.Monday, 0, 600), MusicOnly(DayOfWeek.Monday, 300, 900)], expectedVersion: null, CancellationToken.None);

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
                expectedVersion: null, CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.Overlap && e.StartMinute == 70);
        }

        [Fact]
        public async Task AnUnknownPersonaIdIsRejected()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync([Staffed(DayOfWeek.Monday, 0, 1440, 999_999)], expectedVersion: null, CancellationToken.None);

            var failed = Assert.IsType<ScheduleReplaceResult.ValidationFailed>(result);
            Assert.Contains(failed.Errors, e => e.Kind == ScheduleCellErrorKind.UnknownPersona);
        }

        [Fact]
        public async Task RejectionLeavesTheStoredWeekUnchanged()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Monday, 0, 1440)], expectedVersion: null, CancellationToken.None);

            // Off-grid start minute — rejected before anything is written.
            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Tuesday, 15, 1440)], expectedVersion: null, CancellationToken.None);

            var snapshot = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(DayOfWeek.Monday, Assert.Single(snapshot.Segments).Day);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — gh-#406 slice 1: ReplaceWeekAsync's own 23503 race-backstop catch (moved down from
    // ScheduleController, which used to catch Npgsql.PostgresException directly — an L2
    // Postgres-confinement violation) maps a foreign-key violation raised by the INSERT itself to
    // ScheduleReplaceResult.PersonaVanished.
    //
    // The GENUINE race this catch exists for (SPEC F91.1/PersonaVanished's own remarks: a persona a
    // validated row names is deleted, on a SEPARATE connection, between ValidateAsync's existence
    // check and this transaction's own INSERT) is not independently reproducible here without a
    // test-only hook into the repository — mirrors Story118_PersonaStorage.cs's own
    // ScenarioDeleteFkGuard header note that PersonaRepository.DeleteAsync's identically-shaped
    // query-then-delete race backstop carries no such coverage either, for the same reason; that
    // file's own ScenarioDeleteFkGuard facts (and this file's own ScenarioPersonaForeignKeyHasTeeth,
    // directly below) already prove the schema's FK constraints fire given a direct conflicting row.
    //
    // What IS deterministically reproducible on a single connection: ValidateAsync (this file's own
    // ScenarioRejectingInvalidWeeks, directly above) checks a submitted row's PersonaId against
    // station.persona before ever reaching the database, but has no equivalent check for ShowId
    // (PLAN T243's own remarks: ReplaceWeekAsync writes ShowId straight through, unvalidated) — so a
    // week naming an unknown ShowId reaches the INSERT unrejected and trips
    // segment_schedule.show_id's own FK (db/06, db/33; ON DELETE RESTRICT) there instead. This is the
    // exact same 23503 foreign_key_violation SQLSTATE the persona race raises — the catch itself
    // matches on SqlState alone, never which column's FK fired — so it exercises the identical
    // catch/mapping code path deterministically, without needing genuine cross-connection concurrency.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioForeignKeyViolationMapsToPersonaVanished(DatabaseFixture db)
    {
        [Fact]
        public async Task AnUnknownShowIdTripsTheInsertsOwnForeignKeyAndMapsToPersonaVanished()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var week = new[] { MusicOnly(DayOfWeek.Monday, 0, 600) with { ShowId = 999_999 } };
            var result = await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None);

            Assert.IsType<ScheduleReplaceResult.PersonaVanished>(result);
        }

        [Fact]
        public async Task TheRejectionLeavesTheStoredWeekUnchanged()
        {
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            await repo.ReplaceWeekAsync([MusicOnly(DayOfWeek.Tuesday, 0, 600)], expectedVersion: null, CancellationToken.None);

            var week = new[] { MusicOnly(DayOfWeek.Monday, 0, 600) with { ShowId = 999_999 } };
            await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None);

            var snapshot = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(DayOfWeek.Tuesday, Assert.Single(snapshot.Segments).Day);
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
