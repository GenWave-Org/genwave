using Microsoft.Extensions.Options;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Host.Playout;
using GenWave.Orchestration;
using GenWave.Tts;

namespace GenWave.Host.Crosstalk;

/// <summary>
/// The Host's thin timer shell for two-voice banter (SPEC F127.7, STORY-328, PLAN T286) — the SAME
/// "feeder pattern" precedent <c>PlayoutFeederService</c>/<c>ContextTickerService</c> already
/// establish: a periodic tick, a try/catch so one bad tick never kills the loop, and every actual
/// DECISION delegated to a framework-free collaborator. Here that is <see cref="CrosstalkPlanner"/>
/// (GenWave.Orchestration, casting/stock/staleness) and <see cref="CrosstalkBreakWindow"/> (the "never
/// inside a break window" gate) — this class only sequences calls to them plus the two Tts renderers
/// (<see cref="CrosstalkScriptWriter"/>, <see cref="CrosstalkAssembler"/>) and owns the one piece of
/// state that is genuinely a Host I/O concern: the startup purge of orphaned assets below.
///
/// <para>
/// <b>Only ever stocks the show that is CURRENTLY on air (build-time decision, T286).</b>
/// <see cref="CrosstalkPlanner.TryCastAsync"/> needs a concrete host block to cast against; the
/// natural, already-cheap answer to "which block" is whatever <see cref="CachingScheduleResolver.TryGetCurrent"/>
/// already resolves for every other on-air-aware Host consumer (<c>OnAirPersonaAccessor</c>,
/// <c>ScheduleEnvelopeProvider</c>) — no new schedule-scanning machinery, and it composes correctly
/// with SPEC F127.7's own "opportunistic, off the clock" framing: an exchange generated DURING a
/// show's own airtime has the rest of that same block to be vended mid-block (PLAN T287) before the
/// show ends, and a schedule edit that moves the neighbor is caught by <c>TryVend</c>'s own staleness
/// check regardless of when this worker happened to generate it.
/// </para>
///
/// <para>
/// <b>The break-window gate — cancel-in-flight, not a shared completion gate with <see cref="CrosstalkScriptWriter"/>
/// counterpart <c>LlmCopyWriter</c> (build-time decision, T286; see PLAN T286's own recorded review
/// requirement).</b> A shared single-flight transport (LlmCopyWriter's own SPEC F69.6 seam,
/// queue-wait/<c>LlmGateBusyException</c> semantics, golden-pinned request bytes) was rejected: this
/// worker owning a <see cref="CancellationTokenSource"/> and watching <see cref="CrosstalkBreakWindow"/>
/// against <c>NowPlayingService</c>'s own already-published snapshot touches NONE of that
/// byte-sensitive machinery, needs no new abstraction on the <see cref="CrosstalkScriptWriter"/> side
/// (both <see cref="CrosstalkScriptWriter.WriteExchangeAsync"/> and
/// <see cref="CrosstalkAssembler.AssembleAsync"/> already thread a <see cref="CancellationToken"/> all
/// the way to the HttpClient call and the ffmpeg process respectively — genuine cancellation, not a
/// cooperative flag), and the assembler's own exception-path cleanup (its class remarks: "ANY exception
/// past this point leaves neither the per-line renders nor a partially-written mixed asset behind")
/// already discards whatever a cancelled-mid-flight attempt leaves on disk — nothing here needs to
/// duplicate that cleanup. The gate itself never reaches Orchestration's boundary/handoff machinery
/// (see <see cref="CrosstalkBreakWindow"/>'s own remarks for why that would be new coupling the F124
/// ladder already rejected once for an identical case) — it reads
/// <see cref="CachingScheduleResolver.TryGetCurrent"/>, <c>NowPlayingService</c>, and (PLAN T286
/// review F1) <see cref="OnAirRenderGate"/>, all already Host-shell-observable with zero extra I/O.
/// </para>
///
/// <para>
/// <b>PLAN T286 review F1 — the fence widened to the real render timing.</b> The original single
/// end-of-item margin closed exactly when the on-air render actually STARTS (a fresh
/// <c>NowPlayingSnapshot</c> publishes, then <c>PlayoutFeeder.RefillAsync</c> fires — gh-#184) and
/// stood open for the idle middle of a long track. <see cref="CrosstalkBreakWindow.IsOpen"/> now
/// gates on that render's REAL in-flight signal (<see cref="OnAirRenderGate"/>, set by
/// <c>PlayoutFeederService</c> around its own <c>RefillAsync</c> call) OR either of two time-based
/// predictions (after-transition, imminent-transition) — see that type's own remarks for why the
/// real signal was chosen over re-reading <c>LlmCopyWriter</c>'s own SPEC F69.6 single-flight gate.
/// </para>
///
/// <para>
/// <b>PLAN T286 review F4 — a per-show cooldown after a discard.</b> <see cref="cooldownUntil"/>
/// tracks, per show slug, the earliest time this worker will attempt that show again after its
/// generation was discarded (not cancelled by a break window — that is retried the very next tick,
/// off-window, and never counts against a show) — see <see cref="TickOnceAsync"/>'s own remarks for
/// where it is read and written.
/// </para>
///
/// <para>
/// <b>Startup purge (the T284/T285-recorded rider for T286).</b> Crosstalk's stock is deliberately unpersisted
/// (SPEC F127.7 — "the stock survives nothing") but <see cref="CrosstalkAssembler.AssembleAsync"/>
/// writes its mixed asset to disk BEFORE this worker ever offers it to <see cref="CrosstalkPlanner.Stock"/>;
/// a process crash/restart between that write and the offer — or between a successful <c>Stock</c> and
/// this worker's own next boot — orphans the file forever (nothing else in the system ever sweeps
/// <c>crosstalk/</c>, by design — <see cref="CrosstalkAssembler"/>'s own remarks: "crosstalk/ has NO
/// sweeper"). Purging the whole directory once, before the very first tick, is the one place this
/// worker owns that responsibility — see <see cref="PurgeStaleAssets"/>.
/// </para>
/// </summary>
public sealed class CrosstalkStockWorker(
    CrosstalkPlanner planner,
    CrosstalkScriptWriter scriptWriter,
    CrosstalkAssembler assembler,
    CachingScheduleResolver scheduleResolver,
    NowPlayingService nowPlaying,
    IStationIdentityProvider identityProvider,
    IStationClockProvider stationClock,
    IOptionsMonitor<TtsOptions> ttsOptions,
    OnAirRenderGate onAirRenderGate,
    ILogger<CrosstalkStockWorker> log,
    TimeProvider timeProvider) : BackgroundService
{
    /// <summary>Outer tick cadence — deliberately much coarser than the 3s feeder tick this is
    /// opportunistic, off-clock work (SPEC F127.7): frequent enough that a freshly-opened stock slot
    /// fills reasonably soon, infrequent enough that a no-op tick (the common case — most ticks find
    /// stock already full or a break window open) costs nothing worth tuning away.</summary>
    static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);

    /// <summary>How often an in-flight generation re-checks <see cref="CrosstalkBreakWindow"/> against
    /// the live <c>NowPlayingService</c> snapshot — the SAME order of magnitude as the feeder's own 3s
    /// tick (the fastest this state can actually change), so a break window opening mid-flight is
    /// caught within a few seconds, never left to run the FULL remaining generation+render duration.</summary>
    static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// PLAN T286 review F4: how long a show sits out after a DISCARD (the writer skipped, or the
    /// assembler rejected — never a break-window cancellation, which is retried off-window the very
    /// next tick regardless) before this worker attempts it again. At T283's paper-audition accept
    /// rate (~17%), an unthrottled below-target show would otherwise mean near-continuous LLM traffic
    /// every <see cref="TickInterval"/> — this worker is the one place SPEC F127.7's own "opportunistic,
    /// off the clock" framing is actually paced. 75s: comfortably longer than one whole TickInterval
    /// (one discard skips more than one retry, so a persistently bad show cannot re-attempt every
    /// single tick) while staying short enough that a merely unlucky roll self-heals inside the same
    /// on-air block it was attempted in, well under this show's own multi-hour airtime.
    /// </summary>
    static readonly TimeSpan DiscardCooldown = TimeSpan.FromSeconds(75);

    /// <summary>
    /// Per-show-slug "do not attempt again before this instant" (PLAN T286 review F4) — mutated only
    /// from <see cref="TickOnceAsync"/>, which <see cref="BackgroundService.ExecuteAsync"/> only ever
    /// calls one tick at a time (never overlapping), so no lock is needed. Reset (removed) on the
    /// SAME show's next successful generation; naturally stale (and so ignored, never explicitly
    /// cleared) once a different show comes on air, since <see cref="DecideAttempt"/> only ever checks
    /// the CURRENTLY on-air show's own slug and <see cref="DiscardCooldown"/> is far shorter than any
    /// realistic gap before the same show returns.
    /// </summary>
    readonly Dictionary<string, DateTimeOffset> cooldownUntil = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PurgeStaleAssets(CrosstalkAssembler.ResolveCacheDir(ttsOptions.CurrentValue), log);

        log.LogInformation("Crosstalk stock worker started: every {TickIntervalSeconds}s", TickInterval.TotalSeconds);

        try
        {
            // PLAN T286 review F2: timeProvider-driven (not the bare interval overload) so a test can
            // drive this loop with a FakeTimeProvider instead of waiting on wall-clock time.
            using var timer = new PeriodicTimer(TickInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TickOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        log.LogInformation("Crosstalk stock worker stopped");
    }

    /// <summary>One tick, internal so a test can drive it directly without the real timer (mirrors
    /// <c>ContextTickerService.TickOnceAsync</c>'s own precedent). Never throws past the "caller
    /// cancelled" case — every other fault is logged and swallowed, the same "never crashes the host"
    /// posture <c>ContextTickerService</c> already documents.
    /// <para>
    /// PLAN T286 review F4: <see cref="cooldownUntil"/> is read (inside <see cref="DecideAttempt"/>)
    /// and written HERE, never inside <see cref="GenerateAndAssembleAsync"/> — that method only
    /// reports WHAT happened (<see cref="GenerationOutcome.CancelledByBreakWindow"/> vs a genuine
    /// discard), never decides pacing itself, so the decider stays the one place SPEC F127.7's
    /// "opportunistic, off the clock" cadence is actually enforced.
    /// </para>
    /// </summary>
    internal async Task TickOnceAsync(CancellationToken ct)
    {
        try
        {
            var onAir = scheduleResolver.TryGetCurrent();
            var nowPlayingSnapshot = nowPlaying.GetSnapshot(SingleStation.IdString);
            var now = timeProvider.GetUtcNow();
            var renderBudget = TimeSpan.FromSeconds(ttsOptions.CurrentValue.RenderBudgetSeconds);

            if (DecideAttempt(planner, onAir, nowPlayingSnapshot, now, onAirRenderGate.InFlight, renderBudget, cooldownUntil)
                is not { } attempt)
            {
                return;
            }

            // The FULL grid (CachingScheduleResolver.TryGetCurrentWeekSnapshot), not merely "who is on
            // now" — CrosstalkPlanner.TryCastAsync casts from cyclic adjacency (next block AND
            // previous block), which OnAirSnapshot alone cannot reconstruct. Null only before the
            // process's very first schedule resolve has ever completed (the boot window) — nothing to
            // cast against yet, retried next tick.
            if (scheduleResolver.TryGetCurrentWeekSnapshot() is not { } week)
                return;

            if (await planner.TryCastAsync(attempt.HostBlock, week, ct) is not { } cast)
                return;

            var outcome = await GenerateAndAssembleAsync(attempt, cast, ct);
            if (outcome.Assembled is not { } assembled)
            {
                // PLAN T286 review F4: only a genuine discard costs the show a cooldown — a break
                // window opening mid-flight is retried the very next tick, off-window, and must not
                // be conflated with the accept-rate problem the cooldown exists to pace.
                //
                // The base time is read HERE, at the write, not the tick-start `now` above (PLAN
                // T286 review finding) — generation (the script writer's HTTP round trip, the
                // assembler's per-line synth + ffmpeg mix) can itself run for seconds, and basing the
                // cooldown on tick-start would silently shrink the effective cooldown by however long
                // THIS discarded attempt took, undercutting the very pacing DiscardCooldown's own
                // remarks promise.
                if (!outcome.CancelledByBreakWindow)
                    cooldownUntil[attempt.ShowSlug] = timeProvider.GetUtcNow() + DiscardCooldown;

                return;
            }

            cooldownUntil.Remove(attempt.ShowSlug);

            var exchange = new StockedCrosstalkExchange(
                attempt.ShowSlug, cast.Cast, assembled.Path, assembled.Loudness, assembled.Cue, assembled.DurationMs);

            if (planner.Stock(exchange))
            {
                log.LogInformation(
                    "Crosstalk exchange stocked for '{Show}' (host={HostPersonaId}, neighbor={NeighborPersonaId})",
                    LogSanitize.Strip(attempt.ShowSlug), cast.Cast.HostPersonaId, cast.Cast.NeighborPersonaId);
            }
            else
            {
                // Defensive only — this worker is the sole caller of Stock() (BackgroundService.ExecuteAsync
                // runs ticks sequentially, never overlapping), so the target cannot have filled behind
                // this same tick's own earlier NeedsStock read. Never leak the freshly-assembled asset
                // regardless.
                DeleteAssetBestEffort(assembled.Path);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancellation (shutdown) — must propagate to stop the loop
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Crosstalk stock tick failed; continuing on the next tick");
        }
    }

    /// <summary>
    /// Runs <see cref="CrosstalkScriptWriter.WriteExchangeAsync"/> then
    /// <see cref="CrosstalkAssembler.AssembleAsync"/> under a worker-owned
    /// <see cref="CancellationTokenSource"/>, watched concurrently by <see cref="WatchBreakWindowAsync"/>
    /// (this class's own remarks: cancel-in-flight, not a shared completion gate). Every non-air
    /// outcome — a script/render discard, the ceiling gate, OR a break window opening mid-flight —
    /// answers a <see langword="null"/> <see cref="GenerationOutcome.Assembled"/>, so
    /// <see cref="TickOnceAsync"/>'s caller never has to unpick WHICH sub-call produced it (each
    /// already logs its own reason at the point it happens) — only WHETHER it was the break window
    /// (<see cref="GenerationOutcome.CancelledByBreakWindow"/>), which is all the cooldown decision
    /// (PLAN T286 review F4) needs to tell a discard apart from an abandoned-but-blameless attempt.
    /// </summary>
    async Task<GenerationOutcome> GenerateAndAssembleAsync(
        CrosstalkStockAttempt attempt, CrosstalkCastResult cast, CancellationToken stoppingToken)
    {
        using var workCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var watchdog = WatchBreakWindowAsync(workCts, watchdogCts.Token);

        try
        {
            var request = new CrosstalkExchangeRequest(
                cast.HostCard, cast.NeighborCard, identityProvider.Current.Name, attempt.Show.Name,
                Daypart: null, stationClock.LocalNow);

            var writeResult = await scriptWriter.WriteExchangeAsync(request, workCts.Token);
            if (writeResult is not CrosstalkWriteResult.Accepted accepted)
                return GenerationOutcome.Discarded;

            var assembleResult = await assembler.AssembleAsync(
                new CrosstalkAssemblyRequest(accepted.Script, cast.HostCard, cast.NeighborCard), workCts.Token);

            return assembleResult is CrosstalkAssemblyResult.Assembled assembled
                ? new GenerationOutcome(assembled, CancelledByBreakWindow: false)
                : GenerationOutcome.Discarded;
        }
        catch (OperationCanceledException) when (workCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // Our OWN watchdog fired (SPEC F127.7) — a break window opened mid-flight, not a host
            // shutdown. The assembler's own exception-path cleanup (its class remarks) has already
            // discarded any per-line renders/partial mix this attempt produced; nothing left to clean
            // here. Retried off-window on a later tick.
            log.LogInformation(
                "Crosstalk generation for '{Show}' abandoned — a break window opened mid-flight; retrying off-window",
                LogSanitize.Strip(attempt.ShowSlug));
            return GenerationOutcome.CancelledByWindow;
        }
        finally
        {
            await watchdogCts.CancelAsync();
            await watchdog;
        }
    }

    /// <summary>Polls <see cref="CrosstalkBreakWindow"/> against the live <c>NowPlayingService</c>
    /// snapshot and <see cref="onAirRenderGate"/> every <see cref="WatchdogInterval"/>, and cancels
    /// <paramref name="workCts"/> the instant either reads open. Exits cleanly (swallows its own
    /// cancellation) once <paramref name="ct"/> fires — <see cref="GenerateAndAssembleAsync"/>'s own
    /// <c>finally</c> cancels it as soon as the generation it is watching finishes for any reason, so
    /// this loop never outlives the work it exists to interrupt.</summary>
    async Task WatchBreakWindowAsync(CancellationTokenSource workCts, CancellationToken ct)
    {
        try
        {
            // PLAN T286 review F2: timeProvider-driven — see ExecuteAsync's own outer timer remarks.
            using var timer = new PeriodicTimer(WatchdogInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(ct))
            {
                var snapshot = nowPlaying.GetSnapshot(SingleStation.IdString);
                var renderBudget = TimeSpan.FromSeconds(ttsOptions.CurrentValue.RenderBudgetSeconds);
                var isOpen = BreakWindowOpen(timeProvider.GetUtcNow(), snapshot, renderBudget, onAirRenderGate.InFlight);

                if (isOpen)
                {
                    await workCts.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal: ct fired because the generation it watches already finished (or the host is
            // shutting down) — nothing left to interrupt.
        }
    }

    /// <summary>
    /// Every fact this worker needs before attempting a generation, gathered in one place so
    /// <see cref="TickOnceAsync"/> reads as one guard clause per SPEC F127.7 rule rather than a nested
    /// pyramid — and, just as importantly, so the gate is unit-testable with none of
    /// <see cref="CrosstalkScriptWriter"/>/<see cref="CrosstalkAssembler"/>'s own network/ffmpeg
    /// dependencies (PLAN T286's own "testable without a real ollama" requirement): a plain function of
    /// its every parameter, no ambient state. <see langword="null"/> for a grid gap, an unnamed block,
    /// a disabled/already-full show (SPEC F127.8), a show still inside its own
    /// <paramref name="cooldownUntil"/> window (PLAN T286 review F4), or — <see cref="CrosstalkBreakWindow"/>'s
    /// own gate, checked LAST so an already-disqualified show never even reads the clock — a break
    /// window that is open right now.
    /// </summary>
    internal static CrosstalkStockAttempt? DecideAttempt(
        CrosstalkPlanner planner, OnAirSnapshot? onAir, NowPlayingSnapshot? nowPlaying, DateTimeOffset now,
        bool refillInFlight, TimeSpan renderBudget, IReadOnlyDictionary<string, DateTimeOffset> cooldownUntil)
    {
        if (onAir?.Segment is not { } hostBlock) return null;
        if (onAir.Show is not { Slug.Length: > 0 } show) return null;
        if (!planner.IsShowEnabled(show.Slug)) return null;
        if (!planner.NeedsStock(show.Slug)) return null;
        if (cooldownUntil.TryGetValue(show.Slug, out var coolsUntil) && now < coolsUntil) return null;

        if (BreakWindowOpen(now, nowPlaying, renderBudget, refillInFlight)) return null;

        return new CrosstalkStockAttempt(show.Slug, hostBlock, show);
    }

    /// <summary>
    /// The one place <see cref="CrosstalkBreakWindow.IsOpen"/>'s five args are actually composed —
    /// both <see cref="DecideAttempt"/> (the pre-attempt gate) and <see cref="WatchBreakWindowAsync"/>
    /// (the mid-flight watchdog) consult the SAME <see cref="EstimatedOnAirEndsAt"/>/
    /// <see cref="OnAirStartedAt"/> derivation from whatever <see cref="NowPlayingSnapshot"/> each has
    /// in hand, so this is the one seam that can drift between the two callers if left duplicated.
    /// </summary>
    static bool BreakWindowOpen(
        DateTimeOffset now, NowPlayingSnapshot? nowPlaying, TimeSpan renderBudget, bool refillInFlight) =>
        CrosstalkBreakWindow.IsOpen(
            now, EstimatedOnAirEndsAt(nowPlaying), OnAirStartedAt(nowPlaying), renderBudget, refillInFlight);

    /// <summary>The current on-air item's estimated end — <see cref="CrosstalkBreakWindow"/>'s own
    /// input — derived from <c>NowPlayingSnapshot</c>'s already-published
    /// <see cref="NowPlayingSnapshot.StartedAt"/>/<see cref="NowPlayingSnapshot.DurationMs"/> with zero
    /// extra I/O. <see langword="null"/> when no snapshot has published yet OR the airing item's
    /// duration is not (yet) known — an engine-initiated/foreign advance, or the brief window before
    /// <c>DurationRehydrator</c> patches one in — which <see cref="CrosstalkBreakWindow.IsOpen"/> itself
    /// treats as fail-closed (open).</summary>
    internal static DateTimeOffset? EstimatedOnAirEndsAt(NowPlayingSnapshot? snapshot) =>
        snapshot?.DurationMs is int durationMs ? snapshot.StartedAt + TimeSpan.FromMilliseconds(durationMs) : null;

    /// <summary>The current on-air item's own start — <see cref="CrosstalkBreakWindow"/>'s
    /// after-transition input (PLAN T286 review F1). <see langword="null"/> only when no snapshot has
    /// published yet (<see cref="NowPlayingSnapshot.StartedAt"/> itself is never nullable), which
    /// <see cref="CrosstalkBreakWindow.IsOpen"/> treats as fail-closed (open), mirroring
    /// <see cref="EstimatedOnAirEndsAt"/>'s own posture.</summary>
    internal static DateTimeOffset? OnAirStartedAt(NowPlayingSnapshot? snapshot) => snapshot?.StartedAt;

    /// <summary>
    /// Deletes every file directly under <paramref name="crosstalkDir"/> once, before this worker's
    /// very first tick (this class's own remarks — the T284/T285-recorded rider for T286). A no-op when the
    /// directory does not exist yet (a fresh install/volume — <see cref="CrosstalkAssembler.AssembleAsync"/>
    /// creates it lazily on first use). Best-effort per file, mirroring <see cref="CrosstalkPlanner"/>'s
    /// own <c>DeleteAssetBestEffort</c> precedent one seam over: a locked/undeletable file is a
    /// secondary concern, never worth failing boot over.
    /// </summary>
    internal static void PurgeStaleAssets(string crosstalkDir, ILogger log)
    {
        if (!Directory.Exists(crosstalkDir))
            return;

        string[] files;
        try
        {
            files = Directory.GetFiles(crosstalkDir);
        }
        catch (IOException ex)
        {
            log.LogWarning(ex, "Crosstalk startup purge could not list {Directory}", crosstalkDir);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.LogWarning(ex, "Crosstalk startup purge could not list {Directory}", crosstalkDir);
            return;
        }

        var deleted = 0;
        foreach (var file in files)
        {
            if (DeleteAssetBestEffort(file))
                deleted++;
        }

        if (deleted > 0)
        {
            log.LogInformation(
                "Crosstalk startup purge removed {Count} asset(s) orphaned by a previous run (SPEC F127.7 — the stock survives nothing)",
                deleted);
        }
    }

    /// <summary>Best-effort single-file delete, mirroring <see cref="CrosstalkPlanner"/>'s own
    /// <c>DeleteAssetBestEffort</c>/<see cref="CrosstalkAssembler"/>'s own <c>DeleteIfExists</c>
    /// precedent: a locked/already-gone file is a secondary concern. Returns whether a file was
    /// actually removed.</summary>
    static bool DeleteAssetBestEffort(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
