// STORY-313 — Span-assign & imaging scope (F119.2), the schedule-assignment half (SPEC F119.2,
// PLAN T243)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection, mirrors
// Story240_ScheduleStore.cs's own harness. Owns ScheduleRepository.AssignShowAsync's own semantics
// ONLY — the F119.2 run-span rule (contiguous same-persona, stops at interruptions, narrow-to-one,
// clear), transactionality (a rejected assignment changes nothing), and ReplaceWeekAsync's own new
// show_id round-trip (PLAN T243's landmine #2: a whole-week replace now carries whatever Show a
// caller's ScheduleSegment already names, instead of silently dropping it). The T243 wire contract
// (POST /api/schedule/assign-show through a real HTTP request) is Host.Tests' own
// Story313_ScheduleShowAssignment.cs — out of this file's scope entirely (mirrors Story240's own
// DB-half/API-half split).

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScheduleShowAssignment
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Mirrors Story240_ScheduleStore.cs's own identically-named helper — see its own remarks
    /// in full. AssignShowAsync's own queries (station.show existence, segment_schedule.show_id) need
    /// both idempotent migration scripts (db/33 then db/35) re-run before every fact's own connection,
    /// regardless of xUnit's class scheduling against this collection's sibling files.</summary>
    static ScheduleRepository Repo(DatabaseFixture db)
    {
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "33-show-and-segment-kind-migration.sh"));
        db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "35-show-identity-migration.sh"));
        return new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));
    }

    static ScheduleSegment Staffed(DayOfWeek day, int start, int end, long personaId) =>
        new(null, day, start, end, personaId, Genres: null, EnergyMin: null, EnergyMax: null);

    static ScheduleSegment MusicOnly(DayOfWeek day, int start, int end) =>
        new(null, day, start, end, PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null);

    static async Task<long> InsertShowAsync(DatabaseFixture db, string name, string slug)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into station.show (name, slug) values (@name, @slug) returning id::bigint",
            new { name, slug });
    }

    static long IdOf(ScheduleWeekSnapshot snapshot, DayOfWeek day, int start) =>
        snapshot.Segments.Single(s => s.Day == day && s.StartMinute == start).Id
        ?? throw new InvalidOperationException("Store-assigned segment carried no id.");

    static async Task<long?> ShowIdOfAsync(DatabaseFixture db, long blockId)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long?>(
            "select show_id::bigint from station.segment_schedule where id = @blockId", new { blockId });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the span rule: a run-default assignment lands on every block of the contiguous
    // same-persona run (SPEC F119.2 AC1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheSpanRule(DatabaseFixture db)
    {
        [Fact]
        public async Task AssigningToOneBlockOfASixBlockRunAppliesTheShowToAllSix()
        {
            // Given a persona painted across six contiguous 60-minute blocks (the STORY-313 AC1 shape)
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Six Block DJ");
            var showId = await InsertShowAsync(db, "Night Moves", "night-moves");
            var week = Enumerable.Range(0, 6).Select(i => Staffed(DayOfWeek.Monday, i * 60, (i + 1) * 60, personaId)).ToList();
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Monday, 180); // the 4th block, mid-run

            // When a show is assigned from that block's own side panel, applying to the run
            var result = await repo.AssignShowAsync(clickedBlockId, showId, applyToRun: true, CancellationToken.None);

            // Then all six blocks take the show
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            Assert.Equal(6, assigned.UpdatedBlockIds.Count);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.All(loaded.Segments, s => Assert.Equal(showId, s.Show?.Id));
        }

        [Fact]
        public async Task ApplyToRunFalseNarrowsTheAssignmentToJustTheClickedBlock()
        {
            // Given the same six-block run
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Narrow DJ");
            var showId = await InsertShowAsync(db, "Morning Drive", "morning-drive");
            var week = Enumerable.Range(0, 6).Select(i => Staffed(DayOfWeek.Tuesday, i * 60, (i + 1) * 60, personaId)).ToList();
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Tuesday, 180);

            // When the narrow-to-one checkbox is honored
            var result = await repo.AssignShowAsync(clickedBlockId, showId, applyToRun: false, CancellationToken.None);

            // Then only the clicked block carries the show — its five run-mates stay unnamed
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            var onlyId = Assert.Single(assigned.UpdatedBlockIds);
            Assert.Equal(clickedBlockId, onlyId);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            var named = loaded.Segments.Where(s => s.Show is not null).ToList();
            var namedOnly = Assert.Single(named);
            Assert.Equal(clickedBlockId, namedOnly.Id);
        }

        [Fact]
        public async Task AContiguousRunOfMusicOnlyBlocksIsALegalRunOfItsOwn()
        {
            // Given three contiguous MUSIC-ONLY (null-persona) blocks — SPEC F115.2/F119.2: a
            // music-only block can legally carry a show, so a run of them is exactly as legal a run as
            // one sharing a persona id.
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var showId = await InsertShowAsync(db, "Overnight Static", "overnight-static");
            var week = new List<ScheduleSegment>
            {
                MusicOnly(DayOfWeek.Wednesday, 0, 60),
                MusicOnly(DayOfWeek.Wednesday, 60, 120),
                MusicOnly(DayOfWeek.Wednesday, 120, 180),
            };
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Wednesday, 60);

            // When a show is assigned to the middle block, applying to the run
            var result = await repo.AssignShowAsync(clickedBlockId, showId, applyToRun: true, CancellationToken.None);

            // Then all three music-only blocks take the show
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            Assert.Equal(3, assigned.UpdatedBlockIds.Count);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a run ends honestly: music-only/other-persona/a time gap all stop it (SPEC F119.2 AC3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRunsEndHonestly(DatabaseFixture db)
    {
        [Fact]
        public async Task ARunStopsAtAMusicOnlyInterruption()
        {
            // Given persona P, persona P, MUSIC-ONLY, persona P — the middle music-only block splits
            // what would otherwise be one four-block run into two separate ones.
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Interrupted DJ");
            var showId = await InsertShowAsync(db, "Afternoon Session", "afternoon-session");
            var week = new List<ScheduleSegment>
            {
                Staffed(DayOfWeek.Thursday, 0, 60, personaId),
                Staffed(DayOfWeek.Thursday, 60, 120, personaId),
                MusicOnly(DayOfWeek.Thursday, 120, 180),
                Staffed(DayOfWeek.Thursday, 180, 240, personaId),
            };
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Thursday, 0);

            // When the show is assigned from the first block of the leading pair, applying to the run
            var result = await repo.AssignShowAsync(clickedBlockId, showId, applyToRun: true, CancellationToken.None);

            // Then only the two blocks BEFORE the music-only interruption take the show — the
            // music-only block and the trailing persona-P block (on the OTHER side of the
            // interruption) are untouched.
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            Assert.Equal(2, assigned.UpdatedBlockIds.Count);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.All(loaded.Segments.Where(s => s.StartMinute is 0 or 60), s => Assert.Equal(showId, s.Show?.Id));
            Assert.All(loaded.Segments.Where(s => s.StartMinute is 120 or 180), s => Assert.Null(s.Show));
        }

        [Fact]
        public async Task ARunStopsAtAnOtherPersonaInterruption()
        {
            // Given persona A, persona A, persona B, persona A — the middle persona-B block belongs to
            // a different run entirely, even though the block on the far side shares persona A again.
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaA = await ScheduleTestPersonas.InsertAsync(db, "Persona A");
            var personaB = await ScheduleTestPersonas.InsertAsync(db, "Persona B");
            var showId = await InsertShowAsync(db, "Handoff Hour", "handoff-hour");
            var week = new List<ScheduleSegment>
            {
                Staffed(DayOfWeek.Friday, 0, 60, personaA),
                Staffed(DayOfWeek.Friday, 60, 120, personaA),
                Staffed(DayOfWeek.Friday, 120, 180, personaB),
                Staffed(DayOfWeek.Friday, 180, 240, personaA),
            };
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Friday, 60);

            // When the show is assigned from the second persona-A block, applying to the run
            var result = await repo.AssignShowAsync(clickedBlockId, showId, applyToRun: true, CancellationToken.None);

            // Then only the leading persona-A pair takes the show
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            Assert.Equal(2, assigned.UpdatedBlockIds.Count);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.All(loaded.Segments.Where(s => s.StartMinute is 0 or 60), s => Assert.Equal(showId, s.Show?.Id));
            Assert.All(loaded.Segments.Where(s => s.StartMinute is 120 or 180), s => Assert.Null(s.Show));
        }

        [Fact]
        public async Task AGapInTheGridBreaksTheRunEvenWithMatchingPersonaOnBothSides()
        {
            // Given persona P 0-60, an UNSCHEDULED gap 60-120, then persona P again 120-180 — same
            // persona on both sides of the gap, but "no gap between" is its own, independent run-ending
            // condition (SPEC F119.2's own wording), not implied by persona-matching alone.
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Gapped DJ");
            var showId = await InsertShowAsync(db, "Gap Year", "gap-year");
            var week = new List<ScheduleSegment>
            {
                Staffed(DayOfWeek.Saturday, 0, 60, personaId),
                Staffed(DayOfWeek.Saturday, 120, 180, personaId),
            };
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Saturday, 0);

            // When the show is assigned from the block before the gap, applying to the run
            var result = await repo.AssignShowAsync(clickedBlockId, showId, applyToRun: true, CancellationToken.None);

            // Then only that one block takes the show — the gap ends the run before the second block
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            var onlyId = Assert.Single(assigned.UpdatedBlockIds);
            Assert.Equal(clickedBlockId, onlyId);
        }

        [Fact]
        public async Task ARunNeverCrossesADayBoundaryEvenForATimeAdjacentOvernightShow()
        {
            // Given the same show painted as an overnight block — Monday 23:00-24:00 and Tuesday
            // 00:00-01:00 — which is genuinely time-adjacent (1440 meets 0) and same-persona, but
            // segment_schedule rows are per-day (SPEC F91.1): a run is scoped to the clicked block's
            // OWN day query, so the Tuesday row is never even fetched.
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Overnight DJ");
            var showId = await InsertShowAsync(db, "Witching Hour", "witching-hour");
            var week = new List<ScheduleSegment>
            {
                Staffed(DayOfWeek.Monday, 1380, 1440, personaId),
                Staffed(DayOfWeek.Tuesday, 0, 60, personaId),
            };
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Monday, 1380);

            // When the show is assigned from the Monday block, applying to the run
            var result = await repo.AssignShowAsync(clickedBlockId, showId, applyToRun: true, CancellationToken.None);

            // Then only the Monday block takes the show — the Tuesday row is untouched
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            var onlyId = Assert.Single(assigned.UpdatedBlockIds);
            Assert.Equal(clickedBlockId, onlyId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — clearing a show (null showId)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioClearingAShow(DatabaseFixture db)
    {
        [Fact]
        public async Task AssigningNullShowIdClearsAPreviouslyAssignedShow()
        {
            // Given a block already carrying a show
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Clear DJ");
            var showId = await InsertShowAsync(db, "Soon Cleared", "soon-cleared");
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(await repo.ReplaceWeekAsync(
                [Staffed(DayOfWeek.Sunday, 0, 60, personaId)], expectedVersion: null, CancellationToken.None));
            var blockId = IdOf(replaced.Snapshot, DayOfWeek.Sunday, 0);
            await repo.AssignShowAsync(blockId, showId, applyToRun: false, CancellationToken.None);
            Assert.Equal(showId, await ShowIdOfAsync(db, blockId));

            // When the same block is assigned a null show
            var result = await repo.AssignShowAsync(blockId, null, applyToRun: false, CancellationToken.None);

            // Then the block's show is cleared
            Assert.IsType<ShowAssignResult.Assigned>(result);
            Assert.Null(await ShowIdOfAsync(db, blockId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — AssignShowResponseDto's own week fingerprint (PLAN T243 review F2, gh-#255): a
    // client that re-renders off the assign response's own Version treats it as its next PUT's
    // BaseVersion exactly like a fresh GET's Version — the two must actually agree.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioResponseVersionMatchesTheStore(DatabaseFixture db)
    {
        [Fact]
        public async Task TheAssignedVersionEqualsASubsequentLoadsVersion()
        {
            // Given a block with no show yet
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Version Parity DJ");
            var showId = await InsertShowAsync(db, "Fresh Signal", "fresh-signal");
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(await repo.ReplaceWeekAsync(
                [Staffed(DayOfWeek.Monday, 0, 60, personaId)], expectedVersion: null, CancellationToken.None));
            var blockId = IdOf(replaced.Snapshot, DayOfWeek.Monday, 0);

            // When a show is assigned
            var result = await repo.AssignShowAsync(blockId, showId, applyToRun: false, CancellationToken.None);

            // Then the assign response's own Version equals a fresh load's own fingerprint — an editor
            // that treats this response as its latest known state compares cleanly against the store,
            // the same way a GET's own Version would.
            var assigned = Assert.IsType<ShowAssignResult.Assigned>(result);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(ScheduleWeekVersion.Compute(loaded.Segments), assigned.Version);
        }
    }

    // ---------------------------------------------------------------------
    // WeekChanged (PLAN T243 review F4, mirrors Story240_ScheduleStore.cs's own
    // ScenarioChangeNotification): fires exactly once on a successful assignment, never on a rejection.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioChangeNotification(DatabaseFixture db)
    {
        [Fact]
        public async Task WeekChangedFiresOnceAfterASuccessfulAssignment()
        {
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Notify DJ");
            var showId = await InsertShowAsync(db, "On The Air", "on-the-air");
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(await repo.ReplaceWeekAsync(
                [Staffed(DayOfWeek.Tuesday, 0, 60, personaId)], expectedVersion: null, CancellationToken.None));
            var blockId = IdOf(replaced.Snapshot, DayOfWeek.Tuesday, 0);
            var fired = 0;
            repo.WeekChanged += () => fired++;

            await repo.AssignShowAsync(blockId, showId, applyToRun: false, CancellationToken.None);

            Assert.Equal(1, fired);
        }

        [Fact]
        public async Task WeekChangedDoesNotFireWhenTheBlockIsUnknown()
        {
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var showId = await InsertShowAsync(db, "Dead Air", "dead-air");
            var fired = 0;
            repo.WeekChanged += () => fired++;

            await repo.AssignShowAsync(blockId: 999_999, showId, applyToRun: true, CancellationToken.None);

            Assert.Equal(0, fired);
        }

        [Fact]
        public async Task WeekChangedDoesNotFireWhenTheShowIsUnknown()
        {
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Unnamed DJ");
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(await repo.ReplaceWeekAsync(
                [Staffed(DayOfWeek.Wednesday, 0, 60, personaId)], expectedVersion: null, CancellationToken.None));
            var blockId = IdOf(replaced.Snapshot, DayOfWeek.Wednesday, 0);
            var fired = 0;
            repo.WeekChanged += () => fired++;

            await repo.AssignShowAsync(blockId, showId: 999_999, applyToRun: true, CancellationToken.None);

            Assert.Equal(0, fired);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — transactionality: a rejected assignment changes NOTHING
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTransactionality(DatabaseFixture db)
    {
        [Fact]
        public async Task AssigningAnUnknownShowIdChangesNothingAcrossTheWholeIntendedRun()
        {
            // Given a three-block run with no show assigned yet
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Untouched DJ");
            var week = Enumerable.Range(0, 3).Select(i => Staffed(DayOfWeek.Monday, i * 60, (i + 1) * 60, personaId)).ToList();
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(
                await repo.ReplaceWeekAsync(week, expectedVersion: null, CancellationToken.None));
            var clickedBlockId = IdOf(replaced.Snapshot, DayOfWeek.Monday, 60);

            // When a show id that names no station.show row is assigned, applying to the run
            var result = await repo.AssignShowAsync(clickedBlockId, showId: 999_999, applyToRun: true, CancellationToken.None);

            // Then the store reports ShowNotFound, and every block in the WOULD-BE run — not just the
            // clicked one — remains unassigned; nothing partially landed before the rejection. Asserts
            // ShowId (the write-authoritative field — ScheduleSegment's own remarks), never Show
            // (the load-time display projection): a rejected write only ever touches show_id, so that
            // is the field a transactionality fact must pin.
            Assert.IsType<ShowAssignResult.ShowNotFound>(result);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.All(loaded.Segments, s => Assert.Null(s.ShowId));
        }

        [Fact]
        public async Task AssigningToAnUnknownBlockIdReturnsBlockNotFoundAndChangesNothing()
        {
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var showId = await InsertShowAsync(db, "Nobody Home", "nobody-home");

            var result = await repo.AssignShowAsync(blockId: 999_999, showId, applyToRun: true, CancellationToken.None);

            Assert.IsType<ShowAssignResult.BlockNotFound>(result);
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Empty(loaded.Segments);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ReplaceWeekAsync round-trips show_id now (PLAN T243's landmine #2): a caller's own
    // ShowId-bearing ScheduleSegment survives a whole-week replace instead of being silently dropped.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReplaceWeekAsyncRoundTripsShowId(DatabaseFixture db)
    {
        [Fact]
        public async Task AWeekReplacedWithAShowBearingSegmentPersistsAndReloadsWithTheSameShow()
        {
            // Given a real show row
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Repaint DJ");
            var showId = await InsertShowAsync(db, "Grid Repaint Show", "grid-repaint-show");

            // When ReplaceWeekAsync is given a ScheduleSegment whose OWN ShowId already names that row
            // — the write-authoritative field (ScheduleSegment's own remarks), exactly the shape the
            // PUT wire's ToSegment or a future grid-repaint caller (T245's own client state) would
            // submit — never a fabricated ShowSummary just to carry an id through —
            var segment = new ScheduleSegment(
                null, DayOfWeek.Wednesday, 0, 60, personaId, Genres: null, EnergyMin: null, EnergyMax: null,
                Show: null, ShowId: showId);
            var result = await repo.ReplaceWeekAsync([segment], expectedVersion: null, CancellationToken.None);

            // Then the write's own response AND a fresh reload both carry the show — it was never
            // silently dropped the way this insert used to (see ScheduleRepository's own remarks).
            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(result);
            Assert.Equal(showId, Assert.Single(replaced.Snapshot.Segments).Show?.Id);
            var reloaded = await repo.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(showId, Assert.Single(reloaded.Segments).Show?.Id);
        }

        [Fact]
        public async Task AWeekReplacedWithNoShowOnASegmentLeavesItUnnamed()
        {
            // The plain-default case every other fact in Story240_ScheduleStore.cs already exercises,
            // pinned here too so this file's own show_id column addition to the INSERT is proven not to
            // have changed the ordinary (Show: null) path at all.
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);

            var result = await repo.ReplaceWeekAsync(
                [MusicOnly(DayOfWeek.Thursday, 0, 1440)], expectedVersion: null, CancellationToken.None);

            var replaced = Assert.IsType<ScheduleReplaceResult.Replaced>(result);
            Assert.Null(Assert.Single(replaced.Snapshot.Segments).Show);
        }
    }

    // ---------------------------------------------------------------------
    // N1 — the Show/ShowId duality (ScheduleSegment's own remarks): ScheduleRepository's load path
    // sets BOTH fields from the same joined row, so they can never disagree about which block carries
    // which show. Pinned against a real load (a mix of a named and an unnamed block, so the fact can't
    // pass by both sides coincidentally being null) rather than merely asserted in a doc comment.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShowIdAndShowAgree(DatabaseFixture db)
    {
        [Fact]
        public async Task EveryLoadedSegmentsShowIdEqualsItsShowsOwnId()
        {
            // Given a named block and an unnamed block, loaded together
            await db.ResetShowAsync();
            await db.ResetScheduleAsync();
            var repo = Repo(db);
            var personaId = await ScheduleTestPersonas.InsertAsync(db, "Duality DJ");
            var showId = await InsertShowAsync(db, "Duality Hour", "duality-hour");
            var segments = new List<ScheduleSegment>
            {
                new(null, DayOfWeek.Friday, 0, 60, personaId, Genres: null, EnergyMin: null, EnergyMax: null,
                    Show: null, ShowId: showId),
                MusicOnly(DayOfWeek.Friday, 60, 120),
            };
            await repo.ReplaceWeekAsync(segments, expectedVersion: null, CancellationToken.None);

            // When the week is loaded back
            var loaded = await repo.LoadWeekAsync(CancellationToken.None);

            // Then every segment's ShowId equals its own Show's Id — the named block (ShowId ==
            // Show.Id, neither null) and the unnamed block (both null) alike; a writer and the
            // load-time projection never disagree about which field means "the show this block
            // carries" (ScheduleSegment's own remarks).
            Assert.Equal(2, loaded.Segments.Count);
            Assert.All(loaded.Segments, s => Assert.Equal(s.ShowId, s.Show?.Id));
            Assert.Contains(loaded.Segments, s => s.ShowId == showId);
            Assert.Contains(loaded.Segments, s => s.ShowId == null);
        }
    }
}
