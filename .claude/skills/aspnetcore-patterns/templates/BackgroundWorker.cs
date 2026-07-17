namespace GenWave.Example.Workers;

/// <summary>
/// 24/7 resilient background worker template. Encodes the house patterns:
/// yield before work (don't block host startup), scope-per-cycle for
/// scoped dependencies, heartbeat for health checks, recoverable faults
/// logged and retried, cancellation as the only way out.
/// Adapt: rename, fill RunCycleAsync, tune the retry delay.
/// Registration:
///   services.AddSingleton<WorkerHeartbeat>();
///   services.AddHostedService<ExampleWorker>();
/// </summary>
public sealed class ExampleWorker(
    IServiceScopeFactory scopeFactory,
    WorkerHeartbeat heartbeat,
    ILogger<ExampleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        logger.LogInformation("{Worker} started", nameof(ExampleWorker));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
                heartbeat.Beat();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cycle failed; retrying in {Delay}s", RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogInformation("{Worker} stopping", nameof(ExampleWorker));
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // One scope == one unit of work. Scoped services (DbContext etc.)
        // are resolved here, never in the constructor of this singleton.
        await using var scope = scopeFactory.CreateAsyncScope();

        // var db = scope.ServiceProvider.GetRequiredService<ExampleDbContext>();
        // ... do one cycle of work, honoring ct on every awaited call ...

        await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
}
