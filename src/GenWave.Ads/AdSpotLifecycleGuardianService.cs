namespace GenWave.Ads;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// The stuck-<c>rendering</c> guardian (SPEC F161.1; STORY-391 AC6; PLAN T402) — the
/// <c>AnnouncementLifecycleGuardianService</c> re-arm shape applied to <c>station.ad_spot</c>: a
/// periodic sweep that finds every row stuck <see cref="AdState.Rendering"/> past its own grace and
/// re-arms it back to <see cref="AdState.Approved"/>, so a crashed worker process (never one that
/// finished cleanly — see <see cref="AdSpotWorker"/>'s own render-budget handling) does not orphan a
/// spot forever.
///
/// <para>
/// <b>The grace is PINNED to exceed <see cref="AdsOptions.RenderBudgetSeconds"/> by construction
/// (PLAN T402 review block 1), not merely tuned near it.</b> Every sweep re-computes
/// <see cref="AdSpotGuardianGrace.Compute"/> — the SAME shared helper <c>AdSpotWorker</c>'s own repair
/// sweep reads (PLAN T402 review F1/F4, one time constant) — adding <see cref="AdSpotGuardianGrace.Margin"/>,
/// a fixed positive headroom, so for ANY value an operator sets that knob to, this guardian's own
/// grace is mathematically guaranteed larger. That relation is what makes <see cref="AdSpotWorker"/>'s own
/// render-budget timeout structurally win the race every time: a render that is genuinely still
/// running always self-terminates (via its OWN <c>CancelAfter(RenderBudgetSeconds)</c>, transitioning
/// the row to <see cref="AdState.Failed"/> or, on a break-window yield, straight back to
/// <see cref="AdState.Approved"/>) before this guardian's own grace could ever elapse for it. This
/// sweep therefore only ever catches a row NO live worker is still attending to — a crashed process,
/// never a render honestly in flight — closing the class of "each loop orphans another row" the
/// review named, by construction rather than by hoping the two numbers never drift apart.
/// </para>
/// </summary>
sealed class AdSpotLifecycleGuardianService(
    IAdSpotStore store,
    IOptionsMonitor<AdsOptions> adsOptions,
    TimeProvider timeProvider,
    ILogger<AdSpotLifecycleGuardianService> logger) : BackgroundService
{
    /// <summary>The sweep cadence — mirrors <c>AnnouncementLifecycleGuardianService.SweepInterval</c>'s
    /// own trade-off: frequent enough that a stuck row is caught within, at most, one minute of
    /// crossing its own grace; infrequent enough that an idle tick (the common case) costs
    /// nothing.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Ad spot lifecycle guardian started: sweeping every {IntervalSeconds}s", SweepInterval.TotalSeconds);

        try
        {
            using var timer = new PeriodicTimer(SweepInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Ad spot lifecycle guardian stopped");
    }

    /// <summary>One sweep, internal so a spec can drive it directly without the real timer (mirrors
    /// <c>AnnouncementLifecycleGuardianService.SweepOnceAsync</c>'s own precedent). Never throws past
    /// the "caller cancelled" case.</summary>
    internal async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var grace = AdSpotGuardianGrace.Compute(adsOptions.CurrentValue);

            var candidates = await store.FindRenderingPastGraceAsync(grace, now, ct);
            var reArmed = 0;
            foreach (var id in candidates)
            {
                if (await store.ReArmAsync(id, ct))
                    reArmed++;
            }

            // gh-#558 volume lesson (the SAME posture AnnouncementLifecycleGuardianService already
            // keeps): no line at all when the tick found nothing to do.
            if (reArmed > 0)
                logger.LogInformation("Ad spot lifecycle sweep: reArmed={ReArmed}", reArmed);
        }
        catch (OperationCanceledException)
        {
            throw; // caller cancellation (shutdown) — must propagate to stop the loop
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot lifecycle sweep failed; continuing on the next tick");
        }
    }
}
