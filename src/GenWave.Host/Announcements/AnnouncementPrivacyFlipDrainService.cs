using System.Threading.Channels;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Announcements;

/// <summary>
/// Drains <see cref="AnnouncementPrivacyFlipEventSink"/>'s queue (SPEC F145.2, STORY-359, PLAN T343)
/// — the private→public flip's own "nothing is ever held waiting behind the toggle" sweep. Mirrors
/// <see cref="AnnouncementAiredDrainService"/>'s own shape one seam over: isolated from the settings
/// write's own hot path by a queue, per-item try/catch, never crashes the loop.
///
/// <b>A failed decline sweep is logged and dropped, never retried here.</b> The rows it would have
/// declined remain <c>pending</c>/<c>claimed</c>, but they are never at risk of reaching a public
/// stream regardless: <c>AnnouncementsController</c>'s own F145.1 door already 403s every NEW
/// submission while public, and <c>SpectatorModeAnnouncementVendGuard</c>'s F145.2 vend refusal
/// already blocks every EXISTING row from being claimed for delivery — this sweep exists to make the
/// STATE visible (declined, with a reason), not to prevent an air. A row that outlives this failure
/// still resolves eventually via <see cref="AnnouncementLifecycleGuardianService"/>'s own
/// <c>ExpireStaleAsync</c> sweep once its TTL passes.
/// </summary>
sealed class AnnouncementPrivacyFlipDrainService(
    ChannelReader<AnnouncementPrivacyFlipSignal> queue,
    IAnnouncementLifecycle lifecycle,
    ILogger<AnnouncementPrivacyFlipDrainService> logger) : BackgroundService
{
    internal const string StationWentPublicReason = "station went public";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var signal in queue.ReadAllAsync(stoppingToken))
            await ProcessAsync(signal, stoppingToken);
    }

    /// <summary>The real per-signal work <see cref="ExecuteAsync"/>'s loop calls — directly testable
    /// without the hosted background loop, mirroring <see cref="AnnouncementAiredDrainService.ProcessAsync"/>'s
    /// own precedent.</summary>
    internal async Task ProcessAsync(AnnouncementPrivacyFlipSignal signal, CancellationToken ct)
    {
        _ = signal; // the signal carries no data of its own — see its own remarks

        try
        {
            var declined = await lifecycle.DeclineAllLiveAsync(StationWentPublicReason, ct);
            if (declined > 0)
                logger.LogInformation("Station went public — declined {Count} pending/claimed announcement(s)", declined);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Announcement privacy-flip decline sweep failed — playout unaffected");
        }
    }
}
