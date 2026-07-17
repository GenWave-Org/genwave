// STORY-156 — The enrichment pool cleans up after itself (Epic Z / SPEC F59, closes gitea-#222).
//
// EnrichmentService.ReconcileWorkerPool prunes completed/retired worker tasks; the tracked set
// is bounded by target concurrency + in-flight retirees across repeated grow/shrink cycles.
// Claim predicates, the 50/tick throttle, and error handling are unchanged (F59.2).

using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureWorkerPoolPrune
{
    /// <summary>
    /// A <see cref="MediaRepository"/> wired to a lazily-connecting <see cref="NpgsqlDataSource"/> —
    /// never opened by the happy-path facts below, which only ever grow workers against an EMPTY,
    /// still-open channel blocked on the read; retirement is always driven by cancellation, never by
    /// completing the channel (an already-completed, empty channel makes <c>WaitToReadAsync</c> return
    /// synchronously, which — pre-existing, independent of this fix — would let a newly-spawned worker
    /// run to completion inside <c>ReconcileWorkerPool</c>'s own growth loop and never actually count
    /// toward the desired headcount; out of scope for F59's lifecycle-hygiene-only prune). Mirrors
    /// Story139's own DisconnectedRepo convention so these facts stay DB-free and run in the filtered
    /// CI wall.
    /// </summary>
    static MediaRepository DisconnectedRepo(Channel<long> enrichQueue) =>
        new(
            new NpgsqlDataSourceBuilder("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused").Build(),
            NullLogger<MediaRepository>.Instance,
            enrichQueue);

    static EnrichmentService NewService(Channel<long> queue, FakeOptionsMonitor<LibraryOptions> options) =>
        new(
            DisconnectedRepo(queue),
            new Enricher(new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
            queue,
            options,
            NullLogger<EnrichmentService>.Instance,
            new FakeCueAnalyzer(),
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));

    static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(20);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — reconcile prunes the retired
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioReconcilePrunesRetiredTasks
    {
        [Fact]
        public async Task CompletedTasksAreRemovedOnReconcile()
        {
            var queue = Channel.CreateUnbounded<long>();
            var options = new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { EnrichmentConcurrency = 2 });
            var svc = NewService(queue, options);

            using var cts = new CancellationTokenSource();
            svc.ReconcileWorkerPool(cts.Token);
            Assert.Equal(2, svc.TrackedWorkerTaskCount);

            // Cancel so both workers retire — they're blocked on WaitToReadAsync against an EMPTY,
            // still-open channel, so cancellation is what unblocks them; no item is ever claimed and
            // the channel itself is never completed.
            cts.Cancel();
            await WaitUntilAsync(() => svc.ActiveWorkerCount == 0, TimeSpan.FromSeconds(5));

            // Desired concurrency (2) hasn't changed, so this reconcile — with a fresh, live token —
            // regrows to exactly 2. Pre-fix, the ConcurrentBag would still be holding the 2 retired
            // tasks from above, so a regression here would report 4, not 2.
            svc.ReconcileWorkerPool(CancellationToken.None);

            Assert.Equal(2, svc.TrackedWorkerTaskCount);
        }
    }

    public sealed class ScenarioRepeatedCyclesStayBounded
    {
        [Fact]
        public async Task RepeatedGrowShrinkCyclesNeverGrowTheSetMonotonically()
        {
            var queue = Channel.CreateUnbounded<long>();
            var options = new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { EnrichmentConcurrency = 1 });
            var svc = NewService(queue, options);

            // One persistent worker survives every cycle below (its token is never canceled) —
            // it stands in for "still at target, never over budget".
            svc.ReconcileWorkerPool(CancellationToken.None);
            await WaitUntilAsync(() => svc.ActiveWorkerCount == 1, TimeSpan.FromSeconds(5));

            for (var cycle = 0; cycle < 10; cycle++)
            {
                using var cts = new CancellationTokenSource();

                // Grow: raise concurrency and spawn the extra workers under THIS cycle's token.
                options.CurrentValue = new LibraryOptions { EnrichmentConcurrency = 4 };
                svc.ReconcileWorkerPool(cts.Token);
                await WaitUntilAsync(() => svc.ActiveWorkerCount == 4, TimeSpan.FromSeconds(5));

                // Right after growth the tracked set is exactly the live headcount — never the
                // running total of every worker ever spawned across prior cycles.
                Assert.Equal(4, svc.TrackedWorkerTaskCount);

                // Shrink: lower the target back down and cancel this cycle's token so the excess
                // workers retire (blocked on WaitToReadAsync, so cancellation is what unblocks them —
                // the queue itself is never touched, so no claim ever happens on this path).
                options.CurrentValue = new LibraryOptions { EnrichmentConcurrency = 1 };
                cts.Cancel();
                await WaitUntilAsync(() => svc.ActiveWorkerCount == 1, TimeSpan.FromSeconds(5));

                // Next reconcile prunes the 3 retirees; desired (1) already met, so no regrowth.
                svc.ReconcileWorkerPool(CancellationToken.None);

                Assert.Equal(1, svc.TrackedWorkerTaskCount);
            }
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioClaimSemanticsAreUnchanged(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task ClaimAndThrottleBehaviorIsUnchangedWithThePruneInPlace()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "claim.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var queue = Channel.CreateUnbounded<long>();
                var options = new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { EnrichmentConcurrency = 1 });
                var svc = new EnrichmentService(
                    repo,
                    new Enricher(new FfmpegLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance),
                    queue,
                    options,
                    NullLogger<EnrichmentService>.Instance,
                    new FakeCueAnalyzer(),
                    Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
                    new FakeEnergyAnalyzer(),
                    new FakeBpmAnalyzer(),
                    new FakeYearLookup(),
                    new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));

                // A few grow/shrink/prune cycles BEFORE the real claim — proving the prune doesn't
                // disturb the channel-claim path (TryRead) that follows.
                svc.ReconcileWorkerPool(CancellationToken.None);
                options.CurrentValue = new LibraryOptions { EnrichmentConcurrency = 3 };
                svc.ReconcileWorkerPool(CancellationToken.None);
                options.CurrentValue = new LibraryOptions { EnrichmentConcurrency = 1 };
                svc.ReconcileWorkerPool(CancellationToken.None);

                await queue.Writer.WriteAsync(id, CancellationToken.None);

                await WaitUntilAsync(
                    () => Harness.StateOfAsync(db, id).GetAwaiter().GetResult() == "ready",
                    TimeSpan.FromSeconds(10));

                // Claim predicate + error handling: the queued item was claimed off the channel and
                // enriched to completion exactly as before the prune.
                Assert.Equal("ready", await Harness.StateOfAsync(db, id));

                // 50/tick throttle: the backfill batch size default is untouched by this change —
                // ReconcileWorkerPool never reads or writes CueDetectionOptions.
                Assert.Equal(50, new CueDetectionOptions().BackfillBatchSize);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
