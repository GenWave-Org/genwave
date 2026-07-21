using GenWave.Host.Options;
using GenWave.Tts;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Health;

/// <summary>
/// The BackgroundService shell around <see cref="DependencyHealthProber"/> (SPEC F70.2,
/// STORY-187). Its only job is translating the validated <see cref="DependencyHealthOptions"/>
/// cadence into the prober's <see cref="DependencyHealthProber.RunAsync"/> call and swallowing
/// the expected shutdown cancellation — all the cadence/timeout/never-throws logic lives in the
/// prober itself, unit-tested directly in GenWave.Tts.Tests (mirrors
/// <c>PlayoutFeederService</c>'s split from <c>PlayoutFeeder</c>: a thin timer/try-catch shell
/// around a pure, independently-tested cycle).
/// </summary>
sealed class DependencyHealthProbeService(
    DependencyHealthProber prober,
    IOptions<DependencyHealthOptions> options,
    ILogger<DependencyHealthProbeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = options.Value;
        logger.LogInformation(
            "Dependency health probes started: every {IntervalSeconds}s, {TimeoutSeconds}s per-probe timeout",
            cfg.ProbeIntervalSeconds, cfg.ProbeTimeoutSeconds);

        try
        {
            await prober.RunAsync(
                TimeSpan.FromSeconds(cfg.ProbeIntervalSeconds),
                TimeSpan.FromSeconds(cfg.ProbeTimeoutSeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Dependency health probes stopped");
    }
}
