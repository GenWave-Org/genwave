using GenWave.Host.Options;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Stats;

/// <summary>
/// The scheduling shell for <see cref="ListenerStatsSampler"/> (gh-#10, plugin-readiness P1.4):
/// one sample every <see cref="ListenerStatsOptions.PollSeconds"/>, forever, starting one full
/// interval after boot (a boot-instant sample would race Icecast's own startup and always skip).
/// Zero/negative <c>PollSeconds</c> disables the poller — logged once, then the service exits
/// cleanly. Mirrors <see cref="Health.DependencyHealthProbeService"/>'s thin-shell shape; every
/// behavioral fact lives on the sampler.
/// </summary>
sealed class ListenerStatsPollerService(
    ListenerStatsSampler sampler,
    IOptions<ListenerStatsOptions> options,
    ILogger<ListenerStatsPollerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = options.Value.PollSeconds;
        if (pollSeconds <= 0)
        {
            logger.LogInformation("Listener-count poller disabled (ListenerStats:PollSeconds={PollSeconds})", pollSeconds);
            return;
        }

        logger.LogInformation("Listener-count poller started: every {PollSeconds}s", pollSeconds);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await sampler.SampleOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Listener-count poller stopped");
    }
}
