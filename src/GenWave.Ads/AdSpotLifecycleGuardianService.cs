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
    AdSpotLocatorRoots locatorRoots,
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

            await SweepPreviewsAsync(now, ct);
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

    /// <summary>
    /// Deletes a rendered preview's own file, then clears its stamp (SPEC F176.2; STORY-429; PLAN
    /// T442) for every row <see cref="IAdSpotStore.ListPreviewsToSweepAsync"/> returns — a spot that
    /// left the editable draft/approved lifecycle, or a preview that simply outlived
    /// <see cref="AdsOptions.PreviewRetentionDays"/>. Runs AFTER the re-arm pass above, on the SAME
    /// sweep tick, deliberately never its own timer: one guardian, one cadence, two independent
    /// cleanup jobs.
    ///
    /// <para>
    /// <b>The path is re-asserted under the preview root here, in this SAME method (the CodeQL
    /// path-injection guard's own "strong guard" shape, <see cref="AdPreviewRoot.IsUnder"/>'s own
    /// remarks — the one construction/check site <see cref="AdRenderService"/> and
    /// <c>AdsController.PreviewWav</c> share, PLAN T442 ruling)</b> — even though <c>preview_path</c>
    /// only ever reaches this row via <c>AdRenderService.RenderPreviewAsync</c>'s own write, a stored
    /// path is untrusted the instant it crosses a storage boundary; an escaped row is skipped (deleted
    /// by no one) rather than trusted, and still has its stamp cleared so no dangling reference lingers
    /// forever.
    /// </para>
    /// </summary>
    async Task SweepPreviewsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var retention = TimeSpan.FromDays(adsOptions.CurrentValue.PreviewRetentionDays);
        var previewRoot = AdPreviewRoot.Resolve(locatorRoots);

        var candidates = await store.ListPreviewsToSweepAsync(retention, now, ct);
        foreach (var spot in candidates)
        {
            if (spot.PreviewPath is { } previewPath)
            {
                var target = Path.GetFullPath(previewPath);
                if (AdPreviewRoot.IsUnder(previewRoot, target))
                {
                    try
                    {
                        File.Delete(target);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "Ad spot preview sweep could not remove the file for spot {Id}", spot.Id);
                    }
                }
                else
                {
                    logger.LogWarning(
                        "Ad spot preview sweep found a preview path outside the preview root for spot {Id}", spot.Id);
                }
            }

            await store.ClearPreviewAsync(spot.Id, ct);
            logger.LogInformation("Ad spot preview swept spotId={Id}", spot.Id);
        }
    }
}
