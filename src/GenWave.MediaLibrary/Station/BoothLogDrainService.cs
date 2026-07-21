using System.Threading.Channels;
using GenWave.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Drains <see cref="BoothLogWriter"/>'s queue and persists each entry via <see cref="IBoothLogAppender"/>
/// (SPEC F72.1, F72.3, STORY-195). Isolated from <see cref="BoothLogWriter.Publish"/>'s hot-path call
/// by design — a DB outage or latency spike here never reaches the feeder tick or a TTS render. A
/// failed append is logged and the entry dropped (never retried, never crashes the loop), per the
/// sink contract's "must never affect playout" posture.
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
            await store.AppendAsync(request.Kind, request.Summary, ct);
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
