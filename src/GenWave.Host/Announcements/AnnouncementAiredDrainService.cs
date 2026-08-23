using System.Globalization;
using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Announcements;

/// <summary>
/// Drains <see cref="AnnouncementAiredEventSink"/>'s queue (SPEC F143.3, STORY-358, PLAN T343) —
/// mirrors <c>GenWave.MediaLibrary.Station.BoothLogDrainService</c>'s own "isolated from the hot-path
/// sink by a queue, per-item try/catch, never crashes the loop" shape one seam over. A failed
/// confirmation is logged and dropped (never retried here) — the row simply stays <c>claimed</c>
/// until <see cref="AnnouncementLifecycleGuardianService"/>'s own re-arm sweep reaches it, per the
/// sink contract's "must never affect playout" posture.
/// </summary>
sealed class AnnouncementAiredDrainService(
    ChannelReader<AnnouncementAiredSignal> queue,
    IAnnouncementLifecycle lifecycle,
    IBoothLogAppender boothLog,
    ILogger<AnnouncementAiredDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var signal in queue.ReadAllAsync(stoppingToken))
            await ProcessAsync(signal, stoppingToken);
    }

    /// <summary>
    /// The real per-item work <see cref="ExecuteAsync"/>'s loop calls — a distinct, directly testable
    /// seam (mirrors <c>BoothLogDrainService.ProcessAsync</c>'s own precedent) so a spec can drive one
    /// signal through the real confirmation path without running the hosted background loop itself.
    /// </summary>
    internal async Task ProcessAsync(AnnouncementAiredSignal signal, CancellationToken ct)
    {
        try
        {
            // SPEC F143.3: aired is stamped ONLY here, on this genuine TrackAired-derived signal.
            // MarkAiredAsync's own total, idempotent-safe transition (IAnnouncementLifecycle's own
            // remarks) means a row already aired/re-armed/expired/unknown answers null — a normal,
            // silent outcome, never an error.
            var collapseCount = await lifecycle.MarkAiredAsync(signal.AnnouncementId, ct);
            if (collapseCount is not { } count)
                return;

            var summary = count > 1
                ? $"Announcement #{signal.AnnouncementId.ToString(CultureInfo.InvariantCulture)} aired ({count.ToString(CultureInfo.InvariantCulture)} submissions collapsed into it)"
                : $"Announcement #{signal.AnnouncementId.ToString(CultureInfo.InvariantCulture)} aired";

            await boothLog.AppendAsync(new BoothLogAppendRequest("announcement-aired", summary, PersonaId: null), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Announcement aired confirmation failed for announcement {AnnouncementId} — playout unaffected",
                signal.AnnouncementId);
        }
    }
}
