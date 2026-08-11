using System.Threading.Channels;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Drains <see cref="BoothLogWriter"/>'s queue and persists each entry via <see cref="IBoothLogAppender"/>
/// (SPEC F72.1, F72.3, STORY-195). Isolated from <see cref="BoothLogWriter.Publish"/>'s hot-path call
/// by design — a DB outage or latency spike here never reaches the feeder tick or a TTS render. A
/// failed append is logged and the entry dropped (never retried, never crashes the loop), per the
/// sink contract's "must never affect playout" posture.
///
/// <see cref="BoothLogEntryRequest.PersonaId"/> (SPEC F84.6, STORY-215) arrives here already resolved
/// — <see cref="BoothLogWriter.Publish"/> captured it SYNCHRONOUSLY at air time, before the entry ever
/// reached this queue. This loop persists it verbatim and never calls
/// <see cref="IActivePersonaAccessor"/> itself: re-resolving here, at drain time, would mis-stamp a
/// row that backed up behind a bounded-queue backlog with whatever persona is active once the
/// backlog clears rather than the one that was on air when the row was created — exactly the "never
/// inferred after the fact" failure F84.6 rules out. A persona deleted between air and this drain is
/// an append-time (FK) concern, degraded inside <c>BoothLogRepository.AppendAsync</c>, not here.
///
/// <see cref="BoothLogEntryRequest.Pick"/> (SPEC F86.1, STORY-217, PLAN T73) arrives the same way —
/// already resolved to its final jsonb text (or <see langword="null"/>) by
/// <see cref="BoothLogWriter.Publish"/> — and is persisted verbatim, never re-serialized or
/// re-derived here.
///
/// <see cref="BoothLogEntryRequest.SegmentKind"/> (SPEC F113.1, STORY-304, PLAN T220) arrives the
/// same way — already stringified (or <see langword="null"/>) by <see cref="BoothLogWriter.Publish"/>
/// — and is persisted verbatim.
///
/// <see cref="BoothLogEntryRequest.ShowId"/> (SPEC F121.1, STORY-310, PLAN T242) arrives the same
/// way — already resolved off <c>IActivePersonaAccessor.ActiveShowId</c> by
/// <see cref="BoothLogWriter.Publish"/> at air time — and is persisted verbatim; no FK to degrade
/// (F121.1: history outlives the entity), so unlike <see cref="BoothLogEntryRequest.PersonaId"/> it
/// has no append-time retry concern for <c>BoothLogRepository</c> to handle either.
/// </summary>
sealed class BoothLogDrainService(
    ChannelReader<BoothLogEntryRequest> queue,
    IBoothLogAppender store,
    ILogger<BoothLogDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
            await ProcessAsync(request, stoppingToken);
    }

    /// <summary>
    /// The real per-item work <see cref="ExecuteAsync"/>'s loop calls — a distinct, directly
    /// testable seam (STORY-195) so a DB-backed spec can drive one entry through the real append +
    /// retention path without needing to run (and poll) the hosted background loop itself.
    /// </summary>
    internal async Task ProcessAsync(BoothLogEntryRequest request, CancellationToken ct)
    {
        try
        {
            // Recorded carry-forward (T220 review, still only half-closed): BoothLogEntryRequest and
            // BoothLogAppendRequest duplicate these same 8 fields positionally — this mapping exists
            // only because the two types are two records, not one. Not merged now (out of this task's
            // scope); merge candidate is passing BoothLogEntryRequest straight onto the channel and
            // dropping BoothLogAppendRequest as a separate type.
            await store.AppendAsync(
                new BoothLogAppendRequest(
                    request.Kind, request.Summary, request.PersonaId, request.Artist, request.Pick,
                    request.MediaId, request.SegmentKind, request.ShowId),
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Booth log write failed for {Kind} — entry dropped, playout unaffected", request.Kind);
        }
    }
}
