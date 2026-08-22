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
    /// <summary>
    /// The feeder's tick interval (PRD §10 feed poll interval). Internal, not private — SPEC
    /// F142/PLAN T327's <c>BoundaryCadenceCovenantPostConfigure</c> (<c>GenWave.Host.Options</c>)
    /// needs the SAME value as its worst-case feeder pull gap. <c>Program.cs</c>, the composition
    /// root, reads this field and passes it into that type's constructor — neither namespace reaches
    /// into the other directly (gh-#445's namespace-cycle fitness law: <c>Playout</c> already
    /// depends on <c>Options</c> the other way, so the reverse would cycle).
    /// </summary>
    internal static readonly TimeSpan PullInterval = TimeSpan.FromSeconds(3);

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
    readonly OnAirRenderGate? onAirRenderGate;

    CancellationTokenSource? cts;
    Task? executeTask;

    // The last push-loss signal already warned about (gh-#612) — the WARN fires on CHANGE, not on
    // presence, so a continuing episode re-warns once per replan cycle (the pending count grew)
    // instead of once per 3s tick. Cleared with the signal itself when a real track reaches air.
    PushLossSignal? lastWarnedPushLoss;

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
    /// <param name="onAirRenderGate">
    /// Optional (PLAN T286 review F1) — bracketed around <see cref="PlayoutFeeder.RefillAsync"/>
    /// below, marking the on-air LLM+TTS render window in flight so <c>CrosstalkStockWorker</c> never
    /// competes with it for CPU (see <see cref="OnAirRenderGate"/>'s own remarks).
    /// </param>
    public PlayoutFeederService(
        Station station,
        PlayoutFeeder feeder,
        IStationIdentityProvider identityProvider,
        ILogger<PlayoutFeederService> log,
        NowPlayingService? nowPlaying = null,
        OnAirRenderGate? onAirRenderGate = null)
    {
        this.feeder = feeder;
        this.identityProvider = identityProvider;
        this.log = log;
        this.nowPlaying = nowPlaying;
        this.onAirRenderGate = onAirRenderGate;
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

        using var timer = new PeriodicTimer(PullInterval);
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

                // gh-#612: the feeder is pure and holds no logger, so ITS "pushed but never aired"
                // diagnostic becomes a log line HERE — before the refill, so the warning never
                // waits behind a render window either.
                WarnOnPushLoss();

                if (observed)
                {
                    var refillStarted = Stopwatch.GetTimestamp();
                    onAirRenderGate?.Enter();
                    try
                    {
                        await feeder.RefillAsync(ct);
                    }
                    finally
                    {
                        onAirRenderGate?.Exit();
                    }
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

    /// <summary>
    /// Logs the feeder's push-loss diagnostic (gh-#612) when it changes: pushes this feeder believed
    /// succeeded never reached air and the safe rotation is covering — the silent-failure signature
    /// (engine-side resolution death after a success-shaped RID reply) that ran unlogged for seven
    /// days in the gh-#610 incident. WARN, not error: never-silent (F1.3) held and the feeder is
    /// already retrying — this line exists so an operator (and the log sweep) can SEE it happening.
    /// </summary>
    void WarnOnPushLoss()
    {
        var loss = feeder.PushLoss;
        if (loss == lastWarnedPushLoss) return;
        lastWarnedPushLoss = loss;
        if (loss is null) return;   // the episode ended — a real track reached air; nothing to say

        log.LogWarning(
            "Pushed chain never reached air for station {StationId} ({StationName}) — safe rotation "
            + "is covering while {PendingCount} push(es) remain unproven (oldest: {MediaId} '{Title}' "
            + "by {Artist}). A push likely died at engine-side resolution; check the engine log for "
            + "'Nonexistent file or ill-formed URI' (gh-#612).",
            stationId, identityProvider.Current.Name,
            loss.PendingCount, loss.OldestPendingId, loss.Title, loss.Artist);
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
