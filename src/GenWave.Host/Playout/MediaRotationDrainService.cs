using System.Threading.Channels;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Playout;

/// <summary>
/// Drains <see cref="MediaRotationEventSink"/>'s queue (SPEC F149.2, STORY-367, PLAN T355) —
/// mirrors <c>GenWave.Host.Announcements.AnnouncementAiredDrainService</c>'s own "isolated from the
/// hot-path sink by a queue, per-item try/catch, never crashes the loop" shape one seam over
/// (STORY-367 AC8: a ledger write failure never delays air). A failed write is logged and dropped
/// (never retried here) — the row simply never counts that one airing; the next genuine airing of
/// the same track upserts normally.
/// </summary>
sealed class MediaRotationDrainService(
    ChannelReader<MediaRotationAiredSignal> queue,
    IMediaRotationSink ledger,
    ILogger<MediaRotationDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var signal in queue.ReadAllAsync(stoppingToken))
            await ProcessAsync(signal, stoppingToken);
    }

    /// <summary>
    /// The real per-item work <see cref="ExecuteAsync"/>'s loop calls — a distinct, directly
    /// testable seam (mirrors <c>AnnouncementAiredDrainService.ProcessAsync</c>'s own precedent) so
    /// a spec can drive one signal through the real write path without running the hosted
    /// background loop itself.
    /// </summary>
    internal async Task ProcessAsync(MediaRotationAiredSignal signal, CancellationToken ct)
    {
        try
        {
            await ledger.RecordAiringAsync(signal.MediaId, signal.AiredAt, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Rotation ledger write failed for media {MediaId} — playout unaffected", signal.MediaId);
        }
    }
}
