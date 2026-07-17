using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Host.Options;

namespace GenWave.Host.Playout;

/// <summary>
/// Starts the single station's <see cref="PlayoutFeederService"/> at host startup, bound to the
/// configured Liquidsoap engine. The station's identity comes from config, read live through
/// <see cref="IStationIdentityProvider"/> (SPEC F44.1, gitea-#196) rather than a DB registry — one
/// deployment broadcasts one station. The feeder chain itself (engine control → feeder → feeder
/// service) is composed in DI by <c>AddGenWavePlayout</c> (gitea-#243).
/// </summary>
sealed class PlayoutSupervisor : IHostedService
{
    readonly PlayoutFeederService feederService;
    readonly IStationIdentityProvider identityProvider;
    readonly LiquidsoapOptions liquidsoapOptions;
    readonly ILogger<PlayoutSupervisor> log;

    public PlayoutSupervisor(
        PlayoutFeederService feederService,
        IStationIdentityProvider identityProvider,
        IOptions<LiquidsoapOptions> liquidsoapOptions,
        ILogger<PlayoutSupervisor> log)
    {
        this.feederService = feederService;
        this.identityProvider = identityProvider;
        this.liquidsoapOptions = liquidsoapOptions.Value;
        this.log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await feederService.StartAsync(ct);

        // A one-time boot snapshot for this log line only — every RECURRING name use (the engine
        // push path, the feeder's tick logs) reads identityProvider live instead (SPEC F44.1, gitea-#196).
        log.LogInformation(
            "PlayoutSupervisor: started feeder for station {StationName} → engine {EngineHost}",
            identityProvider.Current.Name, liquidsoapOptions.Host);
    }

    public Task StopAsync(CancellationToken ct) => feederService.StopAsync(ct);
}
