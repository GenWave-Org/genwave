using System.Threading.Channels;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Playout;

/// <summary>
/// Drains <see cref="UtteranceAirEventSink"/>'s queue into <see cref="IUtteranceStore.MarkAiredAsync"/>
/// (SPEC F218.3). A store failure is logged and swallowed — playout is never affected.
/// </summary>
sealed class UtteranceAirDrainService(
    ChannelReader<UtteranceAirSignal> queue,
    IUtteranceStore store,
    ILogger<UtteranceAirDrainService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var signal in queue.ReadAllAsync(stoppingToken))
            await ProcessAsync(signal.UtteranceId, stoppingToken);
    }

    internal async Task ProcessAsync(long utteranceId, CancellationToken ct)
    {
        try
        {
            // MarkAiredAsync stamps the database's now(), not the instant the line aired: the
            // queue is drained promptly, and memory only needs "it aired", not the exact second.
            await store.MarkAiredAsync(utteranceId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Utterance air stamp failed for {UtteranceId} — playout unaffected", utteranceId);
        }
    }
}
