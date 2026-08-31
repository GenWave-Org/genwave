// STORY-378 — Tracks my schedule can never reach (SPEC F153.8 · PLAN T376)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture, the Story376/Story377 shape one seam
// over: most facts seed library.media on base columns only (genre/energy) and drive
// RotFindingRepository.ReconcileUnreachableAsync directly against a hand-built EnvelopeTuple list;
// the station-default-fallback, per-field-fallback, and dedup facts drive the real
// UnreachableGardenerPass over a FakeScheduleStore/FakeStationDefaultEnvelopeSource (the
// Story377 ScenarioJustOverTheShelfDustDaysBoundaryThroughThePass idiom) — the pass->repository arc,
// proven more than once here since this pass's own tuple-building logic (per-field fallback, dedup,
// normalization) is the one thing no repository-direct fact can exercise.

using System.Text.Json;
using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Garden;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureTracksMyScheduleCanNeverReach
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static RotFindingRepository Repo(DatabaseFixture db) => new(db.DataSource);

    static async Task<long> InsertPlayableRowAsync(
        DatabaseFixture db, string path, string? genre = null, double? energy = null, bool eligible = true)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, measurable, eligible, genre, energy)
            values (@path, 'flac', 1024, now(), 'ready', true, @eligible, @genre, @energy)
            returning id
            """,
            new { path, eligible, genre, energy });
    }

    static async Task SetNeverPlayAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rating (media_id, never_play) values (@mediaId, true)",
            new { mediaId });
    }

    /// <summary>One <c>unreachable</c> finding for <paramref name="mediaId"/>, or
    /// <see langword="null"/> when none exists yet — the Story377 <c>ReadFindingAsync</c> idiom,
    /// narrowed to this file's own single kind.</summary>
    static async Task<(long Id, string State, string Evidence, DateTimeOffset OpenedAt, DateTimeOffset? ResolvedAt)?> ReadFindingAsync(
        DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<(long, string, string, DateTimeOffset, DateTimeOffset?)?>(
            """
            select id, state::text, evidence::text, opened_at, resolved_at
            from library.rot_finding
            where media_id = @mediaId and kind = 'unreachable'::library.rot_kind
            """,
            new { mediaId });
    }

    static async Task<int> CountOpenAsync(DatabaseFixture db, IEnumerable<long> mediaIds)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            """
            select count(*)::int from library.rot_finding
            where kind = 'unreachable'::library.rot_kind and state = 'open' and media_id = any(@ids)
            """,
            new { ids = mediaIds.ToArray() });
    }

    static string ReasonOf(string evidenceJson)
    {
        using var evidence = JsonDocument.Parse(evidenceJson);
        return evidence.RootElement.GetProperty("reason").GetString() ?? string.Empty;
    }

    static int EnvelopeCountOf(string evidenceJson)
    {
        using var evidence = JsonDocument.Parse(evidenceJson);
        return evidence.RootElement.GetProperty("envelopes").GetInt32();
    }

    // ---------------------------------------------------------------------
    // AC1 — genre unreachable
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGenreUnreachable(DatabaseFixture db)
    {
        // Given tuples {rock} and {jazz}, a playable classical row, a playable rock row, and a
        // playable row with NO genre at all (T376 review BLOCK-3(a): the three-valued-logic fix's
        // own live-Postgres pin — a NULL genre must still read "genre" under a genre-constrained
        // tuple set, never fall through as NULL/not-flagged), When the unreachable reconcile runs.
        // All three facts share this one arrangement.
        async Task<(long ClassicalId, long RockId, long NullGenreId)> ArrangeAsync()
        {
            await db.ResetAsync();
            var classicalId = await InsertPlayableRowAsync(db, "/gardener/t376-genre-classical.flac", genre: "classical", energy: 0.5);
            var rockId = await InsertPlayableRowAsync(db, "/gardener/t376-genre-rock.flac", genre: "rock", energy: 0.5);
            var nullGenreId = await InsertPlayableRowAsync(db, "/gardener/t376-genre-null.flac", genre: null, energy: 0.5);

            await Repo(db).ReconcileUnreachableAsync(
                [new EnvelopeTuple(["rock"], 0, 1), new EnvelopeTuple(["jazz"], 0, 1)], CancellationToken.None);

            return (classicalId, rockId, nullGenreId);
        }

        [Fact]
        public async Task AnOpenUnreachableFindingHasEvidenceReasonGenre()
        {
            var (classicalId, _, _) = await ArrangeAsync();

            var finding = await ReadFindingAsync(db, classicalId);
            Assert.Equal("genre", ReasonOf(finding!.Value.Evidence));
        }

        [Fact]
        public async Task TheRockRowHasNoFinding()
        {
            var (_, rockId, _) = await ArrangeAsync();

            Assert.False((await ReadFindingAsync(db, rockId)).HasValue);
        }

        [Fact]
        public async Task TheNullGenreRowAlsoHasEvidenceReasonGenre()
        {
            var (_, _, nullGenreId) = await ArrangeAsync();

            var finding = await ReadFindingAsync(db, nullGenreId);
            Assert.Equal("genre", ReasonOf(finding!.Value.Evidence));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioClearingTheGenreDoesNotWronglyResolveTheFinding(DatabaseFixture db)
    {
        // T376 review BLOCK-3(b): the resolve half's own regression pin. Given an open unreachable
        // finding for a jazz row under a genre-constrained tuple {rock}, When the row's genre is
        // cleared to NULL (via raw SQL — no pass involved in the mutation) and a SECOND reconcile
        // runs against the SAME tuple, Then the finding stays open — a NULL genre is still NOT
        // admitted by a genre-constrained tuple (SegmentEnvelope.Genres's own documented contract:
        // "an untagged track does not satisfy a non-empty list"), so `not adm.admitted` must stay
        // true, never flip to NULL-so-not-true the pre-fix bug produced.
        [Fact]
        public async Task TheFindingStaysOpen()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-clear-genre.flac", genre: "jazz", energy: 0.5);
            var repo = Repo(db);
            var tuples = new[] { new EnvelopeTuple(["rock"], 0, 1) };
            await repo.ReconcileUnreachableAsync(tuples, CancellationToken.None);

            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update library.media set genre = null where id = @jazzId", new { jazzId });
            await repo.ReconcileUnreachableAsync(tuples, CancellationToken.None);

            Assert.Equal("open", (await ReadFindingAsync(db, jazzId))!.Value.State);
        }
    }

    // ---------------------------------------------------------------------
    // AC2 — energy unreachable
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyUnreachable(DatabaseFixture db)
    {
        // Given tuples [0.2,0.5] and [0.4,0.7] (no genres), a row at energy 0.9, a row at 0.45, and
        // a row with energy NULL, When the pass runs. Three facts share this one arrangement.
        async Task<(long HighId, long MidId, long NullId)> ArrangeAsync()
        {
            await db.ResetAsync();
            var highId = await InsertPlayableRowAsync(db, "/gardener/t376-energy-high.flac", energy: 0.9);
            var midId = await InsertPlayableRowAsync(db, "/gardener/t376-energy-mid.flac", energy: 0.45);
            var nullId = await InsertPlayableRowAsync(db, "/gardener/t376-energy-null.flac", energy: null);

            await Repo(db).ReconcileUnreachableAsync(
                [new EnvelopeTuple([], 0.2, 0.5), new EnvelopeTuple([], 0.4, 0.7)], CancellationToken.None);

            return (highId, midId, nullId);
        }

        [Fact]
        public async Task TheHighEnergyRowsFindingReasonIsEnergy()
        {
            var (highId, _, _) = await ArrangeAsync();

            var finding = await ReadFindingAsync(db, highId);
            Assert.Equal("energy", ReasonOf(finding!.Value.Evidence));
        }

        [Fact]
        public async Task TheMidEnergyRowHasNoFinding()
        {
            var (_, midId, _) = await ArrangeAsync();

            Assert.False((await ReadFindingAsync(db, midId)).HasValue);
        }

        [Fact]
        public async Task TheNullEnergyRowHasNoFinding()
        {
            var (_, _, nullId) = await ArrangeAsync();

            Assert.False((await ReadFindingAsync(db, nullId)).HasValue);
        }
    }

    // ---------------------------------------------------------------------
    // AC3 — an empty genre list admits every genre
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnEmptyGenreListAdmitsAll(DatabaseFixture db)
    {
        // Given one tuple with no genres and range [0,1], and three rows of different genre/energy
        // — including a NULL-genre row (T376 review BLOCK-3(c): this is the SHORT-CIRCUIT case, not
        // the coalesce case BLOCK-3(a)/(b) pin — `cardinality(e.genres) = 0` is true on its own, so
        // Postgres's `or` never even needs the coalesced NULL-genre comparison to resolve `genre_ok`
        // to true here) — When the pass runs. One count assertion over the homogeneous set (the
        // Story377 ScenarioTheTrackNnFamily idiom), not three independently-named claims.
        [Fact]
        public async Task NoFindingForAnyRow()
        {
            await db.ResetAsync();
            var rockId = await InsertPlayableRowAsync(db, "/gardener/t376-admit-all-rock.flac", genre: "rock", energy: 0.1);
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-admit-all-jazz.flac", genre: "jazz", energy: 0.9);
            var untaggedId = await InsertPlayableRowAsync(db, "/gardener/t376-admit-all-untagged.flac", genre: null, energy: null);

            await Repo(db).ReconcileUnreachableAsync([new EnvelopeTuple([], 0, 1)], CancellationToken.None);

            Assert.Equal(0, await CountOpenAsync(db, [rockId, jazzId, untaggedId]));
        }
    }

    // ---------------------------------------------------------------------
    // AC4 — the station default is the whole envelope when the grid is empty
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheStationDefaultWhenTheGridIsEmpty(DatabaseFixture db)
    {
        // Given no schedule blocks (FakeScheduleStore with an empty segment list), a station
        // default envelope of {rock}, and two non-rock rows plus one rock row, When the real
        // UnreachableGardenerPass runs — proving the pass -> repository arc, not just the SQL. Two
        // facts share this one arrangement.
        async Task<(long JazzId, long ClassicalId, long RockId)> ArrangeAsync()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-empty-grid-jazz.flac", genre: "jazz", energy: 0.5);
            var classicalId = await InsertPlayableRowAsync(db, "/gardener/t376-empty-grid-classical.flac", genre: "classical", energy: 0.5);
            var rockId = await InsertPlayableRowAsync(db, "/gardener/t376-empty-grid-rock.flac", genre: "rock", energy: 0.5);

            var stationDefault = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["rock"], EnergyRange.Unconstrained);
            var pass = new UnreachableGardenerPass(
                Repo(db), new FakeScheduleStore([]), new FakeStationDefaultEnvelopeSource(stationDefault));

            await pass.RunAsync(CancellationToken.None);

            return (jazzId, classicalId, rockId);
        }

        [Fact]
        public async Task NonRockRowsAreUnreachable()
        {
            var (jazzId, classicalId, _) = await ArrangeAsync();

            Assert.Equal(2, await CountOpenAsync(db, [jazzId, classicalId]));
        }

        [Fact]
        public async Task TheRockRowHasNoFinding()
        {
            var (_, _, rockId) = await ArrangeAsync();

            Assert.False((await ReadFindingAsync(db, rockId)).HasValue);
        }
    }

    // ---------------------------------------------------------------------
    // Per-field fallback — mirrors ScheduleResolver.BuildSegmentEnvelope exactly, THROUGH the pass
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPerFieldFallbackThroughThePass(DatabaseFixture db)
    {
        // Given a segment with Genres = null, EnergyMin = 0.6, EnergyMax = null, and a station
        // default of {rock}, [0,1] — the effective tuple is (rock, 0.6, 1.0) — plus a rock row at
        // energy 0.3 and a jazz row at energy 0.8, When the real pass runs. Two facts share this
        // one arrangement.
        async Task<(long RockLowEnergyId, long JazzHighEnergyId)> ArrangeAsync()
        {
            await db.ResetAsync();
            var rockLowEnergyId = await InsertPlayableRowAsync(db, "/gardener/t376-fallback-rock-low.flac", genre: "rock", energy: 0.3);
            var jazzHighEnergyId = await InsertPlayableRowAsync(db, "/gardener/t376-fallback-jazz-high.flac", genre: "jazz", energy: 0.8);

            var segment = new ScheduleSegment(null, DayOfWeek.Monday, 0, 1440, null, null, 0.6, null);
            var stationDefault = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["rock"], new EnergyRange(0, 1));
            var pass = new UnreachableGardenerPass(
                Repo(db), new FakeScheduleStore([segment]), new FakeStationDefaultEnvelopeSource(stationDefault));

            await pass.RunAsync(CancellationToken.None);

            return (rockLowEnergyId, jazzHighEnergyId);
        }

        [Fact]
        public async Task TheRockRowBelowTheFallenBackEnergyFloorReasonIsEnergy()
        {
            var (rockLowEnergyId, _) = await ArrangeAsync();

            var finding = await ReadFindingAsync(db, rockLowEnergyId);
            Assert.Equal("energy", ReasonOf(finding!.Value.Evidence));
        }

        [Fact]
        public async Task TheJazzRowReasonIsGenre()
        {
            var (_, jazzHighEnergyId) = await ArrangeAsync();

            var finding = await ReadFindingAsync(db, jazzHighEnergyId);
            Assert.Equal("genre", ReasonOf(finding!.Value.Evidence));
        }
    }

    // ---------------------------------------------------------------------
    // MED-1 — Genres = [] (explicit, no constraint) versus Genres = null (station default applies)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnExplicitEmptyGenresListMeansNoConstraint(DatabaseFixture db)
    {
        // Given a segment with Genres = [] (empty, NOT null) and a station default of {rock}, When
        // the real pass runs against a jazz row — the effective tuple carries NO genre constraint
        // at all (Genres ?? stationDefault.Genres never falls back, since [] is not null), so the
        // station default's own {rock} never applies.
        [Fact]
        public async Task TheJazzRowHasNoFinding()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-genres-empty-list.flac", genre: "jazz", energy: 0.5);
            var segment = new ScheduleSegment(null, DayOfWeek.Monday, 0, 1440, null, [], null, null);
            var stationDefault = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["rock"], new EnergyRange(0, 1));
            var pass = new UnreachableGardenerPass(
                Repo(db), new FakeScheduleStore([segment]), new FakeStationDefaultEnvelopeSource(stationDefault));

            await pass.RunAsync(CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, jazzId)).HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNullGenresMeansTheStationDefaultApplies(DatabaseFixture db)
    {
        // Given a segment with Genres = null (unset, distinct from an explicit empty list above)
        // and the SAME station default of {rock}, When the real pass runs against the SAME jazz
        // row — Genres ?? stationDefault.Genres now DOES fall back, so {rock} applies and the row
        // is unreachable.
        [Fact]
        public async Task TheJazzRowsFindingReasonIsGenre()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-genres-null.flac", genre: "jazz", energy: 0.5);
            var segment = new ScheduleSegment(null, DayOfWeek.Monday, 0, 1440, null, null, null, null);
            var stationDefault = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["rock"], new EnergyRange(0, 1));
            var pass = new UnreachableGardenerPass(
                Repo(db), new FakeScheduleStore([segment]), new FakeStationDefaultEnvelopeSource(stationDefault));

            await pass.RunAsync(CancellationToken.None);

            var finding = await ReadFindingAsync(db, jazzId);
            Assert.Equal("genre", ReasonOf(finding!.Value.Evidence));
        }
    }

    // ---------------------------------------------------------------------
    // Both-fail precedence — genre wins the tie
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBothFailPrecedence(DatabaseFixture db)
    {
        // Given one tuple {rock}, [0,0.5] and a jazz row at energy 0.9 — genre AND energy both
        // fail — When the pass runs.
        [Fact]
        public async Task TheReasonIsGenre()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-both-fail.flac", genre: "jazz", energy: 0.9);

            await Repo(db).ReconcileUnreachableAsync([new EnvelopeTuple(["rock"], 0, 0.5)], CancellationToken.None);

            var finding = await ReadFindingAsync(db, jazzId);
            Assert.Equal("genre", ReasonOf(finding!.Value.Evidence));
        }
    }

    // ---------------------------------------------------------------------
    // Genre matching is case-insensitive on the row's own stored casing
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGenreCaseInsensitivity(DatabaseFixture db)
    {
        // Given a tuple {rock} (already lower-cased, this type's own contract) and rows stored as
        // "rock" and "ROCK", When the pass runs. One count assertion over the homogeneous set.
        [Fact]
        public async Task BothRowsAreAdmitted()
        {
            await db.ResetAsync();
            var lowerId = await InsertPlayableRowAsync(db, "/gardener/t376-case-lower.flac", genre: "rock", energy: 0.5);
            var upperId = await InsertPlayableRowAsync(db, "/gardener/t376-case-upper.flac", genre: "ROCK", energy: 0.5);

            await Repo(db).ReconcileUnreachableAsync([new EnvelopeTuple(["rock"], 0, 1)], CancellationToken.None);

            Assert.Equal(0, await CountOpenAsync(db, [lowerId, upperId]));
        }
    }

    // ---------------------------------------------------------------------
    // AC5 — a schedule change heals an unreachable finding
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAScheduleChangeHealsIt(DatabaseFixture db)
    {
        // Given an open unreachable finding for a jazz row (tuples admit only rock), When a NEW
        // tuple set admitting jazz reconciles. Two facts share this one arrangement.
        async Task<long> ArrangeAsync()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-heals.flac", genre: "jazz", energy: 0.5);
            var repo = Repo(db);
            await repo.ReconcileUnreachableAsync([new EnvelopeTuple(["rock"], 0, 1)], CancellationToken.None);

            await repo.ReconcileUnreachableAsync([new EnvelopeTuple(["jazz"], 0, 1)], CancellationToken.None);

            return jazzId;
        }

        [Fact]
        public async Task TheFindingIsResolved()
        {
            var jazzId = await ArrangeAsync();

            Assert.Equal("resolved", (await ReadFindingAsync(db, jazzId))!.Value.State);
        }

        [Fact]
        public async Task ResolvedAtIsSet()
        {
            var jazzId = await ArrangeAsync();

            Assert.NotNull((await ReadFindingAsync(db, jazzId))!.Value.ResolvedAt);
        }
    }

    // ---------------------------------------------------------------------
    // Dismissed-forever
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioADismissedFindingStaysDismissed(DatabaseFixture db)
    {
        // Given an open finding dismissed at the store level, When the row is STILL unreachable and
        // a second reconcile runs, Then it stays dismissed (dismissed-forever, SPEC F153.2).
        [Fact]
        public async Task TheFindingStaysDismissed()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-dismissed.flac", genre: "jazz", energy: 0.5);
            var repo = Repo(db);
            var tuples = new[] { new EnvelopeTuple(["rock"], 0, 1) };
            await repo.ReconcileUnreachableAsync(tuples, CancellationToken.None);
            var findingId = (await ReadFindingAsync(db, jazzId))!.Value.Id;
            await repo.DismissAsync(findingId, CancellationToken.None);

            await repo.ReconcileUnreachableAsync(tuples, CancellationToken.None);

            Assert.Equal("dismissed", (await ReadFindingAsync(db, jazzId))!.Value.State);
        }
    }

    // ---------------------------------------------------------------------
    // opened_at stability + evidence.envelopes refresh
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOpenedAtIsStableWhileTheTupleCountChanges(DatabaseFixture db)
    {
        // Given an open finding opened against ONE tuple, When a SECOND reconcile runs against TWO
        // tuples that still don't admit the row. Both facts share this one arrangement (the
        // Story377 ScenarioOpenedAtIsStableWhileFieldsGrow idiom).
        async Task<(long MediaId, DateTimeOffset FirstOpenedAt)> ArrangeAsync()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-opened-at-stable.flac", genre: "jazz", energy: 0.5);
            var repo = Repo(db);
            await repo.ReconcileUnreachableAsync([new EnvelopeTuple(["rock"], 0, 1)], CancellationToken.None);
            var firstOpenedAt = (await ReadFindingAsync(db, jazzId))!.Value.OpenedAt;

            await repo.ReconcileUnreachableAsync(
                [new EnvelopeTuple(["rock"], 0, 1), new EnvelopeTuple(["classical"], 0, 1)], CancellationToken.None);

            return (jazzId, firstOpenedAt);
        }

        [Fact]
        public async Task OpenedAtIsUnchanged()
        {
            var (jazzId, firstOpenedAt) = await ArrangeAsync();

            Assert.Equal(firstOpenedAt, (await ReadFindingAsync(db, jazzId))!.Value.OpenedAt);
        }

        [Fact]
        public async Task EnvelopesCountRefreshesToTwo()
        {
            var (jazzId, _) = await ArrangeAsync();

            var finding = await ReadFindingAsync(db, jazzId);
            Assert.Equal(2, EnvelopeCountOf(finding!.Value.Evidence));
        }
    }

    // ---------------------------------------------------------------------
    // MED-2 — a row leaving PlayablePredicate's own scope resolves an open finding
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARowThatBecomesIneligibleResolves(DatabaseFixture db)
    {
        // Given an open unreachable finding for a jazz row, When that row is marked ineligible
        // (leaving PlayablePredicate's own scope entirely — not a genre/energy change at all) and a
        // second reconcile runs against the SAME tuple, Then the finding resolves — the resolve
        // half's own `not exists (... and PlayablePredicate and not adm.admitted)` finds no row at
        // all once eligible = false, exactly like every sibling pass's own predicate.
        [Fact]
        public async Task TheFindingResolves()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-becomes-ineligible.flac", genre: "jazz", energy: 0.5);
            var repo = Repo(db);
            var tuples = new[] { new EnvelopeTuple(["rock"], 0, 1) };
            await repo.ReconcileUnreachableAsync(tuples, CancellationToken.None);

            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update library.media set eligible = false where id = @jazzId", new { jazzId });
            await repo.ReconcileUnreachableAsync(tuples, CancellationToken.None);

            Assert.Equal("resolved", (await ReadFindingAsync(db, jazzId))!.Value.State);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the guard clause, and rows out of PlayablePredicate's own scope
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnEmptyEnvelopeListIsRejected(DatabaseFixture db)
    {
        // Given the repository, When ReconcileUnreachableAsync is called with an empty tuple list —
        // the caller's own station-default fallback guarantees this never happens in production, but
        // the store refuses outright rather than silently no-op-ing.
        [Fact]
        public async Task ItThrowsArgumentException() =>
            await Assert.ThrowsAsync<ArgumentException>(
                () => Repo(db).ReconcileUnreachableAsync([], CancellationToken.None));
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioExceedingTheEnvelopeCapIsRejected(DatabaseFixture db)
    {
        // T376 review MED-3. Given the repository, When ReconcileUnreachableAsync is called with
        // MaxEnvelopeTuples + 1 tuples — one more than the schema itself can ever produce
        // (db/27-segment-schedule-migration.sh's own 30-minute-step CHECK + non-overlap EXCLUDE
        // constraint), so a caller ever reaching this must be a bug upstream, refused outright.
        [Fact]
        public async Task ItThrowsArgumentException()
        {
            var tooMany = Enumerable.Range(0, RotFindingRepository.MaxEnvelopeTuples + 1)
                .Select(_ => new EnvelopeTuple([], 0, 1))
                .ToList();

            await Assert.ThrowsAsync<ArgumentException>(
                () => Repo(db).ReconcileUnreachableAsync(tooMany, CancellationToken.None));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioANeverPlayRowIsOutOfScope(DatabaseFixture db)
    {
        // Given a never_play row that would otherwise be unreachable, When the pass runs — curation
        // stays out of scope entirely, the same way every sibling pass's own predicate excludes it.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-never-play.flac", genre: "jazz", energy: 0.5);
            await SetNeverPlayAsync(db, jazzId);

            await Repo(db).ReconcileUnreachableAsync([new EnvelopeTuple(["rock"], 0, 1)], CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, jazzId)).HasValue);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnIneligibleRowIsOutOfScope(DatabaseFixture db)
    {
        // Given an ineligible row that would otherwise be unreachable, When the pass runs.
        [Fact]
        public async Task NoFindingIsOpened()
        {
            await db.ResetAsync();
            var jazzId = await InsertPlayableRowAsync(db, "/gardener/t376-ineligible.flac", genre: "jazz", energy: 0.5, eligible: false);

            await Repo(db).ReconcileUnreachableAsync([new EnvelopeTuple(["rock"], 0, 1)], CancellationToken.None);

            Assert.False((await ReadFindingAsync(db, jazzId)).HasValue);
        }
    }

    // ---------------------------------------------------------------------
    // AC6 — the join stays on the library side (pure text, no Postgres needed)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheJoinStaysOnTheLibrarySide
    {
        // Given the pass's own insert-select statement, When its SQL is inspected.
        [Fact]
        public void ItReferencesNoStationSchemaTable() =>
            Assert.DoesNotContain("station.", RotFindingRepository.BuildUnreachableInsertSql(1));

        // Given the pass's own resolve statement, When its SQL is inspected.
        [Fact]
        public void TheResolveStatementReferencesNoStationSchemaTableEither() =>
            Assert.DoesNotContain("station.", RotFindingRepository.BuildUnreachableResolveSql(1));
    }

    // ---------------------------------------------------------------------
    // MED-4 — the pass's own tuple matches ScheduleSegment.EffectiveEnvelope directly
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePassTupleMatchesEffectiveEnvelopeDirectly
    {
        // T376 review MED-4. Given a segment with Genres = null, EnergyMin = 0.6, EnergyMax = null
        // and a station default of {rock}, [0,1], When the real pass runs against a recording fake
        // IRotFindingStore — the tuple it hands the store carries the SAME genres/energy
        // ScheduleSegment.EffectiveEnvelope(stationDefault) yields when called directly, proving the
        // pass shares that ONE piece of code rather than an independently-derived copy.
        [Fact]
        public async Task TheReceivedTupleEqualsEffectiveEnvelopesOwnOutput()
        {
            var segment = new ScheduleSegment(null, DayOfWeek.Monday, 0, 1440, null, null, 0.6, null);
            var stationDefault = new SegmentEnvelope(TimeOnly.MinValue, TimeOnly.MaxValue, ["rock"], new EnergyRange(0, 1));
            var expectedEnvelope = segment.EffectiveEnvelope(stationDefault);
            var expectedTuple = new EnvelopeTuple(
                expectedEnvelope.Genres.Select(g => g.ToLowerInvariant()).OrderBy(g => g, StringComparer.Ordinal).ToList(),
                expectedEnvelope.EnergyRange.Min,
                expectedEnvelope.EnergyRange.Max);
            var recordingStore = new RecordingRotFindingStore();
            var pass = new UnreachableGardenerPass(
                recordingStore, new FakeScheduleStore([segment]), new FakeStationDefaultEnvelopeSource(stationDefault));

            await pass.RunAsync(CancellationToken.None);

            Assert.Equal(expectedTuple, Assert.Single(recordingStore.ReceivedEnvelopes!));
        }
    }

    // ---------------------------------------------------------------------
    // Dedup — the pass hands the repository ONE tuple for two segments that fold together
    // ---------------------------------------------------------------------

    public sealed class ScenarioTwoSegmentsDedupeToOneTupleDespiteCasingAndOrder
    {
        // T376 review LOW-3: genres {"Rock","jazz"} and {"JAZZ","rock"} — different casing AND
        // different ordering — fold to the textually identical tuple {jazz, rock}. Given two
        // schedule segments carrying those two differently-cased/ordered genre lists (same
        // energy), When the real pass runs against a recording fake IRotFindingStore — one
        // assertion on the received tuple count proves the fold, not merely that two IDENTICAL
        // inputs happen to compare equal.
        [Fact]
        public async Task ThePassHandsTheStoreOneTuple()
        {
            var segmentA = new ScheduleSegment(null, DayOfWeek.Monday, 0, 480, null, ["Rock", "jazz"], 0.2, 0.8);
            var segmentB = new ScheduleSegment(null, DayOfWeek.Monday, 480, 960, null, ["JAZZ", "rock"], 0.2, 0.8);
            var recordingStore = new RecordingRotFindingStore();
            var pass = new UnreachableGardenerPass(
                recordingStore, new FakeScheduleStore([segmentA, segmentB]), new FakeStationDefaultEnvelopeSource(SegmentEnvelope.StationDefault));

            await pass.RunAsync(CancellationToken.None);

            Assert.Single(recordingStore.ReceivedEnvelopes!);
        }
    }
}
