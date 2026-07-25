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
    IOptionsMonitor<DependencyHealthOptions> options,
    ILogger<DependencyHealthProbeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startup = CurrentCadence();
        logger.LogInformation(
            "Dependency health probes started: every {IntervalSeconds}s, {TimeoutSeconds}s per-probe "
            + "timeout, {UnhealthyThreshold} consecutive failures to flip a verdict",
            startup.Interval.TotalSeconds, startup.PerProbeTimeout.TotalSeconds,
            startup.UnhealthyThreshold);

        try
        {
            await prober.RunAsync(CurrentCadence, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Dependency health probes stopped");
    }

    /// <summary>
    /// The cadence as of right now — reads <see cref="IOptionsMonitor{T}.CurrentValue"/> fresh, and
    /// is handed to the prober as a delegate so it is re-evaluated on every cycle rather than
    /// frozen here at boot (gh-#125; the same live shape as <c>ScanService</c>'s
    /// <c>Library:ScanIntervalSeconds</c> retune). The <c>Math.Max(1, …)</c> floors mirror
    /// <c>ScanService</c>'s: <c>SettingValidator</c> already rejects out-of-range live edits, so
    /// these only guard a hand-edited appsettings that bypasses it — a zero interval would spin
    /// this loop hot.
    /// </summary>
    DependencyProbeCadence CurrentCadence()
    {
        var cfg = options.CurrentValue;
        return new DependencyProbeCadence(
            TimeSpan.FromSeconds(Math.Max(1, cfg.ProbeIntervalSeconds)),
            TimeSpan.FromSeconds(Math.Max(1, cfg.ProbeTimeoutSeconds)),
            Math.Max(1, cfg.UnhealthyThreshold));
    }
}
