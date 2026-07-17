using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Loudness;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests;

/// <summary>
/// Restart-recovery (PRD §5.2): a row discovered before a restart — its id lost with the in-memory
/// queue — must be re-enqueued from the durable <c>discovered</c> state, not orphaned. Reproduces the
/// gap where a crash/redeploy mid-backfill would otherwise leave files permanently un-enriched and
/// invisible to playout.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class RecoveryTests(DatabaseFixture fixture)
{
    static EnrichmentService NewService(MediaRepository repo, Channel<long> queue)
    {
        var cueAnalyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
        return new(repo,
            new Enricher(
                new FfmpegLoudnessAnalyzer(),
                cueAnalyzer,
                new FakeEnergyAnalyzer(),
                new FakeBpmAnalyzer(),
                NullLogger<Enricher>.Instance),
            queue,
            new FakeOptionsMonitor<LibraryOptions>(new LibraryOptions()),
            NullLogger<EnrichmentService>.Instance,
            cueAnalyzer,
            Microsoft.Extensions.Options.Options.Create(new CueDetectionOptions()),
            new FakeEnergyAnalyzer(),
            new FakeBpmAnalyzer(),
            new FakeYearLookup(),
            new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()));
    }

    [Fact]
    public async Task DiscoveredRow_IsRequeued_AfterRestart()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        // Discovered just before a "restart"; the queue that held its id is gone. The orphan scenario.
        // (No real file needed: recovery only re-enqueues — it does not enrich here.)
        var orphan = await repo.InsertDiscoveredAsync(
            "/media/orphan.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

        // The "restart": a brand-new service with a brand-new, EMPTY channel.
        var queue = Channel.CreateUnbounded<long>();
        await NewService(repo, queue).RequeuePendingAsync(CancellationToken.None);

        Assert.Contains(orphan, Harness.DrainIds(queue));   // FAILS without the recovery step
    }

    [Fact]
    public async Task ReadyAndFailedRows_AreNotRequeued()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        var ready = await repo.InsertDiscoveredAsync("/media/ready.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(ready, Harness.ReadyResult(measurable: true), CancellationToken.None);

        var failed = await repo.InsertDiscoveredAsync("/media/failed.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.MarkFailedAsync(failed, CancellationToken.None);

        var pending = await repo.InsertDiscoveredAsync("/media/pending.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

        var queue = Channel.CreateUnbounded<long>();
        await NewService(repo, queue).RequeuePendingAsync(CancellationToken.None);

        var requeued = Harness.DrainIds(queue);
        Assert.Contains(pending, requeued);          // only pending work
        Assert.DoesNotContain(ready, requeued);      // ready is done
        Assert.DoesNotContain(failed, requeued);     // failed is terminal (see decision below)
    }
}
