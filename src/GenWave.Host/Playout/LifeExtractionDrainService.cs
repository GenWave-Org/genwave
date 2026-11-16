using GenWave.Memory;

namespace GenWave.Host.Playout;

/// <summary>
/// Runs <see cref="LifeExtractionService.RunAsync"/> for the life of the host (SPEC F218.17, STORY-537; PLAN T704).
/// A fault is logged and that copy lost, never thrown into the host. Queued copies are not flushed at shutdown:
/// each costs a model call, and a claim missed on the way down is not worth holding the host.
/// </summary>
sealed class LifeExtractionDrainService(
    LifeExtractionService service,
    ILogger<LifeExtractionDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await service.RunAsync(
                ex => logger.LogWarning(ex, "DJ life extraction failed — playout unaffected"),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }
}
