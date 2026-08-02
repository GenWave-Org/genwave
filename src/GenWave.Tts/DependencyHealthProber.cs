namespace GenWave.Tts;

using System.Collections.Concurrent;
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
    ILogger<DependencyHealthProber> logger,
    TimeProvider? timeProvider = null)
{
    // Nullable-with-System-default rather than required: the host injects the container's
    // TimeProvider (AddGenWaveTts TryAdds TimeProvider.System), while a spec passes a
    // FakeTimeProvider to drive the loop cycle-by-cycle instead of racing wall-clock sleeps
    // against it (gh-#171, the gh-#106 class).
    readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Per-dependency record of the reason this prober has already WARNED about, keyed by
    /// dependency name and present only while that dependency's cached verdict is unhealthy
    /// (gh-#338).
    /// <para>
    /// This exists so the warning fires on the EDGE — the probe that actually flips the verdict —
    /// and not on every probe thereafter. It has to key on "have we warned since the verdict last
    /// changed" rather than on "is the verdict unhealthy", or a dependency that drops, recovers and
    /// drops again would go silent on the second drop. Deriving the edge from
    /// <see cref="DependencyHealthVerdict.ConsecutiveFailureCount"/> against the threshold instead
    /// would look simpler and be wrong: the threshold is re-read live every cycle, so lowering it
    /// mid-outage flips the verdict at a count that is already past it, and the flip would log at
    /// Debug.
    /// </para>
    /// <para>
    /// A ConcurrentDictionary, not a plain one: <see cref="RunCycleAsync"/> is public and a caller
    /// with its own cadence may drive cycles from more than one thread. <c>TryAdd</c>/<c>TryRemove</c>
    /// are the atomic edge tests — the add succeeds exactly once per outage.
    /// </para>
    /// </summary>
    readonly ConcurrentDictionary<string, string> warnedUnhealthyReasons = new(StringComparer.Ordinal);

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
        using var timer = new PeriodicTimer(cadence().Interval, time);
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
        // The timeout gets its own CTS on the injected TimeProvider (CancelAfter has no
        // TimeProvider overload), linked with the caller's token — same semantics as the old
        // linked-source-plus-CancelAfter shape, but a FakeTimeProvider can now fire it (gh-#171).
        using var timeoutCts = new CancellationTokenSource(timeout, time);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var healthy = await probe.ProbeAsync(linkedCts.Token);

            // Deliberately NOT debounced (threshold 1). A false here is "disabled by design"
            // (NotConfiguredReason — e.g. an empty Llm:Endpoint, SPEC F34.2), which is a
            // deterministic declaration the probe repeats identically every cycle, not a flap.
            // Debouncing it would only delay the truth by one interval and would leave
            // DegradationController's probe-driven drop (F69.2) briefly reading an unconfigured
            // dependency as healthy. AC5's threshold exists for transient FAILURES only.
            store.Record(probe.DependencyName, healthy, healthy ? null : NotConfiguredReason);

            // The recovering edge (gh-#338). Only fires if this prober actually warned about the
            // outage, so the "not configured" path above — which records unhealthy but deliberately
            // logs nothing, being a declaration rather than a fault — never produces a lone
            // "recovered" line for an outage nobody was told about.
            if (healthy && warnedUnhealthyReasons.TryRemove(probe.DependencyName, out var wasFailing))
            {
                logger.LogInformation(
                    "{Dependency} health probe recovered — cached verdict is healthy again "
                    + "(was failing: {Reason})",
                    probe.DependencyName, wasFailing);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller (host shutdown, or this test) cancelled — not our own per-probe timeout.
            // Propagate so RunAsync's loop actually ends instead of recording a bogus verdict.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Only our own timeout CTS could have fired at this point (ct itself is NOT
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
    /// <para>
    /// "Only the failure that actually flips the verdict is a warning" was always the intent; until
    /// gh-#338 the code did not honour it. The warn branch fired on every failure once the verdict
    /// was unhealthy — re-announcing a transition that may have happened hours earlier, with an
    /// identical stack trace each time. It is now genuinely edge-triggered via
    /// <see cref="warnedUnhealthyReasons"/>: one warning per outage on the way down, one
    /// Information line on the way back up, Debug for everything in between.
    /// </para>
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

        // The failing edge: warn once, with the exception, on the probe that actually flipped the
        // verdict. TryAdd succeeds exactly once per outage — it is the edge test, and it is atomic.
        if (warnedUnhealthyReasons.TryAdd(probe.DependencyName, reason))
        {
            logger.LogWarning(ex,
                "{Dependency} health probe failed ({Reason}) — {Failures} consecutive failures, cached "
                + "verdict is now unhealthy",
                probe.DependencyName, reason, verdict.ConsecutiveFailureCount);
            return;
        }

        // Already warned, verdict unchanged: Debug, and WITHOUT the exception (gh-#338). The old
        // code re-fired the warning above every cycle, wording it "is now unhealthy" each time — on
        // a piper-only box, where kokoro is absent by construction and the verdict can never
        // change, that was ~2,880 warnings a day carrying an identical stack trace, for a condition
        // already reported once. The reason string carries the cause; the first occurrence carries
        // the trace.
        //
        // A reason that CHANGES mid-outage stays at Debug too, deliberately: re-warning on it would
        // reopen exactly this hole for any dependency whose failure mode oscillates.
        logger.LogDebug(
            "{Dependency} health probe still failing ({Reason}) — {Failures} consecutive failures, "
            + "cached verdict unchanged since it flipped unhealthy",
            probe.DependencyName, reason, verdict.ConsecutiveFailureCount);
    }
}
