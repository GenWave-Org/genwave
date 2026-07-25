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
    /// only after the first full interval elapses — then again on the cadence
    /// <paramref name="cadence"/> reports, until <paramref name="ct"/> is cancelled.
    /// <para>
    /// <paramref name="cadence"/> is a delegate, not a value, so every knob is re-read once per
    /// cycle and an operator edit lands on the very next probe with no api restart (SPEC F70.2,
    /// gh-#125). The timer is retuned via <see cref="PeriodicTimer.Period"/> AFTER each cycle, so a
    /// changed interval governs the next wait without disturbing the tick that just completed —
    /// the same shape as <c>ScanService</c>'s <c>Library:ScanIntervalSeconds</c> retune.
    /// </para>
    /// </summary>
    public async Task RunAsync(Func<DependencyProbeCadence> cadence, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(cadence().Interval);
        do
        {
            await RunCycleAsync(cadence(), ct);
            timer.Period = cadence().Interval;
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>
    /// One pass over every registered probe, in order. Exposed separately from
    /// <see cref="RunAsync"/> so a test (or a caller with its own cadence) can drive exactly N
    /// cycles without waiting on real or faked time.
    /// </summary>
    public async Task RunCycleAsync(DependencyProbeCadence cadence, CancellationToken ct)
    {
        foreach (var probe in probes)
        {
            await ProbeOneAsync(probe, cadence, ct);
        }
    }

    async Task ProbeOneAsync(IDependencyProbe probe, DependencyProbeCadence cadence, CancellationToken ct)
    {
        var timeout = cadence.PerProbeTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var healthy = await probe.ProbeAsync(timeoutCts.Token);

            // Deliberately NOT debounced (threshold 1). A false here is "disabled by design"
            // (NotConfiguredReason — e.g. an empty Llm:Endpoint, SPEC F34.2), which is a
            // deterministic declaration the probe repeats identically every cycle, not a flap.
            // Debouncing it would only delay the truth by one interval and would leave
            // DegradationController's probe-driven drop (F69.2) briefly reading an unconfigured
            // dependency as healthy. AC5's threshold exists for transient FAILURES only.
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
            RecordFailure(probe, cadence, $"probe timed out after {timeout.TotalSeconds:F0}s", ex: null);
        }
        catch (Exception ex)
        {
            // Connect failure, non-2xx (EnsureSuccessStatusCode), or any other probe fault —
            // every one of these degrades to an unhealthy verdict; none of them ever throws
            // out of this method (STORY-187 AC3: "the probe service keeps running").
            RecordFailure(probe, cadence, ex.Message, ex);
        }
    }

    /// <summary>
    /// Records one failed probe and logs it at a severity that matches what actually happened
    /// (SPEC F70.2 AC5, gh-#125). A failure that has NOT yet crossed
    /// <see cref="DependencyProbeCadence.UnhealthyThreshold"/> changed no routing decision, so it
    /// logs at Debug — it is a missed probe, not an incident. Only the failure that actually flips
    /// the verdict (and therefore starts diverting renders to the fallback engine) is a warning.
    /// This is what keeps a busy-but-alive dependency off the warning stream entirely: gh-#125's
    /// Kokoro blocks its own event loop for the length of a render, so it misses isolated probes
    /// forever, and paging on that is noise.
    /// </summary>
    void RecordFailure(IDependencyProbe probe, DependencyProbeCadence cadence, string reason, Exception? ex)
    {
        var verdict = store.Record(probe.DependencyName, healthy: false, reason, cadence.UnhealthyThreshold);

        if (verdict.Healthy)
        {
            logger.LogDebug(ex,
                "{Dependency} health probe failed ({Reason}) — {Failures} of {Threshold} consecutive "
                + "failures needed before the verdict flips; cached verdict still healthy",
                probe.DependencyName, reason, verdict.ConsecutiveFailureCount, cadence.UnhealthyThreshold);
            return;
        }

        logger.LogWarning(ex,
            "{Dependency} health probe failed ({Reason}) — {Failures} consecutive failures, cached "
            + "verdict is now unhealthy",
            probe.DependencyName, reason, verdict.ConsecutiveFailureCount);
    }
}
