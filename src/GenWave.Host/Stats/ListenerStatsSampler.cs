using GenWave.Core.Abstractions;
using GenWave.Core.Events;

namespace GenWave.Host.Stats;

/// <summary>
/// One listener-count sample (gh-#10, plugin-readiness P1.4): asks <see cref="IListenerStatsSource"/>
/// for the current count and publishes a <see cref="ListenerCountSampled"/> through the station
/// event sink — or publishes NOTHING when the count cannot be determined (the source's documented
/// null: Icecast down, stats unreachable). An absent sample is honest; a fabricated zero would
/// poison the time series a future analytics consumer builds from these events.
///
/// Split from <see cref="ListenerStatsPollerService"/> (the thin <c>BackgroundService</c> scheduling
/// shell) the same way <c>DependencyHealthProber</c> is split from its hosted service, so the
/// publish-or-skip behavior is spec'd directly against fakes with no hosted-service lifecycle
/// plumbing.
/// </summary>
sealed class ListenerStatsSampler(
    IListenerStatsSource source,
    IStationEventSink events,
    ILogger<ListenerStatsSampler> logger)
{
    /// <summary>
    /// Never throws (except caller cancellation): the poll loop must survive any misbehaving
    /// source — a failed sample is a skipped sample, logged at debug (an Icecast outage already
    /// warns elsewhere; a per-minute WARN here would be log spam for the same fact).
    /// </summary>
    public async Task SampleOnceAsync(CancellationToken ct)
    {
        int? count;
        try
        {
            count = await source.GetListenerCountAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // IListenerStatsSource's contract is null-never-throw; this catch is the poll loop's
            // own belt for a source that breaks that contract.
            logger.LogDebug(ex, "Listener-count sample failed — skipping this sample");
            return;
        }

        if (count is not int listeners)
        {
            logger.LogDebug("Listener count indeterminate — skipping this sample");
            return;
        }

        events.Publish(new ListenerCountSampled(listeners));
    }
}
