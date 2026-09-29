using GenWave.Host.Engine;
using GenWave.Host.Options;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Health;

/// <summary>
/// The BackgroundService shell around <see cref="EngineSettingsCheck"/> (SPEC F213.5, STORY-488,
/// PLAN T602). Rides the SAME <see cref="DependencyHealthOptions"/> cadence as
/// <see cref="DependencyHealthProbeService"/> — the spec's own "existing dependency-health probe
/// cadence (30s)" — rather than a cadence of its own, since an engine-settings drift is exactly as
/// urgent to notice as a dependency outage and there is no reason to add a second interval knob for
/// it. All cadence/logging logic lives in <see cref="EngineSettingsCheck"/> itself (its own
/// <see cref="EngineSettingsCheck.RunAsync"/>), unit-tested directly; this shell's only job is
/// translating the validated cadence into that call and swallowing the expected shutdown
/// cancellation — the SAME split <see cref="DependencyHealthProbeService"/> makes from
/// <see cref="GenWave.Tts.DependencyHealthProber"/> one seam over.
/// </summary>
sealed class EngineSettingsCheckService(
    EngineSettingsCheck check,
    IOptionsMonitor<DependencyHealthOptions> options,
    ILogger<EngineSettingsCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Engine settings check started");

        try
        {
            await check.RunAsync(CurrentInterval, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Engine settings check stopped");
    }

    /// <summary>
    /// The probe interval as of right now — read fresh every cycle (not frozen at boot) so a live
    /// <c>DependencyHealth:ProbeIntervalSeconds</c> edit governs this loop too, the same live-cadence
    /// shape <see cref="DependencyHealthProbeService"/> already follows. The floor mirrors that
    /// class's own: <c>DependencyHealthOptions</c>' <c>[Range(1, ...)]</c> already rejects an
    /// out-of-range live edit, so this only guards a hand-edited appsettings that bypasses it.
    /// </summary>
    TimeSpan CurrentInterval() =>
        TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.ProbeIntervalSeconds));
}
