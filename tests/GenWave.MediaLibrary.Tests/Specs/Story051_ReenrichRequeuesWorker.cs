// STORY-051 (defect fix, 2026-07-02) — state-resetting re-enrichment must reach the worker
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres).
//
// Defect found closing the L8 acceptance gate: `fields=loudness` / `fields=tags` re-enrichment
// sets state='discovered' but never enqueues the row — the discovered path is fed only by the
// scanner (disk deltas; the file is unchanged) and the startup-only recovery query, so the rows
// sit out of rotation until the next `api` restart. Cue/energy resets converge because their
// backfill predicates POLL every scan interval; loudness/tags had no equivalent.
//
// Fix under spec: ScheduleAsync / ScheduleBulkAsync enqueue the affected ids into the enrichment
// channel whenever the reset includes state='discovered'. Cue/energy-only resets enqueue nothing
// (the polling backfills own them). A full channel degrades gracefully: the UPDATE still commits
// and the startup recovery query remains the safety net.

using System.Threading.Channels;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureReenrichRequeuesWorker
{
    static readonly LibraryScope Scope = new([1L]);

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — single-row state-resetting reenrich reaches the worker queue
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSingleRowStateResettingReenrich(DatabaseFixture db)
    {
        async Task<(long id, Channel<long> queue, ReenrichResult result)> RunAsync(ReenrichFields fields)
        {
            await db.ResetAsync();
            var queue = Channel.CreateUnbounded<long>();
            var repo = Harness.Repo(db, queue);

            var id = await repo.InsertDiscoveredAsync("/media/requeue-single.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var result = await ((IAdminMediaReenrichment)repo).ScheduleAsync(
                id.ToString(), fields, Scope, CancellationToken.None);
            return (id, queue, result);
        }

        [Fact]
        public async Task LoudnessReenrichEnqueuesTheRowId()
        {
            var (id, queue, _) = await RunAsync(ReenrichFields.Loudness);
            Assert.Equal([id], Harness.DrainIds(queue));
        }

        [Fact]
        public async Task TagsReenrichEnqueuesTheRowId()
        {
            var (id, queue, _) = await RunAsync(ReenrichFields.Tags);
            Assert.Equal([id], Harness.DrainIds(queue));
        }

        [Fact]
        public async Task LoudnessAndTagsTogetherEnqueueTheRowIdOnce()
        {
            var (id, queue, _) = await RunAsync(ReenrichFields.Loudness | ReenrichFields.Tags);
            Assert.Equal([id], Harness.DrainIds(queue));
        }

        [Fact]
        public async Task LoudnessReenrichStillReturnsScheduled()
        {
            var (_, _, result) = await RunAsync(ReenrichFields.Loudness);
            Assert.Equal(ReenrichResult.Scheduled, result);
        }

        [Fact]
        public async Task LoudnessReenrichStillResetsStateToDiscovered()
        {
            var (id, _, _) = await RunAsync(ReenrichFields.Loudness);
            Assert.Equal("discovered", await Harness.StateOfAsync(db, id));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — bulk state-resetting reenrich reaches the worker queue
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBulkStateResettingReenrich(DatabaseFixture db)
    {
        async Task<(List<long> matching, long other, Channel<long> queue, int count)> RunAsync()
        {
            await db.ResetAsync();
            var queue = Channel.CreateUnbounded<long>();
            var repo = Harness.Repo(db, queue);

            var matching = new List<long>();
            for (var i = 0; i < 3; i++)
            {
                var id = await repo.InsertDiscoveredAsync($"/media/requeue-bulk-{i}.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "Requeue Artist"), CancellationToken.None);
                matching.Add(id);
            }

            var other = await repo.InsertDiscoveredAsync("/media/requeue-bulk-other.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(other, Harness.ReadyResultWith(artist: "Someone Else"), CancellationToken.None);

            var count = await ((IAdminMediaReenrichment)repo).ScheduleBulkAsync(
                new MediaQuery(State: null, Artist: "Requeue Artist", Genre: null, LibraryId: null, Q: null),
                ReenrichFields.Loudness, Scope, CancellationToken.None);
            return (matching, other, queue, count);
        }

        [Fact]
        public async Task EveryMatchedRowIdIsEnqueued()
        {
            var (matching, _, queue, _) = await RunAsync();
            Assert.Equal(matching.Order(), Harness.DrainIds(queue).Order());
        }

        [Fact]
        public async Task NonMatchingRowsAreNotEnqueued()
        {
            var (_, other, queue, _) = await RunAsync();
            Assert.DoesNotContain(other, Harness.DrainIds(queue));
        }

        [Fact]
        public async Task TheReturnedCountStillReflectsTheRowsReset()
        {
            var (_, _, _, count) = await RunAsync();
            Assert.Equal(3, count);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAD PATH — cue/energy-only resets stay with the polling backfills
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNonStateResettingReenrichDoesNotEnqueue(DatabaseFixture db)
    {
        async Task<Channel<long>> RunAsync(ReenrichFields fields)
        {
            await db.ResetAsync();
            var queue = Channel.CreateUnbounded<long>();
            var repo = Harness.Repo(db, queue);

            var id = await repo.InsertDiscoveredAsync("/media/requeue-nonstate.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await ((IAdminMediaReenrichment)repo).ScheduleAsync(id.ToString(), fields, Scope, CancellationToken.None);
            return queue;
        }

        [Fact]
        public async Task CueOnlyReenrichEnqueuesNothing()
        {
            var queue = await RunAsync(ReenrichFields.Cue);
            Assert.Empty(Harness.DrainIds(queue));
        }

        [Fact]
        public async Task EnergyOnlyReenrichEnqueuesNothing()
        {
            var queue = await RunAsync(ReenrichFields.Energy);
            Assert.Empty(Harness.DrainIds(queue));
        }

        [Fact]
        public async Task BulkCueOnlyReenrichEnqueuesNothing()
        {
            await db.ResetAsync();
            var queue = Channel.CreateUnbounded<long>();
            var repo = Harness.Repo(db, queue);

            var id = await repo.InsertDiscoveredAsync("/media/requeue-bulk-cue.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await ((IAdminMediaReenrichment)repo).ScheduleBulkAsync(
                new MediaQuery(State: null, Artist: null, Genre: null, LibraryId: null, Q: null),
                ReenrichFields.Cue, Scope, CancellationToken.None);

            Assert.Empty(Harness.DrainIds(queue));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAD PATH — full channel degrades gracefully (startup recovery is the net)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFullChannelDegradesGracefully(DatabaseFixture db)
    {
        async Task<(List<long> ids, int count)> RunAsync()
        {
            await db.ResetAsync();
            // Capacity 1 with DropWrite: the second TryWrite fails, mimicking a saturated queue
            // without blocking. The commit must still happen for every row.
            var queue = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
            var repo = Harness.Repo(db, queue);

            var ids = new List<long>();
            for (var i = 0; i < 2; i++)
            {
                var id = await repo.InsertDiscoveredAsync($"/media/requeue-full-{i}.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "Full Channel"), CancellationToken.None);
                ids.Add(id);
            }

            var count = await ((IAdminMediaReenrichment)repo).ScheduleBulkAsync(
                new MediaQuery(State: null, Artist: "Full Channel", Genre: null, LibraryId: null, Q: null),
                ReenrichFields.Loudness, Scope, CancellationToken.None);
            return (ids, count);
        }

        [Fact]
        public async Task TheResetStillCommitsForEveryMatchedRow()
        {
            var (ids, _) = await RunAsync();
            await using var conn = await db.DataSource.OpenConnectionAsync();
            var discovered = await conn.ExecuteScalarAsync<int>(
                "select count(*) from library.media where id = any(@ids) and state = 'discovered'",
                new { ids });
            Assert.Equal(2, discovered);
        }

        [Fact]
        public async Task TheReturnedCountIsUnaffectedByTheFullChannel()
        {
            var (_, count) = await RunAsync();
            Assert.Equal(2, count);
        }
    }
}
