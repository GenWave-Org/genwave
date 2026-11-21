using GenWave.Host.Options;
using GenWave.Tts;
using Microsoft.Extensions.Options;

namespace GenWave.Host.Health;

/// <summary>
/// The BackgroundService shell around <see cref="VoiceEngineWarmUp"/> (SPEC F219.25): the boot warm-up line and the
/// follow-up reachability checks, on the shared <see cref="DependencyHealthOptions"/> cadence. A hosted service's
/// <c>ExecuteAsync</c> runs on its own task here, so a cold engine taking a minute never delays host startup or playout.
/// </summary>
sealed class VoiceEngineWarmUpService(
    VoiceEngineWarmUp warmUp,
    IOptionsMonitor<DependencyHealthOptions> options,
    ILogger<VoiceEngineWarmUpService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting before the first (possibly slow) render.
        await Task.Yield();
        logger.LogInformation("Voice engine warm-up started");
        try
        {
            await warmUp.RunAsync(CurrentCadence, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }
    }

    DependencyProbeCadence CurrentCadence() => options.CurrentValue.ToCadence();
}
