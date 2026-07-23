// STORY-195 — Booth log (DB-backed half)
//
// BDD specification — xUnit (SPEC F72.1, F72.3). Postgres-backed (Category=Integration, shared
// DatabaseFixture) — the narrative-row content and the retention eviction are both real-DB behavior
// (a real INSERT through the real IStationEventSink consumer, a DELETE against now() inside the
// AppendAsync transaction) a fake store would never exercise honestly. The endpoint paging +
// admin-only + not-public facts live in GenWave.Host.Tests/Specs/Story195_BoothLog.cs instead, behind
// a fake IBoothLogReader — Host.Tests has no station Postgres by convention (see that file's header).

using System.Threading.Channels;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBoothLogStore
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Dapper-mapped projection for this file's own direct-SQL assertions (not the
    /// production <see cref="GenWave.Core.Domain.BoothLogEntry"/> shape — no id needed here).</summary>
    sealed record BoothLogRow(DateTime OccurredAt, string Kind, string Summary);

    /// <summary>
    /// Fixed "no active persona" answer (STORY-215) — this file's own scenarios pin narrative
    /// content and retention, never persona attribution, so every writer here publishes persona-less
    /// (its <see cref="IActivePersonaAccessor.ActivePersonaId"/> falls back to the interface's own
    /// default — <see langword="null"/>). The stamping behavior itself is covered by
    /// Story215_BoothLogPersonaStamp.cs.
    /// </summary>
    sealed class NoActivePersonaAccessor : IActivePersonaAccessor
    {
        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult<Persona?>(null);
    }

    // Fully-qualified (rather than `using Microsoft.Extensions.Options;`) — see Story194's own
    // remarks: this file's namespace nests under GenWave.MediaLibrary, which already has a sibling
    // `Options` namespace that shadows the `Microsoft.Extensions.Options.Options` static class.
    static BoothLogRepository Store(DatabaseFixture db, int retentionDays = 14) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new BoothLogOptions { RetentionDays = retentionDays }));

    static async Task InsertRowAsync(DatabaseFixture db, string kind, string summary, TimeSpan occurredAgo)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            insert into station.booth_log (occurred_at, kind, summary)
            values (now() - make_interval(secs => @occurredAgoSeconds), @kind, @summary)
            """,
            new { occurredAgoSeconds = occurredAgo.TotalSeconds, kind, summary });
    }

    static async Task<List<BoothLogRow>> AllRowsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<BoothLogRow>(
            "select occurred_at, kind, summary from station.booth_log order by occurred_at desc, id desc");
        return rows.ToList();
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — narrative rows land through the real wire (F72.1, AC1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNarrativeRows(DatabaseFixture db)
    {
        [Fact]
        public async Task Station_events_land_as_narrative_rows()
        {
            // Given a track start, a patter air, and a mode change occurring — driven through the
            // REAL production pipeline: BoothLogWriter (the IStationEventSink consumer) enqueues,
            // BoothLogDrainService's real per-item work persists via the real BoothLogRepository...
            await db.ResetBoothLogAsync();
            var channel = Channel.CreateBounded<BoothLogEntryRequest>(16);
            var writer = new BoothLogWriter(channel.Writer, new NoActivePersonaAccessor(), NullLogger<BoothLogWriter>.Instance);
            var store = Store(db);
            var drain = new BoothLogDrainService(channel.Reader, store, NullLogger<BoothLogDrainService>.Instance);

            writer.Publish(new TrackAired("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000));
            writer.Publish(new SegmentGenerated("tts:abc123", "LeadIn", "af_heart"));
            writer.Publish(new DegradationModeChanged("Normal", "Soft", "3 consecutive LLM failures (threshold 3)"));

            for (var i = 0; i < 3; i++)
                await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);

            // When the booth_log table is read...
            var rows = await AllRowsAsync(db);

            // Then each event landed as a narrative row with occurred_at, kind, and an
            // operator-readable (not JSON) summary.
            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r.Kind == "track-started" && r.Summary == "Started 'Night Drive' by The Waveforms");
            Assert.Contains(rows, r => r.Kind == "patter-aired" && r.Summary == "Patter aired (LeadIn, voice: af_heart)");
            Assert.Contains(rows, r => r.Kind == "mode-changed"
                && r.Summary == "LLM degradation: Normal → Soft (3 consecutive LLM failures (threshold 3))");
            Assert.All(rows, r => Assert.True(r.OccurredAt > DateTime.UtcNow.AddMinutes(-1)));
        }

        [Fact]
        public async Task Unrelated_events_are_not_recorded()
        {
            // Given an event kind the booth log has no narrative for (a library mutation)...
            await db.ResetBoothLogAsync();
            var channel = Channel.CreateBounded<BoothLogEntryRequest>(16);
            var writer = new BoothLogWriter(channel.Writer, new NoActivePersonaAccessor(), NullLogger<BoothLogWriter>.Instance);

            // When it is published through the real consumer...
            writer.Publish(new LibraryMutated("created", 1));

            // Then nothing was queued for the drain loop to persist.
            Assert.False(channel.Reader.TryRead(out _));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — retention (F72.3, AC3)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRetention(DatabaseFixture db)
    {
        [Fact]
        public async Task Insert_evicts_rows_older_than_the_retention_window()
        {
            // Given rows older than a 1-day retention window, plus one recent row...
            await db.ResetBoothLogAsync();
            await InsertRowAsync(db, "track-started", "Started 'Old Song' by Old Artist", TimeSpan.FromDays(3));
            await InsertRowAsync(db, "track-started", "Started 'Ancient Song' by Ancient Artist", TimeSpan.FromDays(10));
            var store = Store(db, retentionDays: 1);

            // When a new row is inserted...
            await store.AppendAsync("track-started", "Started 'New Song' by New Artist", personaId: null, artist: null, pick: null, mediaId: null, ct: CancellationToken.None);

            // Then the expired rows are gone and only the new row remains — the table stays bounded.
            var rows = await AllRowsAsync(db);
            Assert.Single(rows);
            Assert.Equal("Started 'New Song' by New Artist", rows[0].Summary);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — keyset paging against the REAL database (F72.2)
    //
    // The null-cursor first page and the cursor-continuation page are two different SQL statements
    // in BoothLogRepository.ReadAsync (see its own remarks): only a real Postgres round-trip proves
    // BOTH resolve, which is exactly the coverage gap (42P08, "could not determine data type of
    // parameter") that let the null-cursor shape ship broken behind the Host fake reader.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReadPaging(DatabaseFixture db)
    {
        /// <summary>
        /// Five rows, oldest to newest by 10-second steps — wide enough apart that no two
        /// <c>occurred_at</c> values can collide, so newest-first order is unambiguous.
        /// </summary>
        static async Task<BoothLogRepository> SeedFiveRowsAsync(DatabaseFixture db)
        {
            await db.ResetBoothLogAsync();
            await InsertRowAsync(db, "track-started", "Started 'First Song' by Artist A", TimeSpan.FromSeconds(50));
            await InsertRowAsync(db, "track-started", "Started 'Second Song' by Artist B", TimeSpan.FromSeconds(40));
            await InsertRowAsync(db, "track-started", "Started 'Third Song' by Artist C", TimeSpan.FromSeconds(30));
            await InsertRowAsync(db, "track-started", "Started 'Fourth Song' by Artist D", TimeSpan.FromSeconds(20));
            await InsertRowAsync(db, "track-started", "Started 'Fifth Song' by Artist E", TimeSpan.FromSeconds(10));
            return Store(db);
        }

        [Fact]
        public async Task First_page_with_no_cursor_returns_newest_first_with_next_before()
        {
            // Given five rows and no cursor (the shape that 500'd: a null @BeforeOccurredAt /
            // @BeforeId against real Postgres)...
            var store = await SeedFiveRowsAsync(db);

            // When the first page is read against the REAL repository...
            var page = await store.ReadAsync(before: null, take: 2, CancellationToken.None);

            // Then it succeeds, newest-first, with a cursor to continue (more rows exist).
            Assert.Equal(["Started 'Fifth Song' by Artist E", "Started 'Fourth Song' by Artist D"],
                page.Entries.Select(e => e.Summary));
            Assert.NotNull(page.NextBefore);
        }

        [Fact]
        public async Task Cursor_page_continues_without_duplicates_or_gaps()
        {
            // Given five rows, read as a first page of two...
            var store = await SeedFiveRowsAsync(db);
            var page1 = await store.ReadAsync(before: null, take: 2, CancellationToken.None);

            // When the next page is read using the first page's cursor...
            var page2 = await store.ReadAsync(page1.NextBefore, take: 2, CancellationToken.None);

            // Then it continues strictly after page 1 — no row repeated, none skipped.
            Assert.Equal(["Started 'Third Song' by Artist C", "Started 'Second Song' by Artist B"],
                page2.Entries.Select(e => e.Summary));
            Assert.Empty(page1.Entries.Select(e => e.Id).Intersect(page2.Entries.Select(e => e.Id)));
        }

        [Fact]
        public async Task Final_page_has_no_next_before()
        {
            // Given five rows, paged two at a time through pages 1 and 2...
            var store = await SeedFiveRowsAsync(db);
            var page1 = await store.ReadAsync(before: null, take: 2, CancellationToken.None);
            var page2 = await store.ReadAsync(page1.NextBefore, take: 2, CancellationToken.None);

            // When the final page is read...
            var page3 = await store.ReadAsync(page2.NextBefore, take: 2, CancellationToken.None);

            // Then it holds the one remaining (oldest) row and signals no further pages.
            Assert.Equal(["Started 'First Song' by Artist A"], page3.Entries.Select(e => e.Summary));
            Assert.Null(page3.NextBefore);
        }
    }
}
