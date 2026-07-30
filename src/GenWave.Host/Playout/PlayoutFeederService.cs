using System.Diagnostics;
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

    /// <summary>
    /// A refill that held its tick longer than this gets a log line (gh-#184): the render window
    /// used to be invisible — no timing existed anywhere in the tick — which is how a 40s stall
    /// hid in plain sight. 5s clears every socket-only refill and flags any render-bound one.
    /// </summary>
    static readonly TimeSpan SlowRefillLogThreshold = TimeSpan.FromSeconds(5);

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
        // Shutdown can reach this more than once (supervisor stop + host teardown) — gh-#156:
        // when the first call's WaitAsync outlived ShutdownTimeout, its finally had disposed the
        // source but left the field set, and the re-entry cancelled a disposed CTS. Claim the
        // fields up front so every call after the first is a no-op.
        var stopCts = Interlocked.Exchange(ref cts, null);
        var running = Interlocked.Exchange(ref executeTask, null);
        if (stopCts is null || running is null) return;
        await stopCts.CancelAsync();
        try
        {
            await running.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // expected: either our cancellation or the host shutdown token
        }
        finally
        {
            stopCts.Dispose();
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
                var observed = await feeder.ObserveAsync(ct);

                // gh-#184: publish BEFORE the refill. The refill lawfully blocks for a whole
                // LLM+TTS render window (30s budget per patter segment, serialized LLM calls),
                // and publishing after it held the fresh snapshot hostage — the UI kept serving
                // an already-finished patter for 30-60s at every track start (measured on demo).
                PublishSnapshot();

                if (observed)
                {
                    var refillStarted = Stopwatch.GetTimestamp();
                    await feeder.RefillAsync(ct);
                    var refillElapsed = Stopwatch.GetElapsedTime(refillStarted);
                    if (refillElapsed >= SlowRefillLogThreshold)
                    {
                        log.LogInformation(
                            "Feeder refill held the tick for {RefillSeconds:F1}s for station {StationId} "
                            + "({StationName}) — LLM+TTS render window; on-air snapshot was already published",
                            refillElapsed.TotalSeconds, stationId, identityProvider.Current.Name);
                    }
                }
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
            IsDrain: !onAir.IsReal,
            ArtworkUrl: onAir.ArtworkUrl,
            DjName: onAir.DjName);

        nowPlaying.Update(stationId, snapshot);
    }
}
