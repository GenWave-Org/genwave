using GenWave.Memory;

namespace GenWave.Host.Playout;

/// <summary>
/// Runs <see cref="EpisodeAccrualSink.RunAsync"/> for the life of the host (SPEC F218.15, STORY-536; PLAN T703).
/// A store fault is logged and the write lost, never thrown into the host; on shutdown the sink is completed
/// so what is already queued is written before the loop ends.
/// </summary>
sealed class EpisodeAccrualDrainService(
    EpisodeAccrualSink sink,
    ILogger<EpisodeAccrualDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await sink.RunAsync(
                ex => logger.LogWarning(ex, "DJ memory episode write failed — playout unaffected"),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown: StopAsync completes the sink.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Complete first and let the loop drain what is queued BEFORE the stopping token is cancelled
        // (base.StopAsync cancels it), bounded by the host's shutdown timeout. When that timeout fires the wait
        // is abandoned, not thrown: the stopping token must still be cancelled so a write against a down store ends.
        sink.Complete();
        try
        {
            if (ExecuteTask is { } running)
                await running.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
