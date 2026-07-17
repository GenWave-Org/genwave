using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;

namespace GenWave.Host.Playout;

/// <summary>
/// The timer shell around <see cref="PlayoutFeeder"/> for a single station (PRD §7). Its only job
/// is the periodic tick and a try/catch — a single bad tick (e.g. a transient socket blip) must not
/// kill the loop. All the tricky reconciliation logic lives in the pure, unit-tested feeder.
/// <para>
/// Instances are created per-station by <see cref="PlayoutSupervisor"/>; they are NOT registered
/// directly as <c>IHostedService</c> in DI. <see cref="PlayoutSupervisor"/> starts and stops them.
/// </para>
/// </summary>
sealed class PlayoutFeederService : IHostedService
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);   // PRD §10 feed poll interval

    readonly PlayoutFeeder feeder;
    readonly IStationIdentityProvider identityProvider;
    readonly ILogger<PlayoutFeederService> log;
    readonly string stationId;
    readonly NowPlayingService? nowPlaying;

    CancellationTokenSource? cts;
    Task? executeTask;

    /// <param name="station">
    /// Boot snapshot supplying the stable <see cref="Station.Id"/> this instance is keyed on.
    /// <see cref="Station.Name"/> is NOT read from here — every log line reads
    /// <paramref name="identityProvider"/> live instead (SPEC F44.1, gitea-#196), so a Station:Name
    /// settings edit is reflected in the very next log line, no api restart.
    /// </param>
    /// <param name="identityProvider">The live station-name seam every log line reads (F44.1).</param>
    /// <param name="nowPlaying">
    /// Optional now-playing sink. When provided, the snapshot is updated after every successful tick.
    /// </param>
    public PlayoutFeederService(
        Station station,
        PlayoutFeeder feeder,
        IStationIdentityProvider identityProvider,
        ILogger<PlayoutFeederService> log,
        NowPlayingService? nowPlaying = null)
    {
        this.feeder = feeder;
        this.identityProvider = identityProvider;
        this.log = log;
        this.nowPlaying = nowPlaying;
        stationId = station.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task StartAsync(CancellationToken ct)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        executeTask = ExecuteAsync(cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (cts is null || executeTask is null) return;
        await cts.CancelAsync();
        try
        {
            await executeTask.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // expected: either our cancellation or the host shutdown token
        }
        finally
        {
            cts.Dispose();
        }
    }

    async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Playout feeder started for station {StationId} ({StationName})",
            stationId, identityProvider.Current.Name);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await feeder.TickAsync(ct);
                PublishSnapshot();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;   // shutting down
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Feeder tick failed for station {StationId} ({StationName})",
                    stationId, identityProvider.Current.Name);
            }
        }

        log.LogInformation("Playout feeder stopped for station {StationId} ({StationName})",
            stationId, identityProvider.Current.Name);
    }

    void PublishSnapshot()
    {
        if (nowPlaying is null) return;
        var onAir = feeder.CurrentOnAir;
        if (onAir is null) return;   // tick returned early (engine returned null id)

        var snapshot = new NowPlayingSnapshot(
            MediaId: onAir.MediaId,
            Title: onAir.Title,
            Artist: onAir.Artist,
            GainDb: onAir.GainDb,
            StartedAt: onAir.StartedAt,
            DurationMs: onAir.DurationMs,
            IsDrain: !onAir.IsReal);

        nowPlaying.Update(stationId, snapshot);
    }
}
