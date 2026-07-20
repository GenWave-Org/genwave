namespace GenWave.Tts;

using Microsoft.Extensions.Logging;

/// <summary>
/// Drives the probe cadence for every registered <see cref="IDependencyProbe"/> (SPEC F70.2,
/// STORY-187). The periodic-timer loop lives here, in <see cref="RunAsync"/>, rather than in the
/// Host's <c>DependencyHealthProbeService</c> BackgroundService shell — so the cadence and
/// never-throws contract are unit-testable directly, without spinning a host (the
/// aspnetcore-patterns house rule: put cycle logic in a method/class that takes its dependencies
/// and a token, test that directly).
/// <para>
/// One probe's failure never blocks or fails another: each gets its own linked timeout token and
/// its own try/catch inside <see cref="RunCycleAsync"/>, and a timeout is recorded as an unhealthy
/// verdict with a reason rather than thrown (STORY-187 AC3). Nothing in this class ever lets an
/// exception escape <see cref="RunAsync"/>/<see cref="RunCycleAsync"/> except the caller's own
/// cancellation — the one case that must propagate so a host shutdown actually stops the loop.
/// </para>
/// </summary>
public sealed class DependencyHealthProber(
    IEnumerable<IDependencyProbe> probes,
    DependencyHealthStore store,
    ILogger<DependencyHealthProber> logger)
{
    /// <summary>
    /// Reason recorded when <see cref="IDependencyProbe.ProbeAsync"/> returns false — "disabled by
    /// design" (e.g. empty <c>Llm:Endpoint</c>, SPEC F34.2), never an actual probe failure. Shared
    /// so a reader (<see cref="DegradationController"/>'s probe-driven drop, SPEC F69.2) can tell
    /// this apart from a genuine outage without re-deriving the string.
    /// </summary>
    public const string NotConfiguredReason = "not configured";

    /// <summary>
    /// Probes once immediately — so a verdict exists as soon as possible after boot rather than
    /// only after the first full interval elapses — then again every <paramref name="interval"/>
    /// until <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task RunAsync(TimeSpan interval, TimeSpan perProbeTimeout, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunCycleAsync(perProbeTimeout, ct);
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>
    /// One pass over every registered probe, in order. Exposed separately from
    /// <see cref="RunAsync"/> so a test (or a caller with its own cadence) can drive exactly N
    /// cycles without waiting on real or faked time.
    /// </summary>
    public async Task RunCycleAsync(TimeSpan perProbeTimeout, CancellationToken ct)
    {
        foreach (var probe in probes)
        {
            await ProbeOneAsync(probe, perProbeTimeout, ct);
        }
    }

    async Task ProbeOneAsync(IDependencyProbe probe, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var healthy = await probe.ProbeAsync(timeoutCts.Token);
            store.Record(probe.DependencyName, healthy, healthy ? null : NotConfiguredReason);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller (host shutdown, or this test) cancelled — not our own per-probe timeout.
            // Propagate so RunAsync's loop actually ends instead of recording a bogus verdict.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Only our own CancelAfter could have fired at this point (ct itself is NOT
            // cancelled) — a genuine probe timeout (SPEC F70.2 AC3).
            logger.LogWarning("{Dependency} health probe timed out after {TimeoutSeconds}s",
                probe.DependencyName, timeout.TotalSeconds);
            store.Record(probe.DependencyName, healthy: false,
                reason: $"probe timed out after {timeout.TotalSeconds:F0}s");
        }
        catch (Exception ex)
        {
            // Connect failure, non-2xx (EnsureSuccessStatusCode), or any other probe fault —
            // every one of these degrades to an unhealthy verdict; none of them ever throws
            // out of this method (STORY-187 AC3: "the probe service keeps running").
            logger.LogWarning(ex, "{Dependency} health probe failed", probe.DependencyName);
            store.Record(probe.DependencyName, healthy: false, reason: ex.Message);
        }
    }
}
