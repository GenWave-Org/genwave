// STORY-372 — The pool honours the rotation predicate (SPEC F152.2 · PLAN T359)
//
// BDD specification — xUnit. PENDING until T359. Arrange sketch: DatabaseFixture — seed ready +
// measurable + eligible rows in library 1 via MediaRepository (Story212_EnvelopeCandidateQuery.cs's
// InsertReadyAsync idiom) plus a library.media_rotation row per play-count/last-aired-at case,
// then call GetEnvelopeCandidatePoolAsync with a SegmentEnvelope carrying Rotation and assert the
// returned id set.
//
// Integration: hits real Postgres via DatabaseCollection — mirrors Story212_EnvelopeCandidateQuery's
// own rationale (the by-construction WHERE predicate is selection SQL, provable only against the
// real planner).

using System.Text.Json;
using System.Threading.Channels;
using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureThePoolHonoursTheRotationPredicate
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Story212_EnvelopeCandidateQuery.cs's own InsertReadyAsync idiom: a ready + measurable
    /// + eligible row in library 1, LUFS controllable so RecomputeEnergyPercentilesAsync's own energy
    /// percentile never accidentally excludes a seeded row from the (unconstrained) energy band
    /// this feature's envelopes always use.</summary>
    static async Task<long> InsertReadyAsync(MediaRepository repo, string path)
    {
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, new EnrichmentResult(
            DurationMs: 180_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Title: "t", Artist: "a", Album: "al", AlbumArtist: "aa", Genre: "g", TrackNo: 1, Year: 2020,
            Explicit: null,
            IntegratedLufs: -14.0, TruePeakDbtp: -1.0, Measurable: true,
            CueInSec: null, CueOutSec: null, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: null, BpmAnalyzedAt: DateTime.UtcNow), CancellationToken.None);
        return id;
    }

    /// <summary>Seeds a library.media_rotation row directly — MediaRotationRepository is not involved
    /// in the pool query (it reads the ledger table via a plain LEFT JOIN), so the fixture writes the
    /// row straight over the fixture's own library data source, mirroring
    /// Story110_RatingPersistence.cs's InsertMediaRowAsync raw-SQL idiom for the sibling
    /// media_rating table.</summary>
    static async Task SeedRotationAsync(
        DatabaseFixture db, long mediaId, int playCount = 0, DateTimeOffset? lastAiredAt = null, double nudge = 0)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "insert into library.media_rotation (media_id, play_count, last_aired_at, nudge) " +
            "values (@mediaId, @playCount, @lastAiredAt, @nudge)",
            new { mediaId, playCount, lastAiredAt, nudge });
    }

    static readonly SegmentEnvelope UnconstrainedEnvelope =
        new(TimeOnly.MinValue, TimeOnly.MaxValue, [], EnergyRange.Unconstrained);

    // ---------------------------------------------------------------------
    // HAPPY PATH — MaxPlays and NotAiredWithinDays, by construction
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePoolHonoursMaxPlays(DatabaseFixture db)
    {
        // Given an envelope with Rotation MaxPlays 0 and a library where 6 rows never aired and
        // 4 did, When the candidate pool is queried.
        [Fact]
        public async Task OnlyTheSixNeverAiredRowsAreReturned()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var neverAiredIds = new List<long>();
            for (var i = 0; i < 6; i++)
                neverAiredIds.Add(await InsertReadyAsync(repo, $"/rotation/max-plays-never-{i}.flac"));

            for (var i = 0; i < 4; i++)
            {
                var id = await InsertReadyAsync(repo, $"/rotation/max-plays-aired-{i}.flac");
                await SeedRotationAsync(db, id, playCount: 1);
            }

            var catalog = (IMediaCatalog)repo;
            var scope = new LibraryScope([1L]);
            var envelope = UnconstrainedEnvelope with { Rotation = new RotationPredicate(MaxPlays: 0) };

            var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                scope, [], artistSeparation: 0, envelope, limit: 20, CancellationToken.None);

            Assert.Equal(
                neverAiredIds.Select(id => id.ToString()).OrderBy(x => x, StringComparer.Ordinal),
                pool.Select(c => c.Media.MediaId).OrderBy(x => x, StringComparer.Ordinal));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePoolHonoursNotAiredWithinDays(DatabaseFixture db)
    {
        // Given Rotation NotAiredWithinDays 30 and rows last aired 10, 40, never, When the pool
        // is queried.
        [Fact]
        public async Task TheFortyDayAndNeverRowsAreReturned()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var now = DateTimeOffset.UtcNow;

            var recentId = await InsertReadyAsync(repo, "/rotation/not-aired-recent.flac");
            await SeedRotationAsync(db, recentId, lastAiredAt: now.AddDays(-10));

            var staleId = await InsertReadyAsync(repo, "/rotation/not-aired-stale.flac");
            await SeedRotationAsync(db, staleId, lastAiredAt: now.AddDays(-40));

            var neverId = await InsertReadyAsync(repo, "/rotation/not-aired-never.flac");

            var catalog = (IMediaCatalog)repo;
            var scope = new LibraryScope([1L]);
            var envelope = UnconstrainedEnvelope with { Rotation = new RotationPredicate(NotAiredWithinDays: 30) };

            var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                scope, [], artistSeparation: 0, envelope, limit: 20, CancellationToken.None);

            Assert.Equal(
                new[] { staleId.ToString(), neverId.ToString() }.OrderBy(x => x, StringComparer.Ordinal),
                pool.Select(c => c.Media.MediaId).OrderBy(x => x, StringComparer.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // MED-1 (T359 review) — the F151.1 projection, proven by value against real Postgres
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePoolProjectsTheLedgerValues(DatabaseFixture db)
    {
        // MED-1 (T359 review): MediaRepository's `coalesce(rot.nudge, 0) as nudge,
        // coalesce(rot.play_count, 0) as play_count` projection (SPEC F151.1) had no DB-backed value
        // assertion — Story371_TheNudgeInTheRanker.cs's AC4 facts prove ToRankCandidate's MAPPING but
        // hand-construct the EnvelopeCandidateRow, never round-tripping the actual SQL/Dapper
        // read. Given a library.media_rotation row with nudge 0.6 and play_count 3, When the pool is
        // queried through the real repository (GetEnvelopeCandidatePoolAsync, no Rotation predicate
        // needed here — this scenario is about the PROJECTION, not the filter).
        async Task<EnvelopeCandidateRow> QuerySeededRowAsync()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await InsertReadyAsync(repo, "/rotation/ledger-projection.flac");
            await SeedRotationAsync(db, id, playCount: 3, nudge: 0.6);

            var catalog = (IMediaCatalog)repo;
            var scope = new LibraryScope([1L]);
            var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                scope, [], artistSeparation: 0, UnconstrainedEnvelope, limit: 20, CancellationToken.None);

            return Assert.Single(pool);
        }

        [Fact]
        public async Task ThePlayCountMatchesTheLedger() =>
            Assert.Equal(3, (await QuerySeededRowAsync()).PlayCount);

        // library.media_rotation.nudge is a Postgres `real` (single precision) column: 0.6 widens to
        // 0.6000000238418579 once Npgsql reads it back as a double, so the comparison MUST tolerate
        // that (MED-1, T359 review) — 5 decimal digits comfortably absorbs the widening error while
        // still catching a genuine mismatch.
        [Fact]
        public async Task TheNudgeMatchesTheLedgerWithinFloatTolerance() =>
            Assert.Equal(0.6, (await QuerySeededRowAsync()).Nudge, 5);
    }

    // ---------------------------------------------------------------------
    // LOW-2 (T359 review) — both bounds set together (legal per F152.1): the AND, not just each leg
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThePoolHonoursBothBoundsTogether(DatabaseFixture db)
    {
        // Given a Rotation with BOTH MaxPlays 1 and NotAiredWithinDays 30 set (SPEC F152.1 allows
        // either or both), and three rows — one that satisfies both bounds, one that satisfies only
        // NotAiredWithinDays (its play_count fails MaxPlays), one that satisfies only MaxPlays (its
        // last_aired_at is too recent) — When the pool is queried, Then only the row satisfying BOTH
        // bounds is returned: RotationPredicateSql's `string.Join(" and ", parts)` branch (both parts
        // non-empty) composes as an intersection, not an independent OR of either leg passing.
        [Fact]
        public async Task OnlyTheRowSatisfyingBothBoundsIsReturned()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var now = DateTimeOffset.UtcNow;

            var bothId = await InsertReadyAsync(repo, "/rotation/both-bounds-both.flac");
            await SeedRotationAsync(db, bothId, playCount: 0, lastAiredAt: now.AddDays(-40));

            var failsMaxPlaysId = await InsertReadyAsync(repo, "/rotation/both-bounds-fails-max-plays.flac");
            await SeedRotationAsync(db, failsMaxPlaysId, playCount: 5, lastAiredAt: now.AddDays(-40));

            var failsDaysId = await InsertReadyAsync(repo, "/rotation/both-bounds-fails-days.flac");
            await SeedRotationAsync(db, failsDaysId, playCount: 0, lastAiredAt: now.AddDays(-10));

            var catalog = (IMediaCatalog)repo;
            var scope = new LibraryScope([1L]);
            var envelope = UnconstrainedEnvelope with
            {
                Rotation = new RotationPredicate(MaxPlays: 1, NotAiredWithinDays: 30),
            };

            var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                scope, [], artistSeparation: 0, envelope, limit: 20, CancellationToken.None);

            Assert.Equal(
                new[] { bothId.ToString() },
                pool.Select(c => c.Media.MediaId).OrderBy(x => x, StringComparer.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // T361 — the R2 diagnostic read (GetPlayCountQuantileAsync)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheQuantileRead(DatabaseFixture db)
    {
        // Given 5 rows with play_counts 0, 1, 2, 10, 50, When the R2 diagnostic read is queried at
        // two different quantiles — Postgres' own percentile_disc picks the 1-based rank
        // CEIL(quantile * n) into the ascending-sorted set [0, 1, 2, 10, 50]: 0.1 -> rank
        // CEIL(0.5) = 1 -> value 0; 0.5 -> rank CEIL(2.5) = 3 -> value 2 (verified directly against a
        // real Postgres instance). The ladder's own R2 rung only ever asks for 0.1; this fact also
        // pins 0.5 so a future caller of a different quantile is proven too, not just the one rung
        // MusicSelectionPolicy happens to use today.
        async Task SeedFiveRowsAsync()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var playCounts = new[] { 0, 1, 2, 10, 50 };
            for (var i = 0; i < playCounts.Length; i++)
            {
                var id = await InsertReadyAsync(repo, $"/rotation/quantile-{i}.flac");
                await SeedRotationAsync(db, id, playCount: playCounts[i]);
            }
        }

        [Fact]
        public async Task TheTenthPercentileIsZero()
        {
            await SeedFiveRowsAsync();
            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var p10 = await catalog.GetPlayCountQuantileAsync(scope, UnconstrainedEnvelope, 0.1, CancellationToken.None);

            Assert.Equal(0, p10);
        }

        [Fact]
        public async Task TheFiftiethPercentileIsTwo()
        {
            await SeedFiveRowsAsync();
            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var p50 = await catalog.GetPlayCountQuantileAsync(scope, UnconstrainedEnvelope, 0.5, CancellationToken.None);

            Assert.Equal(2, p50);
        }

        // Sad path: nothing to compute a percentile over (an empty pool) answers null — SPEC F152.4's
        // own "skip R2" signal, never a fabricated 0.
        [Fact]
        public async Task AnEmptyPoolAnswersNull()
        {
            await db.ResetAsync();
            var catalog = (IMediaCatalog)Harness.Repo(db);
            var scope = new LibraryScope([1L]);

            var quantile = await catalog.GetPlayCountQuantileAsync(scope, UnconstrainedEnvelope, 0.1, CancellationToken.None);

            Assert.Null(quantile);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no predicate, no drift
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNoPredicateNoStamp(DatabaseFixture db)
    {
        /// <summary>No-op <see cref="IActivePersonaAccessor"/> double — mirrors
        /// Story329_CrosstalkBoothStamp.cs's own idiom: these two facts assert on <c>pick</c> alone,
        /// so a fixed "nothing active" answer keeps them focused.</summary>
        sealed class NullPersonaAccessor : IActivePersonaAccessor
        {
            public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
        }

        /// <summary>Publishes <paramref name="evt"/> through the real <see cref="BoothLogWriter"/> and
        /// reads back the ONE <see cref="BoothLogAppendRequest"/> it queued — no drain, no database
        /// (mirrors Story329_CrosstalkBoothStamp.cs's own <c>PublishAndCaptureAsync</c>): this fact's
        /// own concern stops at the stamp <see cref="BoothLogWriter"/> itself builds.</summary>
        static async Task<BoothLogAppendRequest> PublishAndCaptureAsync(StationEvent evt)
        {
            var channel = Channel.CreateBounded<BoothLogAppendRequest>(1);
            var writer = new BoothLogWriter(channel.Writer, new NullPersonaAccessor(), NullLogger<BoothLogWriter>.Instance);

            writer.Publish(evt);

            return await channel.Reader.ReadAsync();
        }

        // T361 (STORY-372 AC10, the stamp half): a TrackAired with no PersonaPick, no
        // CrosstalkScript, and no RotationRelax (envelope.Rotation was never set for this pick — the
        // byte-identical no-rotation path) stamps NOTHING — the everyday shape every pre-F152 airing
        // already has, unchanged.
        [Fact]
        public async Task RotationRelaxIsAbsentFromEveryStamp()
        {
            var musicAiring = new TrackAired("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000);

            var request = await PublishAndCaptureAsync(musicAiring);

            Assert.Null(request.Pick);
        }

        // The positive twin (T361): a TrackAired carrying RotationRelax 2 — a ladder pick with NO
        // persona opinion at all (SPEC F81.6's terminal fallback still stamps the relax step,
        // STORY-372 AC9's own shape) — stamps a pick jsonb with empty firedRules/isExploration:false
        // plus the relax step, never a lost value.
        [Fact]
        public async Task RotationRelaxTwoAppearsInTheStamp()
        {
            var laddered = new TrackAired("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000)
            {
                RotationRelax = 2,
            };

            var request = await PublishAndCaptureAsync(laddered);

            Assert.NotNull(request.Pick);
            using var document = JsonDocument.Parse(request.Pick);
            Assert.Equal(2, document.RootElement.GetProperty("rotationRelax").GetInt32());
        }

        // LOW-1 (T359 review): renamed from ThePoolSqlIsByteIdenticalToPreF152, which asserted the
        // OPPOSITE of what it said — see the ORCHESTRATOR RULING below for why "byte-identical" was
        // never literally achievable once this predicate landed.
        //
        // ORCHESTRATOR RULING (build loop, T359 dispatch): AC10's "byte-identical" wording assumed a
        // positional envelope shape; SegmentEnvelope.Rotation's F151.1 projection join
        // (`left join library.media_rotation rot on rot.media_id = m.id`) is UNCONDITIONAL BY DESIGN
        // (a cheap 1:1 PK left join, T356), so the pool SQL can never again be literally
        // byte-identical to pre-F152 once it lands. Ruled testable meaning: with no Rotation on the
        // envelope, (1) the WHERE clause carries NO play_count/last_aired_at predicate text — proven
        // directly against MediaRepository's own predicate builder, the exact fragment the query
        // embeds — and (2) the returned candidate id set matches the no-predicate baseline: a row
        // whose ledger would fail a real predicate (heavily played, long overdue) still surfaces.
        [Fact]
        public async Task TheWhereCarriesNoRotationPredicateAndThePoolMatchesTheBaseline()
        {
            Assert.Equal("", MediaRepository.RotationPredicateSql(null));

            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var heavilyPlayedId = await InsertReadyAsync(repo, "/rotation/no-predicate-baseline.flac");
            await SeedRotationAsync(db, heavilyPlayedId, playCount: 99, lastAiredAt: DateTimeOffset.UtcNow);

            var catalog = (IMediaCatalog)repo;
            var scope = new LibraryScope([1L]);

            var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                scope, [], artistSeparation: 0, UnconstrainedEnvelope, limit: 20, CancellationToken.None);

            Assert.Contains(pool, c => c.Media.MediaId == heavilyPlayedId.ToString());
        }
    }
}
