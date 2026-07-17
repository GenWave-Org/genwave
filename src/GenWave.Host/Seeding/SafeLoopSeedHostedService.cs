namespace GenWave.Host.Seeding;

/// <summary>
/// Runs <see cref="SafeLoopSeeder.SeedAsync"/> once, without blocking host startup (SPEC F27.6,
/// STORY-080). <see cref="BackgroundService"/>'s default <c>StartAsync</c> fires
/// <see cref="ExecuteAsync"/> and returns immediately without awaiting it whenever the work suspends
/// on real I/O — exactly what a several-second render needs: <c>/health</c> comes up right away while
/// the seed runs concurrently (mirrors the fire-and-forget shape
/// <c>PlayoutFeederService.StartAsync</c> already uses one layer up in <c>Playout/</c>).
///
/// <see cref="SafeLoopSeeder.SeedAsync"/> itself never lets an exception escape — including from its
/// own marker-store check, the most common boot-time transient (the station DB not yet reachable) —
/// a WARN and <see cref="SafeLoopSeedOutcome.Failed"/> come back instead. The catch here is therefore
/// a genuine last-resort guard against a bug in that contract, not an expected-failure path this
/// hosted service relies on for normal degrade-and-retry behaviour (F27.6).
/// </summary>
sealed class SafeLoopSeedHostedService(
    SafeLoopSeeder seeder,
    ILogger<SafeLoopSeedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var outcome = await seeder.SeedAsync(stoppingToken);
            if (outcome == SafeLoopSeedOutcome.AlreadySeeded)
                logger.LogInformation("Boot seed: marker already present — nothing to do");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before the seed finished; no marker was written, so the next boot
            // retries from scratch.
        }
        catch (Exception ex)
        {
            // Should be unreachable — SeedAsync itself never throws for an expected failure, including
            // its own marker-store check. Kept as a last-resort guard so a bug in the seed pipeline can
            // never kill an otherwise-healthy host (F27.6).
            logger.LogWarning(ex,
                "Boot seed: unexpected failure outside the seed pipeline — host starting normally");
        }
    }
}
