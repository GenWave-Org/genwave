using System.Globalization;
using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Announcements;

/// <summary>
/// Drains <see cref="AnnouncementAiredEventSink"/>'s queue (SPEC F143.3, F202.2; STORY-358, STORY-469;
/// PLAN T343, T554) — mirrors <c>GenWave.MediaLibrary.Station.BoothLogDrainService</c>'s own "isolated
/// from the hot-path sink by a queue, per-item try/catch, never crashes the loop" shape one seam over.
///
/// <para>
/// <b>A failed confirmation is retried, not dropped on the first fault (SPEC F202.2).</b>
/// <see cref="ProcessAsync"/> retries <see cref="RetryDelays"/> (1 s, 5 s, 30 s) against
/// <paramref name="timeProvider"/> before giving up; only once the fourth attempt (the original plus
/// all three retries) still fails does it log a single <c>WARN</c> naming the announcement id and drop
/// the signal — the row stays <c>claimed</c> until
/// <see cref="AnnouncementLifecycleGuardianService"/>'s own re-arm sweep reaches it, per the sink
/// contract's "must never affect playout" posture.
/// </para>
///
/// <para>
/// <b>Documented residual (SPEC F202.4).</b> A process restart between air and drain drops every
/// signal still in the (unbounded, in-memory) queue — the row stays <c>claimed</c> and the guardian's
/// re-arm sweep re-delivers it, which can re-air that one announcement at most once. This is
/// unchanged by the retry loop above: retries cover a transient store fault mid-drain, not a lost
/// process.
/// </para>
/// </summary>
sealed class AnnouncementAiredDrainService(
    ChannelReader<AnnouncementAiredSignal> queue,
    IAnnouncementLifecycle lifecycle,
    IBoothLogAppender boothLog,
    TimeProvider timeProvider,
    ILogger<AnnouncementAiredDrainService> logger) : BackgroundService
{
    /// <summary>The retry backoff between <see cref="MarkAiredAsync"/> attempts (SPEC F202.2) — three
    /// delays, so a signal gets four total attempts before <see cref="ProcessAsync"/> gives up and logs
    /// a single <c>WARN</c>. Driven through <paramref name="timeProvider"/> (never
    /// <see cref="TimeProvider.System"/> directly) so a spec can ride the delay on a
    /// <c>FakeTimeProvider</c> instead of a real 36-second wait.</summary>
    internal static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

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
        int? collapseCount = null;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // SPEC F143.3: aired is stamped ONLY here, on this genuine TrackAired-derived signal.
                // MarkAiredAsync's own total, idempotent-safe transition (IAnnouncementLifecycle's own
                // remarks; SPEC F202.3) means a row already aired/re-armed/expired/unknown answers
                // null — a normal, silent outcome, never an error.
                collapseCount = await lifecycle.MarkAiredAsync(signal.AnnouncementId, ct);
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                // Intermediate failures stay below Warning (AC6: exactly one WARN per exhausted
                // signal) — Debug is enough to trace a transient fault through the logs without
                // paging anyone over a retry that is about to succeed.
                logger.LogDebug(
                    ex,
                    "Announcement aired confirmation attempt {Attempt} failed for announcement {AnnouncementId} — retrying in {DelaySeconds}s",
                    attempt + 1, signal.AnnouncementId, RetryDelays[attempt].TotalSeconds);
                await Task.Delay(RetryDelays[attempt], timeProvider, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Announcement aired confirmation failed after {Attempts} attempts for announcement {AnnouncementId} — dropped; the guardian's re-arm sweep is the fallback",
                    RetryDelays.Length + 1, signal.AnnouncementId);
                return;
            }
        }

        if (collapseCount is not { } count)
            return;

        var summary = count > 1
            ? $"Announcement #{signal.AnnouncementId.ToString(CultureInfo.InvariantCulture)} aired ({count.ToString(CultureInfo.InvariantCulture)} submissions collapsed into it)"
            : $"Announcement #{signal.AnnouncementId.ToString(CultureInfo.InvariantCulture)} aired";

        // The booth-log append keeps its own, separate try/catch posture (unchanged from before this
        // task): a failure here happens AFTER the mark already succeeded, so it is not retried — one
        // WARN, and the aired stamp itself is unaffected either way.
        try
        {
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
                "Announcement aired booth-log entry failed for announcement {AnnouncementId} — aired stamp unaffected",
                signal.AnnouncementId);
        }
    }
}
