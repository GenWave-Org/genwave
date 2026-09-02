using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenWave.Ads;

/// <summary>
/// Runs <see cref="AdsLibrarySeeder.SeedAsync"/> once, without blocking host startup (SPEC F159.1,
/// PLAN T396 — the <c>SafeLoopSeedHostedService</c> shape). <see cref="BackgroundService"/>'s default
/// <c>StartAsync</c> fires <see cref="ExecuteAsync"/> and returns immediately without awaiting it
/// whenever the work suspends on real I/O, so <c>/health</c> comes up right away while the seed runs
/// concurrently.
///
/// <see cref="AdsLibrarySeeder.SeedAsync"/> itself never lets an exception escape (including from its
/// own repository calls) — a WARN and <see cref="AdsLibrarySeedOutcome.Failed"/> come back instead.
/// The catch here is therefore a genuine last-resort guard against a bug in that contract, not an
/// expected-failure path this hosted service relies on for normal degrade-and-retry behaviour.
/// </summary>
sealed class AdsLibrarySeedHostedService(
    AdsLibrarySeeder seeder,
    ILogger<AdsLibrarySeedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // AdsLibrarySeeder.SeedAsync itself already logs one INFO line for every outcome
            // (including AlreadySeeded, with the library name + id) — nothing duplicated here.
            await seeder.SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the seed finished; the next boot retries from scratch (no
            // marker was written — a bare create-if-absent has nothing partial to leave behind).
        }
        catch (Exception ex)
        {
            // Should be unreachable — SeedAsync itself never throws for an expected failure. Kept as
            // a last-resort guard so a bug in the seed pipeline can never take down an otherwise-
            // healthy host.
            logger.LogWarning(ex,
                "Boot seed: unexpected failure outside the ads library seed pipeline — host starting normally");
        }
    }
}
