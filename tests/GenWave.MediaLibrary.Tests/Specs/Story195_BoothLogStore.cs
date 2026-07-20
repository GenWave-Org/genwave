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
            var writer = new BoothLogWriter(channel.Writer, NullLogger<BoothLogWriter>.Instance);
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
            var writer = new BoothLogWriter(channel.Writer, NullLogger<BoothLogWriter>.Instance);

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
            await store.AppendAsync("track-started", "Started 'New Song' by New Artist", CancellationToken.None);

            // Then the expired rows are gone and only the new row remains — the table stays bounded.
            var rows = await AllRowsAsync(db);
            Assert.Single(rows);
            Assert.Equal("Started 'New Song' by New Artist", rows[0].Summary);
        }
    }
}
