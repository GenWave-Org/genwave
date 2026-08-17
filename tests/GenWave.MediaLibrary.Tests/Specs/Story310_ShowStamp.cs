// STORY-310 — Show airings are countable (F121.1)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration, shared DatabaseFixture) — the
// F113.1 pattern exactly, and the SAME production pipeline Story215_BoothLogPersonaStamp.cs/
// Story304_AiredKindStamp.cs's own DriveThroughAsync drive: real StationEvents through the REAL
// BoothLogWriter/BoothLogDrainService into the real (test) database, because the write-side types
// (BoothLogWriter, BoothLogDrainService) are internal to GenWave.MediaLibrary,
// and a fake store would never prove the real INSERT column-list wiring honestly. `show_id` is
// deliberately read back with a raw query rather than through BoothLogRepository.ReadAsync/
// BoothLogEntry — F113.3's precedent (segment_kind) keeps the read path untouched this cycle too, so
// the column has no projection to assert against yet.
//
// No FK on booth_log.show_id (SPEC F121.1 — history outlives the entity), so these facts stamp an
// arbitrary show id with no need for a real station.show row to exist.

using System.Threading.Channels;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureShowStamp
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Scriptable <see cref="IActivePersonaAccessor"/> double, scoped to this file's own concern
    /// (<see cref="ActiveShowId"/> — not persona attribution, Story215_BoothLogPersonaStamp.cs's own
    /// territory): reports no active persona (the interface's own default). <see cref="ShowId"/> is
    /// settable — mirrors Story215's own <c>FakeActivePersonaAccessor</c> — so a scenario can flip the
    /// on-air show MID-TEST (the F121.1 "stamped at air time" claim is only provable if the answer
    /// can change AFTER <see cref="BoothLogWriter.Publish"/> already captured it).
    /// </summary>
    sealed class FakeActiveShowAccessor(long? showId) : IActivePersonaAccessor
    {
        public long? ShowId { get; set; } = showId;

        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);

        public long? ActiveShowId => ShowId;
    }

    static BoothLogRepository Store(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new BoothLogOptions()));

    /// <summary>
    /// Publishes every <paramref name="events"/> through the real <see cref="BoothLogWriter"/> — which
    /// captures <paramref name="activeShowId"/> SYNCHRONOUSLY at publish time (F121.1), off the SAME
    /// <see cref="IActivePersonaAccessor"/> dependency the persona stamp already reads — and drains
    /// each through the real <see cref="BoothLogDrainService.ProcessAsync"/>, the same production
    /// pipeline this file's sibling specs drive.
    /// </summary>
    static async Task DriveThroughAsync(DatabaseFixture db, long? activeShowId, params StationEvent[] events)
    {
        var channel = Channel.CreateBounded<BoothLogAppendRequest>(16);
        var writer = new BoothLogWriter(channel.Writer, new FakeActiveShowAccessor(activeShowId), NullLogger<BoothLogWriter>.Instance);
        var drain = new BoothLogDrainService(channel.Reader, Store(db), NullLogger<BoothLogDrainService>.Instance);

        foreach (var evt in events)
            writer.Publish(evt);

        for (var i = 0; i < events.Length; i++)
            await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);
    }

    /// <summary>The persisted `show_id` for every `track-started` row, newest first — a raw query
    /// rather than <see cref="BoothLogRepository.ReadAsync"/> because the column has no projection on
    /// <see cref="BoothLogEntry"/> yet (F113.3's precedent, carried to F121.1).</summary>
    static async Task<List<long?>> TrackStartedShowIdsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<long?>(
            """
            select show_id from station.booth_log
            where kind = 'track-started'
            order by occurred_at desc, id desc
            """);
        return rows.ToList();
    }

    static TrackAired AKindedAiring() => new(
        "tts:abc123", "GenWave", "GenWave", -2.0, DateTimeOffset.UtcNow, 4_000,
        SegmentKind: SegmentKind.StationId);

    static TrackAired AMusicAiring() => new("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000);

    // ---------------------------------------------------------------------
    // HAPPY PATH — rows during a show carry show_id (F121.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRowsDuringAShow(DatabaseFixture db)
    {
        [Fact]
        public async Task KindedRowsCarryTheShowId()
        {
            // Given a show on the air...
            await db.ResetBoothLogAsync();
            const long showId = 501;

            // When a kinded (tts) airing writes its track-started row — SegmentKind.StationId, the
            // demo-hour gate's own F121.2(b) evidence...
            await DriveThroughAsync(db, showId, AKindedAiring());

            // Then show_id carries the snapshot's show.
            var showIds = await TrackStartedShowIdsAsync(db);
            Assert.Equal([showId], showIds);
        }

        [Fact]
        public async Task MusicRowsCarryItToo()
        {
            // Given a show on the air, and the SAME BoothLogWriter instance/chokepoint a kinded
            // airing stamps through above — not a second writer, not a second accessor...
            await db.ResetBoothLogAsync();
            const long showId = 777;
            var channel = Channel.CreateBounded<BoothLogAppendRequest>(16);
            var writer = new BoothLogWriter(channel.Writer, new FakeActiveShowAccessor(showId), NullLogger<BoothLogWriter>.Instance);
            var drain = new BoothLogDrainService(channel.Reader, Store(db), NullLogger<BoothLogDrainService>.Instance);

            // When a kinded airing and a plain music airing both flow through it, in order...
            writer.Publish(AKindedAiring());
            writer.Publish(AMusicAiring());
            await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);
            await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);

            // Then show_id is stamped from the same chokepoint on BOTH rows — verifying ONE stamp
            // point covers music and kinded alike (the /design TODO made a fact, PLAN T242).
            var showIds = await TrackStartedShowIdsAsync(db);
            Assert.Equal([showId, showId], showIds);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the stamp reflects AIR time, not DRAIN time, across a channel backlog (F121.1)
    //
    // Mirrors Story215_BoothLogPersonaStamp.cs's own ScenarioPersonaSwitchesDuringBacklog exactly —
    // the T60 review finding this epic's own PLAN task calls out: resolving the on-air show at DRAIN
    // time (rather than capturing it synchronously at PUBLISH time) would mis-stamp a row already
    // queued behind a bounded-channel backlog once a show change lands before the drain catches up.
    // BoothLogWriter.Publish must have already captured the answer before this test ever flips the
    // active show.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShowSwitchesDuringBacklog(DatabaseFixture db)
    {
        [Fact]
        public async Task QueuedTrackStartStaysStampedWithTheAirTimeShowDespiteALaterSwitch()
        {
            // Given show A on the air, and a track-start event published through the real writer —
            // captured synchronously, at air time, while A is still on air...
            await db.ResetBoothLogAsync();
            const long showAId = 111;
            const long showBId = 222;
            var accessor = new FakeActiveShowAccessor(showAId);

            var channel = Channel.CreateBounded<BoothLogAppendRequest>(16);
            var writer = new BoothLogWriter(channel.Writer, accessor, NullLogger<BoothLogWriter>.Instance);
            var drain = new BoothLogDrainService(channel.Reader, Store(db), NullLogger<BoothLogDrainService>.Instance);

            writer.Publish(AMusicAiring());

            // When the on-air show switches to B BEFORE the drain ever processes the entry sitting
            // in the queue — the exact bounded-queue-backlog window the finding described...
            accessor.ShowId = showBId;
            await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);

            // Then the persisted row is stamped with A — the show on air when the track STARTED,
            // never B, which only became active after the row had already queued.
            var showIds = await TrackStartedShowIdsAsync(db);
            Assert.Equal([showAId], showIds);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a showless airing stays unstamped (F121.1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioShowlessRows(DatabaseFixture db)
    {
        [Fact]
        public async Task NoShowMeansNullStamp()
        {
            // Given no show on the air (a grid gap, or an unnamed block)...
            await db.ResetBoothLogAsync();

            // When rows are written — kinded and music alike...
            await DriveThroughAsync(db, activeShowId: null, AKindedAiring(), AMusicAiring());

            // Then show_id stays NULL on every row — pre-F121 and showless rows are indistinguishable.
            var showIds = await TrackStartedShowIdsAsync(db);
            Assert.Equal([null, null], showIds);
        }
    }
}
