using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.MediaLibrary.Options;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// The Library Gardener's tick (SPEC F153.2, F150.9; STORY-374; PLAN T372, gh-#529) — a
/// <see cref="BackgroundService"/> mirroring <c>Enrich.EnrichmentService</c>'s own bounded-batch
/// backfill-loop shape (ARCHITECTURE.md's own reuse-map entry): housekeeping first
/// (<see cref="IThumbStore.RecomputeAllAsync"/>, <see cref="IThumbStore.SweepAsync"/> — the F150.9
/// nudge decay + thumb retention sweep, wired since T365), then every registered
/// <see cref="IGardenerPass"/> in DI order, each isolated in its own try/catch so one pass throwing
/// costs the tick a single WARN naming that pass and never touches the others (STORY-374 AC5).
///
/// <para>
/// <b>The first tick waits one short breather</b> — <see cref="LibraryOptions.ScanIntervalSeconds"/>
/// (never a new Gardener-only knob), the SAME value <c>Scan.ScanService</c>/<c>Enrich.EnrichmentService</c>
/// already read for their own live-editable cadence, reused here purely as a startup delay rather
/// than an inter-tick one. This is STORY-374 AC6's own honest test seam: a live override of this
/// ONE existing key controls how soon the very first real tick fires, with no direct call into any
/// <see cref="IGardenerPass"/> and no test-only branch in this class.
/// </para>
///
/// <para>
/// <see cref="GardenerOptions.IntervalMinutes"/> governs every tick after the first, re-read live
/// via <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every iteration (the same F44.2
/// live-editable posture every other Gardener/library knob already honors) — floored at one minute,
/// matching the boot-validated <c>[Range(1, 1440)]</c> on <see cref="GardenerOptions.IntervalMinutes"/>
/// itself.
/// </para>
/// </summary>
sealed class GardenerService(
    IEnumerable<IGardenerPass> passes,
    IThumbStore thumbStore,
    IOptionsMonitor<GardenerOptions> gardenerOptions,
    IOptionsMonitor<LibraryOptions> libraryOptions,
    ILogger<GardenerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(CurrentStartupDelay, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(CurrentInterval);
        do
        {
            try
            {
                await RunTickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Defense-in-depth only — every step inside RunTickAsync already catches and logs
                // its own failure, so this only ever fires for something genuinely unexpected (e.g.
                // enumerating `passes` itself faulting), mirroring EnrichmentService's own outer
                // "Backfill loop iteration failed; will retry after interval" catch.
                log.LogError(ex, "Gardener tick failed unexpectedly; will retry after interval");
            }

            // Re-read live (SPEC F44.2) before the wait for the NEXT tick — a live edit of
            // Gardener:IntervalMinutes governs the delay to the next tick without disturbing the one
            // that just completed.
            timer.Period = CurrentInterval;
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>The breather before the very first tick — see this class's own remarks. Private
    /// (T372 review LOW-1): only <see cref="ExecuteAsync"/> calls this, and nothing in this test
    /// suite reaches it directly — STORY-374 AC6 exercises it exclusively through the real,
    /// container-resolved <see cref="BackgroundService"/> lifecycle, the honest way to prove a
    /// timing seam behaves.</summary>
    TimeSpan CurrentStartupDelay =>
        TimeSpan.FromSeconds(Math.Max(1, libraryOptions.CurrentValue.ScanIntervalSeconds));

    /// <summary>The live inter-tick interval, floored at one minute. Private — see
    /// <see cref="CurrentStartupDelay"/>'s own remarks.</summary>
    TimeSpan CurrentInterval =>
        TimeSpan.FromMinutes(Math.Max(1, gardenerOptions.CurrentValue.IntervalMinutes));

    /// <summary>One tick: housekeeping, then every registered pass, each bounded by its own timeout
    /// (T372 review LOW-3) so a pass that ignores its own <c>ct</c> cannot wedge the ones after it or
    /// the next tick. Private — only <see cref="ExecuteAsync"/> calls this; STORY-374 AC5 exercises
    /// it exclusively through the real, ticking <c>GardenerService</c>, never a direct call.</summary>
    async Task RunTickAsync(CancellationToken ct)
    {
        var tickBudget = CurrentInterval;

        await RunStepAsync("nudge decay recompute", thumbStore.RecomputeAllAsync, tickBudget, ct);
        await RunSweepStepAsync(tickBudget, ct);

        foreach (var pass in passes)
        {
            try
            {
                using var passCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                passCts.CancelAfter(tickBudget);
                await pass.RunAsync(passCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // T372 review LOW-3: the pass's OWN token (not the outer ct) expired — a pass that
                // ignores cancellation and simply runs long, not a shutdown. One WARN naming the
                // pass, exactly like any other pass failure; the loop continues to the next pass.
                log.LogWarning(
                    "Gardener: pass {Kind} did not complete within {Budget}; the next tick retries",
                    pass.Kind, tickBudget);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Gardener: pass {Kind} failed; the next tick retries", pass.Kind);
            }
        }
    }

    /// <summary>Log-and-continue for one housekeeping step (SPEC F153.2's own "housekeeping first"
    /// ordering) — isolated exactly like a pass, so a thumb-store outage never blocks the passes that
    /// follow it in the same tick. Bounded by <paramref name="budget"/> the same way a pass is
    /// (T372 review LOW-3) — a step that ignores its own <c>ct</c> cannot wedge the passes after it.
    /// </summary>
    async Task RunStepAsync(string name, Func<CancellationToken, Task> step, TimeSpan budget, CancellationToken ct)
    {
        try
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stepCts.CancelAfter(budget);
            await step(stepCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            log.LogWarning("Gardener: {Step} did not complete within {Budget}; the next tick retries", name, budget);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Gardener: {Step} failed; the next tick retries", name);
        }
    }

    /// <summary>The thumb retention sweep's own step (T372 review LOW-4): unlike
    /// <see cref="RunStepAsync"/>'s generic <c>Func&lt;CancellationToken, Task&gt;</c> shape, this
    /// one needs <see cref="IThumbStore.SweepAsync"/>'s own <c>Task&lt;int&gt;</c> result — the
    /// deleted-row count — to log it, so it is its own small method rather than a generic-delegate
    /// instantiation. Same bounded-CTS/log-and-continue posture as <see cref="RunStepAsync"/>.
    /// </summary>
    async Task RunSweepStepAsync(TimeSpan budget, CancellationToken ct)
    {
        try
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stepCts.CancelAfter(budget);
            var swept = await thumbStore.SweepAsync(stepCts.Token);
            log.LogInformation("Gardener swept {Count} thumb rows past retention", swept);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            log.LogWarning("Gardener: thumb retention sweep did not complete within {Budget}; the next tick retries", budget);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Gardener: thumb retention sweep failed; the next tick retries");
        }
    }
}
