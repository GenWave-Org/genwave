using GenWave.Core.Abstractions;

namespace GenWave.Host.Announcements;

/// <summary>
/// The periodic sweep half of the lifecycle guardians (SPEC F143.2, F144.5, STORY-358, PLAN T343) —
/// the resilient-loop shape every timer-driven Host worker in this codebase shares
/// (<c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c>, <c>GenWave.Host.Playout.ContextTickerService</c>):
/// catch, log, continue on the next tick — a fault here must never stop the loop, let alone the host.
///
/// <para>
/// <b>Two duties, one deliberate order.</b> Each tick first runs
/// <see cref="IAnnouncementLifecycle.ExpireStaleAsync"/>, THEN reads
/// <see cref="IAnnouncementLifecycle.FindClaimedPastGraceAsync"/> and re-arms every candidate it
/// returns. This order is load-bearing, not incidental: SPEC F144.5's own "TTL permitting" clause
/// means a claimed row whose TTL has already passed must expire, never re-arm — running the expiry
/// sweep first guarantees every row <see cref="IAnnouncementLifecycle.FindClaimedPastGraceAsync"/>
/// can still see has TTL remaining, so re-arming it can never be the wrong call.
/// </para>
///
/// <para>
/// <b>Sweep cadence and its own consequence (T339 review carry-forward — "expired-unswept rows count
/// toward the depth cap").</b> <see cref="SweepInterval"/> below is this loop's own answer to that:
/// a pending row past its own TTL still occupies a
/// <c>AnnouncementsOptions.PendingDepthCap</c> slot (SPEC F143.4) until THIS sweep reaches it, so this
/// loop's own latency IS the effective ceiling's slack — at most <see cref="SweepInterval"/> worth of
/// expired-but-unswept rows can be sitting in the cap's count at any instant. 60s against a 900s
/// default TTL and a 12-row default cap keeps that slack small relative to either number without a
/// tighter interval buying anything worth the extra idle-tick cost (see that field's own remarks for
/// the full trade-off).
/// </para>
/// </summary>
sealed class AnnouncementLifecycleGuardianService(
    IAnnouncementLifecycle lifecycle,
    TimeProvider timeProvider,
    ILogger<AnnouncementLifecycleGuardianService> logger) : BackgroundService
{
    /// <summary>
    /// The sweep cadence — mirrors <c>CrosstalkStockWorker.TickInterval</c>'s own trade-off one seam
    /// over: frequent enough that a TTL-expired or claim-grace-passed row is caught within, at most,
    /// one minute of becoming eligible (comfortably inside F143.4's own 900s default TTL and
    /// <see cref="ReArmGrace"/>'s own ~6-minute grace, so neither law's own numbers are meaningfully
    /// eroded by this loop's own latency); infrequent enough that an idle tick — the common case at
    /// this station's traffic shape — costs nothing worth tuning away.
    /// </summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// F144.5's "claim + one break cycle" grace, as a documented constant rather than a
    /// fake-precise derivation off a live cadence signal. This station's own break cadence is not a
    /// single honest number to derive from: unit assembly is cadence-independent by design (T341's
    /// own ruling — every unit, ceremony-only path included, never hostage to a cadence flag), and the
    /// live schedule/format-clock grid can vary a show's own break spacing across the day — deriving
    /// "one break cycle" from any single one of those signals would be a false precision this constant
    /// deliberately avoids. ~6 minutes is comfortably longer than a typical break-to-break span at this
    /// station's traffic shape (a claimed announcement genuinely waiting for its own next break rarely
    /// waits anywhere near this long) while staying well inside the 900s default TTL, so a push-loss
    /// re-arm has real TTL runway left to be claimed and delivered again.
    /// </summary>
    internal static readonly TimeSpan ReArmGrace = TimeSpan.FromMinutes(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Announcement lifecycle guardian started: sweeping every {IntervalSeconds}s", SweepInterval.TotalSeconds);

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

        logger.LogInformation("Announcement lifecycle guardian stopped");
    }

    /// <summary>One sweep, internal so a spec can drive it directly without the real timer (mirrors
    /// <c>CrosstalkStockWorker.TickOnceAsync</c>'s own precedent). Never throws past the "caller
    /// cancelled" case — every other fault is logged and swallowed.</summary>
    internal async Task SweepOnceAsync(CancellationToken ct)
    {
        try
        {
            var now = timeProvider.GetUtcNow();

            // Order is load-bearing — see this class's own remarks.
            var expired = await lifecycle.ExpireStaleAsync(now, ct);

            var reArmCandidates = await lifecycle.FindClaimedPastGraceAsync(ReArmGrace, now, ct);
            var reArmed = 0;
            foreach (var id in reArmCandidates)
            {
                if (await lifecycle.ReArmAsync(id, ct))
                    reArmed++;
            }

            // gh-#558 volume lesson: no line at all when the tick found nothing to do.
            if (expired > 0 || reArmed > 0)
                logger.LogInformation("Announcement lifecycle sweep: expired={Expired} reArmed={ReArmed}", expired, reArmed);
        }
        catch (OperationCanceledException)
        {
            throw; // caller cancellation (shutdown) — must propagate to stop the loop
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Announcement lifecycle sweep failed; continuing on the next tick");
        }
    }
}
