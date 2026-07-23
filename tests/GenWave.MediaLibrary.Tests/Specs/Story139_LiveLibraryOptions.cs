// STORY-139 — Every tunable in the console (Epic V / SPEC F44.2, closes gitea-#197) — scan/enrichment
// live-read half. The allowlist half lives in Host.Tests/Specs/Story139_SettingsSurfaceCompletion.cs.
//
// BDD specification — xUnit. Implemented V8 (2026-07-14): ScanService retunes its PeriodicTimer.Period
// from IOptionsMonitor<LibraryOptions>.CurrentValue before every tick; EnrichmentService reconciles its
// worker pool toward the live Library:EnrichmentConcurrency value on the same cadence as its backfill
// loop — growing spawns workers immediately, shrinking is cooperative (a worker retires between items).

using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Scan;
using GenWave.MediaLibrary.Tests.Fakes;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

/// <summary>
/// Loudness analyzer double that blocks inside <see cref="AnalyzeAsync"/> until
/// <paramref name="gate"/> completes — holds a worker "in flight" so a test can mutate
/// concurrency mid-enrichment and prove the running item is never disrupted.
/// </summary>
file sealed class GatedLoudnessAnalyzer(Task gate) : GenWave.Core.Abstractions.ILoudnessAnalyzer
{
    public int CallCount { get; private set; }

    public async Task<GenWave.Core.Domain.Loudness> AnalyzeAsync(string path, CancellationToken ct)
    {
        CallCount++;
        await gate.WaitAsync(ct);
        return new GenWave.Core.Domain.Loudness(-16.0, -1.0, true);
    }
}

public static class FeatureLiveLibraryOptions
{
    /// <summary>
    /// A <see cref="MediaRepository"/> wired to a lazily-connecting <see cref="NpgsqlDataSource"/> —
    /// never opened by these two facts, which only exercise the live-read plumbing above the repo,
    /// not an actual query. Keeps <see cref="ScenarioScanAndEnrichmentReadOptionsPerTick"/> DB-free
    /// (no [Collection(DatabaseCollection.Name)], so it runs in the filtered CI wall too).
    /// </summary>
    static MediaRepository DisconnectedRepo(Channel<long> enrichQueue) =>
        new(
            new NpgsqlDataSourceBuilder("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused").Build(),
            NullLogger<MediaRepository>.Instance,
            enrichQueue,
            new Fakes.FakeSafeScopeProvider());

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

    public sealed class ScenarioScanAndEnrichmentReadOptionsPerTick
    {
        [Fact]
        public void AChangedScanIntervalAppliesOnTheNextTick()
        {
            var queue = Channel.CreateUnbounded<long>();
            var options = new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { ScanIntervalSeconds = 60 });
            var scan = new ScanService(DisconnectedRepo(queue), queue, options, NullLogger<ScanService>.Instance,
                new FakeOptionsMonitor<ScanOptions>(new ScanOptions()));

            Assert.Equal(TimeSpan.FromSeconds(60), scan.CurrentScanInterval);

            // The live edit: no re-construction, no restart — same service instance, new interval.
            // ExecuteAsync's loop re-reads this exact property (PeriodicTimer.Period = CurrentScanInterval)
            // before every WaitForNextTickAsync, so this is the production read path, not a stand-in.
            options.CurrentValue = new LibraryOptions { ScanIntervalSeconds = 5 };

            Assert.Equal(TimeSpan.FromSeconds(5), scan.CurrentScanInterval);
        }

        [Fact]
        public void AChangedEnrichmentConcurrencyAppliesOnTheNextTick()
        {
            var queue = Channel.CreateUnbounded<long>();
            var enricher = new Enricher(
                new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(),
                NullLogger<Enricher>.Instance);
            var options = new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { EnrichmentConcurrency = 2 });
            var svc = new EnrichmentService(
                DisconnectedRepo(queue), enricher, queue, options, NullLogger<EnrichmentService>.Instance,
                new FakeCueAnalyzer(),
                Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
                new FakeEnergyAnalyzer(),
                new FakeBpmAnalyzer(),
                new FakeYearLookup(),
                new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));

            svc.ReconcileWorkerPool(CancellationToken.None);
            Assert.Equal(2, svc.ActiveWorkerCount);

            // The live edit: no re-construction — same service instance, new desired headcount.
            // Growing is immediate: ReconcileWorkerPool spawns the extra workers straight away.
            options.CurrentValue = new LibraryOptions { EnrichmentConcurrency = 5 };
            svc.ReconcileWorkerPool(CancellationToken.None);

            Assert.Equal(5, svc.ActiveWorkerCount);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    public sealed class ScenarioAMidTickChangeNeverDisruptsInFlightWork(DatabaseFixture db)
    {
        [Fact]
        [Trait("Category", "Integration")]
        public async Task AnInFlightTickCompletesUnderItsStartingValues()
        {
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "inflight.flac");
                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(
                    path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                var gate = new TaskCompletionSource();
                var loud = new GatedLoudnessAnalyzer(gate.Task);
                var enricher = new Enricher(loud, new FakeCueAnalyzer(), new FakeEnergyAnalyzer(), new FakeBpmAnalyzer(), NullLogger<Enricher>.Instance);
                var queue = Channel.CreateUnbounded<long>();
                var options = new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions { EnrichmentConcurrency = 2 });
                var svc = new EnrichmentService(
                    repo, enricher, queue, options, NullLogger<EnrichmentService>.Instance,
                    new FakeCueAnalyzer(),
                    Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
                    new FakeEnergyAnalyzer(),
                    new FakeBpmAnalyzer(),
                    new FakeYearLookup(),
                    new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));

                svc.ReconcileWorkerPool(CancellationToken.None);
                await queue.Writer.WriteAsync(id, CancellationToken.None);

                // Wait for a worker to pick the item up and block inside the gated analyzer —
                // the item is now genuinely "in flight".
                await WaitUntilAsync(() => loud.CallCount > 0, TimeSpan.FromSeconds(5));

                // Mid-flight config change: shrink concurrency to 1. This must NOT disturb the
                // item already being enriched (SPEC F44.2's "next tick" promise).
                options.CurrentValue = new LibraryOptions { EnrichmentConcurrency = 1 };
                svc.ReconcileWorkerPool(CancellationToken.None);

                gate.SetResult();

                await WaitUntilAsync(
                    () => Harness.StateOfAsync(db, id).GetAwaiter().GetResult() == "ready",
                    TimeSpan.FromSeconds(5));

                // The in-flight row completed successfully under the value it started with —
                // never aborted or corrupted by the concurrent shrink.
                Assert.Equal("ready", await Harness.StateOfAsync(db, id));
                Assert.Equal(1, loud.CallCount);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
