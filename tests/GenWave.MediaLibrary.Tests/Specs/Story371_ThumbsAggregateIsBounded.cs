// STORY-371 — The aggregate is bounded and remembers (SPEC F150.9 · PLAN T365)
// STORY-369 AC6/AC7 — idempotency and the flip, proven at the repository (the Host cookie/token
// wiring around IThumbStore is T366's own scope, not this task's).
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection. AC1–AC3/
// AC10 seed library.media_thumb rows DIRECTLY (bypassing MediaThumbRepository.RecordAsync) at
// controlled ages/counts and drive the aggregate through the real repository
// (IThumbStore.RecomputeAllAsync/SweepAsync) — the SQL formula's own contract, independent of the
// upsert write path. The STORY-369/safe-scope/unknown-media/never-aired/disjointness facts below
// drive RecordAsync itself, over DatabaseFixture, mirroring Story110_RatingPersistence.cs's own
// "real repository, real Postgres" posture for a taste-write seam one table over.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Garden;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureThumbsAggregateIsBounded
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static readonly DateTimeOffset AiringStartedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    static MediaThumbRepository Repo(DatabaseFixture db, ISafeScopeProvider? safeScope = null, GardenerOptions? gardener = null) =>
        new(db.DataSource, safeScope ?? new FakeSafeScopeProvider(), new FakeOptionsMonitor<GardenerOptions>(gardener ?? new GardenerOptions()));

    /// <summary>MediaRotationRepository, wired the SAME way MediaLibraryServiceCollectionExtensions
    /// wires it in production (own StationSettingsRepository instance over the fixture's own station
    /// connection string) — needed for the T365 review HIGH-2 pin below, which drives
    /// RecordAiringAsync directly to prove RecordAsync's own media_rotation upsert never fights the
    /// FIRST real airing over who gets to set first_aired_at.</summary>
    static MediaRotationRepository RotationRepo(DatabaseFixture db, ISafeScopeProvider? safeScope = null) =>
        new(db.DataSource, new StationSettingsRepository(db.StationConnectionString), safeScope ?? new FakeSafeScopeProvider());

    /// <summary>Minimal library.media row (Story110_RatingPersistence.cs's own InsertMediaRowAsync
    /// idiom, one column added): IThumbStore only ever cares that the id exists and, for the
    /// safe-scope facts, its library_id — measurable/state are irrelevant to this seam.</summary>
    static async Task<long> InsertMediaRowAsync(DatabaseFixture db, string path, long libraryId = 1)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            """
            insert into library.media (path, format, size_bytes, mtime, state, library_id)
            values (@path, 'flac', 1024, now(), 'discovered', @libraryId)
            returning id
            """,
            new { path, libraryId });
    }

    /// <summary>Ensures a library.media_rotation row exists without going through RecordAsync — the
    /// AC1–AC3/AC10/RecomputeAllAsync facts below are about the AGGREGATE's own contract, not the
    /// upsert write path (which earns its own coverage further down this file).</summary>
    static async Task EnsureRotationRowAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rotation (media_id) values (@mediaId) on conflict (media_id) do nothing",
            new { mediaId });
    }

    /// <summary>Seeds a fresh (created_at = now()) library.media_thumb row directly.</summary>
    static async Task InsertFreshThumbRowAsync(DatabaseFixture db, long mediaId, string listenerKey, string direction)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into library.media_thumb (media_id, airing_started_at, listener_key, direction, source)
            values (@mediaId, now(), @listenerKey, @direction::library.thumb_direction, 'spectator')
            """,
            new { mediaId, listenerKey, direction });
    }

    /// <summary>Seeds a library.media_thumb row aged by <paramref name="ageInterval"/> — a Postgres
    /// interval LITERAL (e.g. <c>"30 days"</c>), computed as <c>now() - interval '...'</c> IN SQL so
    /// the seeded age and the recompute's own <c>now()</c> share the exact same clock (no test-host
    /// vs. container clock-skew risk). <paramref name="ageInterval"/> is a test-only constant, never
    /// caller/user input — safe to splice into the SQL text.</summary>
    static async Task InsertAgedThumbRowAsync(DatabaseFixture db, long mediaId, string listenerKey, string direction, string ageInterval)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            $"""
            insert into library.media_thumb (media_id, airing_started_at, listener_key, direction, source, created_at)
            values (@mediaId, now(), @listenerKey, @direction::library.thumb_direction, 'spectator', now() - interval '{ageInterval}')
            """,
            new { mediaId, listenerKey, direction });
    }

    static async Task<int> CountThumbRowsAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "select count(*)::int from library.media_thumb where media_id = @mediaId", new { mediaId });
    }

    static async Task<(int PlayCount, DateTimeOffset? FirstAiredAt, int ThumbsUp, int ThumbsDown, double Nudge)?> ReadRotationAsync(DatabaseFixture db, long mediaId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<(int, DateTimeOffset?, int, int, double)?>(
            "select play_count, first_aired_at, thumbs_up, thumbs_down, nudge from library.media_rotation where media_id = @mediaId",
            new { mediaId });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the formula, its decay, and its clamp
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheAggregateFormula(DatabaseFixture db)
    {
        // Given five up-thumbs on a track within the hour, HalfLifeDays 30, Saturation 5, When
        // the aggregate is recomputed (STORY-371 AC1).
        [Fact]
        public async Task NudgeIsOne()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/ac1.flac");
            await EnsureRotationRowAsync(db, mediaId);
            for (var i = 0; i < 5; i++)
                await InsertFreshThumbRowAsync(db, mediaId, $"listener-{i}", "up");

            await Repo(db).RecomputeAllAsync(CancellationToken.None);

            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(1.0, rotation!.Value.Nudge, 3);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDecay(DatabaseFixture db)
    {
        // Given one up-thumb aged exactly 30 days, When the aggregate is recomputed (STORY-371 AC2:
        // nudge is 0.1 = 0.5 / Saturation 5).
        [Fact]
        public async Task NudgeIsZeroPointOne()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/ac2.flac");
            await EnsureRotationRowAsync(db, mediaId);
            await InsertAgedThumbRowAsync(db, mediaId, "listener-1", "up", "30 days");

            await Repo(db).RecomputeAllAsync(CancellationToken.None);

            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(0.1, rotation!.Value.Nudge, 3);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheClamp(DatabaseFixture db)
    {
        // Given twelve up-thumbs within the hour, When the aggregate is recomputed (STORY-371 AC3:
        // the raw sum/saturation would be 2.4 — the clamp holds it at 1.0).
        [Fact]
        public async Task NudgeClampsToOneNotTwoPointFour()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/ac3.flac");
            await EnsureRotationRowAsync(db, mediaId);
            for (var i = 0; i < 12; i++)
                await InsertFreshThumbRowAsync(db, mediaId, $"listener-{i}", "up");

            await Repo(db).RecomputeAllAsync(CancellationToken.None);

            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(1.0, rotation!.Value.Nudge, 3);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRecomputeAllAsyncDecaysAnAgedThumb(DatabaseFixture db)
    {
        // Given a media_rotation row with a stale, hand-seeded nudge of 0.2 and its ONLY thumb aged
        // exactly 30 days, When RecomputeAllAsync runs, Then the stored value is overwritten by the
        // real formula (0.1) — proving the hourly pass re-derives nudge from library.media_thumb
        // every time rather than trusting whatever value is already on the row.
        [Fact]
        public async Task NudgeDecaysFromZeroPointTwoToZeroPointOne()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/recompute-all.flac");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "insert into library.media_rotation (media_id, nudge) values (@mediaId, 0.2)", new { mediaId });
            await InsertAgedThumbRowAsync(db, mediaId, "listener-1", "up", "30 days");

            await Repo(db).RecomputeAllAsync(CancellationToken.None);

            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(0.1, rotation!.Value.Nudge, 3);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the sweep never touches the counters
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetentionKeepsTheCounters(DatabaseFixture db)
    {
        // Given thumb rows older than ThumbRetentionDays, When the sweep runs (STORY-371 AC10).
        // Each fact re-arranges independently (db.ResetAsync) — the shared shape (one aged row, a
        // hand-seeded counter/nudge state distinct from the swept row's own contribution) lives in
        // this one method so both facts drive the identical scenario.
        async Task<long> SeedAgedRowAndSweepAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/ac10.flac");
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "insert into library.media_rotation (media_id, thumbs_up, thumbs_down, nudge) " +
                    "values (@mediaId, 3, 1, 0.4)", new { mediaId });
            await InsertAgedThumbRowAsync(db, mediaId, "listener-1", "up", "91 days");

            await Repo(db).SweepAsync(CancellationToken.None);
            return mediaId;
        }

        [Fact]
        public async Task TheOldRowsAreGone()
        {
            var mediaId = await SeedAgedRowAndSweepAsync();
            Assert.Equal(0, await CountThumbRowsAsync(db, mediaId));
        }

        [Fact]
        public async Task ThumbsUpDownAndNudgeOnMediaRotationAreUnchanged()
        {
            var mediaId = await SeedAgedRowAndSweepAsync();
            var rotation = await ReadRotationAsync(db, mediaId);
            // nudge is a Postgres `real` (single precision): 0.4 widens to 0.40000000596046448 once
            // Npgsql reads it back as a double (Story372_ThePoolHonoursTheRotationPredicate.cs's own
            // MED-1 remarks) — rounding both sides to 3 decimals absorbs that widening while still
            // proving the sweep left every value byte-for-byte where it started.
            Assert.Equal(
                (3, 1, 0.4),
                (rotation!.Value.ThumbsUp, rotation.Value.ThumbsDown, Math.Round(rotation.Value.Nudge, 3)));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — RecordAsync's own idempotency and flip (STORY-369 AC6/AC7, at the repository)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARepeatThumbIsIdempotent(DatabaseFixture db)
    {
        // Given a listener who thumbed X up, When the same listener thumbs X up again (STORY-369
        // AC6): media_thumb still holds exactly one row, and nothing about the aggregate moves.
        async Task<(long MediaId, ThumbWriteResult Second)> ThumbTwiceAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/ac6.flac");
            var repo = Repo(db);
            await repo.RecordAsync(mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);
            var second = await repo.RecordAsync(mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);
            return (mediaId, second);
        }

        [Fact]
        public async Task TheSecondCallReturnsUnchanged()
        {
            var (_, second) = await ThumbTwiceAsync();
            Assert.Equal(ThumbWriteResult.Unchanged, second);
        }

        [Fact]
        public async Task StillExactlyOneRow()
        {
            var (mediaId, _) = await ThumbTwiceAsync();
            Assert.Equal(1, await CountThumbRowsAsync(db, mediaId));
        }

        [Fact]
        public async Task TheCounterIsNotDoubleCounted()
        {
            var (mediaId, _) = await ThumbTwiceAsync();
            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(1, rotation!.Value.ThumbsUp);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAFlipUpdatesTheDirection(DatabaseFixture db)
    {
        // Given a listener who thumbed X up, When the same listener thumbs X down (STORY-369 AC7):
        // the one row's direction becomes down and the aggregate reflects −1 not +1 — a single fresh
        // thumb at Saturation 5 nudges to exactly −0.2.
        async Task<(long MediaId, ThumbWriteResult Second)> ThumbThenFlipAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/ac7.flac");
            var repo = Repo(db);
            await repo.RecordAsync(mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);
            var second = await repo.RecordAsync(mediaId, AiringStartedAt, "listener-1", ThumbDirection.Down, ThumbSource.Spectator, CancellationToken.None);
            return (mediaId, second);
        }

        [Fact]
        public async Task TheSecondCallReturnsFlipped()
        {
            var (_, second) = await ThumbThenFlipAsync();
            Assert.Equal(ThumbWriteResult.Flipped, second);
        }

        [Fact]
        public async Task StillExactlyOneRow()
        {
            var (mediaId, _) = await ThumbThenFlipAsync();
            Assert.Equal(1, await CountThumbRowsAsync(db, mediaId));
        }

        [Fact]
        public async Task TheAggregateReflectsMinusPointTwo()
        {
            var (mediaId, _) = await ThumbThenFlipAsync();
            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(-0.2, rotation!.Value.Nudge, 3);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — safe-scope + unknown media both refuse, structurally indistinguishable (F150.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASafeScopeRowIsRefused(DatabaseFixture db)
    {
        async Task<(long MediaId, ThumbWriteResult Result)> ThumbSafeScopeRowAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/safe-scope.flac", libraryId: 1);
            var repo = Repo(db, safeScope: new FakeSafeScopeProvider(1));
            var result = await repo.RecordAsync(
                mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);
            return (mediaId, result);
        }

        [Fact]
        public async Task ItReturnsIgnored()
        {
            var (_, result) = await ThumbSafeScopeRowAsync();
            Assert.Equal(ThumbWriteResult.Ignored, result);
        }

        [Fact]
        public async Task NoThumbRowIsWritten()
        {
            var (mediaId, _) = await ThumbSafeScopeRowAsync();
            Assert.Equal(0, await CountThumbRowsAsync(db, mediaId));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnUnknownMediaIdIsRefused(DatabaseFixture db)
    {
        const long UnknownMediaId = 999_999_999L;

        async Task<ThumbWriteResult> ThumbUnknownMediaAsync()
        {
            await db.ResetAsync();
            return await Repo(db).RecordAsync(
                UnknownMediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);
        }

        [Fact]
        public async Task ItReturnsIgnored()
        {
            Assert.Equal(ThumbWriteResult.Ignored, await ThumbUnknownMediaAsync());
        }

        [Fact]
        public async Task NoThumbRowIsWritten()
        {
            await ThumbUnknownMediaAsync();
            Assert.Equal(0, await CountThumbRowsAsync(db, UnknownMediaId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a thumbed-but-never-aired track still carries a nudge
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioANeverAiredTrackStillGetsARotationRow(DatabaseFixture db)
    {
        async Task<long> ThumbNeverAiredTrackAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/never-aired.flac");
            await Repo(db).RecordAsync(
                mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);
            return mediaId;
        }

        [Fact]
        public async Task PlayCountIsZero()
        {
            var mediaId = await ThumbNeverAiredTrackAsync();
            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(0, rotation!.Value.PlayCount);
        }

        [Fact]
        public async Task FirstAiredAtIsNull()
        {
            var mediaId = await ThumbNeverAiredTrackAsync();
            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Null(rotation!.Value.FirstAiredAt);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — F150.1 disjointness: a thumb never reaches media_rating or persona_taste
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAThumbNeverTouchesRatingOrPersonaTaste(DatabaseFixture db)
    {
        // station.persona_taste is NOT covered by db.ResetAsync() (library-schema only) and is
        // shared, in this same DatabaseCollection, with Persona spec classes that legitimately write
        // rows there — so this fact proves "byte-identical before/after" (STORY-370 AC2's own
        // phrasing for the sibling operator-thumb seam) rather than asserting an empty table, which
        // would be a false claim about a table this repository has no say over.
        static async Task<int> CountPersonaTasteRowsAsync(DatabaseFixture db)
        {
            await using var conn = await db.StationDataSource.OpenConnectionAsync();
            return await conn.ExecuteScalarAsync<int>("select count(*)::int from station.persona_taste");
        }

        // library.media_rating IS covered by db.ResetAsync() (CASCADE truncate — that method's own
        // remarks), so this fact can assert the stronger "stays empty" claim directly.
        [Fact]
        public async Task MediaRatingStaysEmpty()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/disjointness-rating.flac");

            await Repo(db).RecordAsync(
                mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>("select count(*)::int from library.media_rating");
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task PersonaTasteIsByteIdenticalBeforeAndAfter()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/disjointness-persona.flac");
            var before = await CountPersonaTasteRowsAsync(db);

            await Repo(db).RecordAsync(
                mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);

            var after = await CountPersonaTasteRowsAsync(db);
            Assert.Equal(before, after);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — T365 review LOW-1: an oversized listener_key never reaches SQL
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnOversizedListenerKeyIsRefused(DatabaseFixture db)
    {
        // T365 review LOW-1, reproduced: a 4,000-char listener_key blew the btree index row-size
        // limit on the (media_id, airing_started_at, listener_key) UNIQUE constraint, surfacing as an
        // unhandled 500 on the anonymous spectator path. Guarded before any SQL runs.
        async Task<ThumbWriteResult> ThumbWithAnOversizedKeyAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/low1-oversized-key.flac");
            var oversizedKey = new string('a', 4_000);
            return await Repo(db).RecordAsync(
                mediaId, AiringStartedAt, oversizedKey, ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);
        }

        [Fact]
        public async Task ItReturnsIgnored()
        {
            Assert.Equal(ThumbWriteResult.Ignored, await ThumbWithAnOversizedKeyAsync());
        }

        [Fact]
        public async Task NoThumbRowIsWritten()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/low1-oversized-key-2.flac");
            var oversizedKey = new string('a', 4_000);
            await Repo(db).RecordAsync(
                mediaId, AiringStartedAt, oversizedKey, ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);

            Assert.Equal(0, await CountThumbRowsAsync(db, mediaId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — T365 review HIGH-1: two concurrent RecordAsync calls for one key never throw
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioConcurrentRecordAsyncCallsForOneKeyDoNotThrow(DatabaseFixture db)
    {
        // T365 review HIGH-1, reproduced with two live sessions against real Postgres: the earlier
        // pre-read-then-write shape let two concurrent callers for the IDENTICAL
        // (media, airing, listener) key both observe "no existing row" and both attempt the INSERT —
        // the loser threw PostgresException 23505 straight out of RecordAsync. The atomic
        // ON CONFLICT ... DO UPDATE ... WHERE upsert removes the gap: Task.WhenAll over two
        // concurrent calls on the SAME repository (each opens its own connection off the shared
        // NpgsqlDataSource — genuine concurrent Postgres sessions, not two C# tasks serialized onto
        // one connection) must complete with no exception, exactly one surviving row, and one
        // Recorded + one Unchanged (same direction on both calls).
        async Task<(long MediaId, ThumbWriteResult[] Results)> ThumbConcurrentlyAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/high1-concurrent.flac");
            var repo = Repo(db);

            // No exception propagating out of this await IS part of what HIGH-1 pins — the old shape
            // would have faulted one of these two tasks with a raw PostgresException.
            var results = await Task.WhenAll(
                repo.RecordAsync(mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None),
                repo.RecordAsync(mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None));

            return (mediaId, results);
        }

        [Fact]
        public async Task ExactlyOneRowSurvives()
        {
            var (mediaId, _) = await ThumbConcurrentlyAsync();
            Assert.Equal(1, await CountThumbRowsAsync(db, mediaId));
        }

        [Fact]
        public async Task OneCallIsRecordedAndTheOtherIsUnchanged()
        {
            var (_, results) = await ThumbConcurrentlyAsync();
            Assert.Equal(
                new[] { ThumbWriteResult.Recorded, ThumbWriteResult.Unchanged }.OrderBy(r => r),
                results.OrderBy(r => r));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — T365 review HIGH-2: a thumb-then-airing track still gets first_aired_at stamped
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAThumbThenAnAiringStampsFirstAiredAt(DatabaseFixture db)
    {
        // T365 review HIGH-2, reproduced: MediaThumbRepository.RecordAsync can be the FIRST writer of
        // a library.media_rotation row (a thumb on a never-aired track), always with first_aired_at
        // NULL by construction. Before MediaRotationRepository.RecordAiringAsync's own fix, its DO
        // UPDATE branch never touched first_aired_at at all, so the track's FIRST real airing left
        // that column permanently NULL despite play_count going to 1.
        async Task<long> ThumbThenAirAsync()
        {
            await db.ResetAsync();
            var mediaId = await InsertMediaRowAsync(db, "/thumbs/high2-thumb-then-air.flac");
            await Repo(db).RecordAsync(
                mediaId, AiringStartedAt, "listener-1", ThumbDirection.Up, ThumbSource.Spectator, CancellationToken.None);

            await RotationRepo(db).RecordAiringAsync(mediaId, DateTimeOffset.UtcNow, CancellationToken.None);
            return mediaId;
        }

        [Fact]
        public async Task FirstAiredAtIsNonNull()
        {
            var mediaId = await ThumbThenAirAsync();
            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.NotNull(rotation!.Value.FirstAiredAt);
        }

        [Fact]
        public async Task PlayCountIsOne()
        {
            var mediaId = await ThumbThenAirAsync();
            var rotation = await ReadRotationAsync(db, mediaId);
            Assert.Equal(1, rotation!.Value.PlayCount);
        }
    }
}
