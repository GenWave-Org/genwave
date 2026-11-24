using System.Threading.Channels;
using GenWave.Core.TalkRules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Empties the check-log queue into <see cref="ITalkCheckStore"/> (SPEC F220.29). A failed write is logged and
/// the row dropped: never retried, never crashes the loop, never reaches the talk path (the
/// <see cref="BoothLogDrainService"/> posture).
/// </summary>
sealed class TalkCheckDrainService(
    ChannelReader<TalkCheckRecord> queue,
    ITalkCheckStore store,
    ILogger<TalkCheckDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var record in queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(record, stoppingToken);
        }
    }

    /// <summary>The per-row work <see cref="ExecuteAsync"/> loops over, directly testable without running the loop.</summary>
    internal async Task ProcessAsync(TalkCheckRecord record, CancellationToken ct)
    {
        try
        {
            await store.AppendAsync(record, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Talk check write failed — row dropped, the talk is unaffected");
        }
    }
}
