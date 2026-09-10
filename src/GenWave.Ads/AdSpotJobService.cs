namespace GenWave.Ads;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Tts;

/// <summary>
/// The station-wide preview/write job runner (SPEC F174.2, F174.3; STORY-422, STORY-423; PLAN T441)
/// behind <c>POST /api/ads/{id}/write</c> and <c>DELETE /api/ads/{id}/job</c> — a FIFO
/// <see cref="Channel{T}"/> of <see cref="AdsOptions.JobQueueCapacity"/>, one job running at a time
/// station-wide (<see cref="Channel{T}"/>'s own <c>SingleReader</c> contract, not a lock: only
/// <see cref="ExecuteAsync"/>'s single consumer loop ever calls <see cref="RunOneAsync"/>), waiting on
/// <see cref="IOnAirRenderSignal.InFlight"/> before it ever touches an LLM or TTS endpoint the on-air
/// boundary itself might also need.
///
/// <para>
/// <b>Enqueue stamps the row before the queue, not before a run (SPEC F174.2, PLAN T441 ruling).</b>
/// <see cref="TryEnqueueAsync"/> calls <see cref="IAdSpotStore.StampJobAsync"/> FIRST, so a row already
/// carries <c>job_kind</c>/<c>job_started_at</c> the instant it is accepted — including while it still
/// sits queued behind another job, never only once <see cref="RunOneAsync"/> actually dequeues it. What
/// stays true station-wide is narrower: at most one job EXECUTES at a time (this class's own single
/// consumer), not merely that at most one row is stamped — a queued second write's own row is stamped
/// too, the guard <see cref="AdSpotJobEnqueueResult.Busy"/> stands on.
/// </para>
///
/// <para>
/// <b>A process restart orphans a stamped row (stated limitation, PLAN T441 ruling).</b> This queue
/// lives in memory only — every <see cref="ConcurrentDictionary{TKey,TValue}"/> entry and every queued
/// <see cref="Channel{T}"/> item is gone the instant the process exits, but a row's own
/// <c>job_kind</c>/<c>job_started_at</c> stamp is a database write that survives. This task does not
/// solve that gap: an operator's own <c>DELETE /api/ads/{id}/job</c> is the recovery path (it clears
/// the stamp unconditionally, whether or not this process still remembers the job at all).
/// </para>
///
/// <para>
/// <b>Two layers keep the consumer loop alive (PLAN T441 ruling).</b> <see cref="RunOneAsync"/>'s own
/// catch-all handles everything that happens once a job's own token lookup succeeds — every statement
/// from its own "starting" log line onward, including its own cleanup call to
/// <see cref="IAdSpotStore.ClearJobAsync"/>, which clears the stamp with a sanitised error — and never
/// rethrows. <see cref="ExecuteAsync"/>'s own catch around the call to <see cref="RunOneAsync"/> handles
/// everything else: today that is only the token lookup itself (a <see cref="CancelAsync"/> landing in
/// that narrow window before <see cref="RunOneAsync"/>'s own try block ever starts), and it also covers
/// any later code a future change places ahead of that try block. It logs the failure and reads the next
/// queued job; it never touches a row's stamp, since a failure that early carries no job context to
/// clear. Only host shutdown ends the loop — no single job, however it fails, can.
/// </para>
/// </summary>
public sealed class AdSpotJobService : BackgroundService
{
    /// <summary>The literal prefix <see cref="RunOneAsync"/>'s own "a job is starting" line always logs
    /// with — shared with the two Ads.Tests <see cref="ILogger{TCategoryName}"/> doubles that key on it
    /// (<c>GatedStartLogger</c>, <c>ThrowOnceStartLogger</c>) so a wording change can never silently stop
    /// matching what those specs actually watch for.</summary>
    internal const string JobStartingLogPrefix = "Ad spot job starting ";

    /// <summary>How often a job re-checks the on-air signal while waiting for the station to clear —
    /// deliberately faster than <c>AdSpotWorker.RenderWatchdogInterval</c>'s 3s: a preview/write job is
    /// an operator waiting on a button in the admin UI right now, not a background tick, so it
    /// re-checks on a tighter cadence.</summary>
    static readonly TimeSpan StationPollInterval = TimeSpan.FromMilliseconds(250);

    readonly IAdSpotStore spotStore;
    readonly ISponsorStore sponsorStore;
    readonly AdScriptWriter scriptWriter;
    readonly AdRenderService renderService;
    readonly AdSpotStamper stamper;
    readonly IPatterDurationEstimator durationEstimator;
    readonly IAudiencePostureProvider audiencePosture;
    readonly IOnAirRenderSignal onAirRenderSignal;
    readonly IOptionsMonitor<AdsOptions> adsOptions;
    readonly IOptionsMonitor<LlmOptions> llmOptions;
    readonly IConfiguration configuration;
    readonly TimeProvider timeProvider;
    readonly ILogger<AdSpotJobService> logger;

    /// <summary>Read ONCE, at construction, from <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>
    /// — a <see cref="Channel{T}"/>'s own bound is fixed the moment it is created, so a live
    /// <c>Ads:JobQueueCapacity</c> edit takes effect only on the next process start, never mid-run.
    /// This is the one field this class needs an explicit constructor for: every sibling
    /// <see cref="BackgroundService"/> in this project (<c>AdSpotWorker</c>,
    /// <c>AdSpotLifecycleGuardianService</c>) reads its own live options on every tick instead and so
    /// stays a bare primary constructor.</summary>
    readonly int jobQueueCapacity;

    /// <summary>The FIFO queue itself — <c>SingleReader: true</c> (only <see cref="ExecuteAsync"/>'s
    /// own loop ever reads) and <c>FullMode: Wait</c>, but every producer calls
    /// <see cref="ChannelWriter{T}.TryWrite"/>, never the blocking <c>WriteAsync</c>: an HTTP request
    /// thread must never block on a bounded channel that may not drain before the response is
    /// due — a full queue is instead an immediate <see cref="AdSpotJobEnqueueResult.QueueFull"/>.</summary>
    readonly Channel<(long SpotId, string Kind)> channel;

    public AdSpotJobService(
        IAdSpotStore spotStore,
        ISponsorStore sponsorStore,
        AdScriptWriter scriptWriter,
        AdRenderService renderService,
        AdSpotStamper stamper,
        IPatterDurationEstimator durationEstimator,
        IAudiencePostureProvider audiencePosture,
        IOnAirRenderSignal onAirRenderSignal,
        IOptionsMonitor<AdsOptions> adsOptions,
        IOptionsMonitor<LlmOptions> llmOptions,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<AdSpotJobService> logger)
    {
        this.spotStore = spotStore;
        this.sponsorStore = sponsorStore;
        this.scriptWriter = scriptWriter;
        this.renderService = renderService;
        this.stamper = stamper;
        this.durationEstimator = durationEstimator;
        this.audiencePosture = audiencePosture;
        this.onAirRenderSignal = onAirRenderSignal;
        this.adsOptions = adsOptions;
        this.llmOptions = llmOptions;
        this.configuration = configuration;
        this.timeProvider = timeProvider;
        this.logger = logger;

        jobQueueCapacity = adsOptions.CurrentValue.JobQueueCapacity;
        channel = Channel.CreateBounded<(long SpotId, string Kind)>(
            new BoundedChannelOptions(jobQueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    /// <summary>One <see cref="CancellationTokenSource"/> per currently queued-or-running job, keyed by
    /// spot id — created in <see cref="TryEnqueueAsync"/>, cancelled and removed by
    /// <see cref="CancelAsync"/> (which reaches a still-queued job too, not only a running one), and
    /// always removed by <see cref="RunOneAsync"/>'s own cleanup once a job finishes on its own.</summary>
    readonly ConcurrentDictionary<long, CancellationTokenSource> jobTokens = new();

    /// <summary>0 when no job is waiting on <see cref="onAirRenderSignal"/> right now, else the spot id
    /// the single consumer loop is currently holding — read/written only via <see cref="Interlocked"/>
    /// (spot ids are Postgres <c>bigserial</c>, so 0 is never a real id).</summary>
    long waitingSpotId;

    /// <summary>
    /// Claims <paramref name="spotId"/> for a <paramref name="kind"/> job and enqueues it (SPEC F174.2,
    /// F174.3; STORY-422 AC1-AC3, STORY-423 AC1/AC3). <see cref="IAdSpotStore.StampJobAsync"/> runs
    /// FIRST — a row already claimed by another job answers <see cref="AdSpotJobEnqueueResult.Busy"/>
    /// before this ever touches the queue, and an unknown id answers
    /// <see cref="AdSpotJobEnqueueResult.NotFound"/>. A full queue undoes the stamp via
    /// <see cref="IAdSpotStore.ClearJobAsync"/> before answering
    /// <see cref="AdSpotJobEnqueueResult.QueueFull"/>, so a refused enqueue leaves the row exactly as
    /// it was found.
    /// </summary>
    public async Task<AdSpotJobEnqueueResult> TryEnqueueAsync(long spotId, string kind, CancellationToken ct)
    {
        var stamped = await spotStore.StampJobAsync(spotId, kind, ct);
        switch (stamped.Result)
        {
            case AdSpotJobStampResult.Busy:
                return AdSpotJobEnqueueResult.Busy;
            case AdSpotJobStampResult.NotFound:
                return AdSpotJobEnqueueResult.NotFound;
        }

        var ownCts = new CancellationTokenSource();
        jobTokens[spotId] = ownCts;

        if (channel.Writer.TryWrite((spotId, kind)))
            return AdSpotJobEnqueueResult.Accepted;

        // The queue was already at jobQueueCapacity — undo both the stamp and the token this call
        // just registered, so a refused enqueue leaves nothing behind for a later, successful one to
        // collide with.
        jobTokens.TryRemove(spotId, out _);
        ownCts.Dispose();
        await spotStore.ClearJobAsync(spotId, null, ct);
        return AdSpotJobEnqueueResult.QueueFull;
    }

    /// <summary>
    /// Cancels whatever is queued or running for <paramref name="spotId"/>, then clears the row's job
    /// stamp regardless of whether anything was actually found to cancel (SPEC F174.2 — PLAN T441
    /// ruling: <c>DELETE /api/ads/{id}/job</c> is idempotent; "no job for this id" still answers 204,
    /// the same total posture <see cref="IAdSpotStore.ClearJobAsync"/> already holds). Cancelling the
    /// id's own <see cref="CancellationTokenSource"/> reaches a job that is still QUEUED too, not only
    /// one already running — this method removes the id's own entry from <see cref="jobTokens"/> before
    /// cancelling it, so see <see cref="RunOneAsync"/>'s own remarks for how a queued job that finds no
    /// token registered at all skips outright rather than ever starting.
    /// </summary>
    public async Task CancelAsync(long spotId, CancellationToken ct)
    {
        if (jobTokens.TryRemove(spotId, out var ownCts))
        {
            try
            {
                // The job this token belongs to may finish and dispose its own source concurrently,
                // between this removal and the cancel call below — a disposed source throwing here is
                // the job having already stopped on its own, not a failure to report. This method
                // never disposes ownCts itself (PLAN T441 ruling): a job already running has its own
                // source disposed by RunOneAsync's own finally once that job's try block unwinds, but a
                // job cancelled while it is still queued never enters that try at all, so its source is
                // disposed by no one — it never acquired a linked registration (CreateLinkedTokenSource
                // sits inside that try) and a cancelled source with no registration and no CancelAfter
                // timer holds nothing that needs disposing.
                await ownCts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        await spotStore.ClearJobAsync(spotId, null, ct);
    }

    /// <summary>
    /// Whether the job the single consumer loop is CURRENTLY holding is <paramref name="spotId"/>, and
    /// it is waiting on <see cref="onAirRenderSignal"/> right now (SPEC F174.2's own
    /// <c>job.waitingForStation</c> — there is no database column for this; it is purely this runner's
    /// own in-memory state). False for every id except the one <see cref="RunOneAsync"/> is actively
    /// waiting on, including for an id that is merely queued behind it.
    /// </summary>
    public bool IsWaitingForStation(long spotId) => Interlocked.Read(ref waitingSpotId) == spotId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ad spot job service started: queue capacity {Capacity}", jobQueueCapacity);

        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await RunOneAsync(request, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Covers anything thrown between reading a request and entering RunOneAsync's own
                    // try — the token lookup today, and any code a later change puts there (PLAN T441
                    // ruling). It logs and continues, and does not touch the row because it has no job
                    // context to clear.
                    logger.LogWarning(
                        ex, "Ad spot job {SpotId} ({Kind}) failed outside its own handler; the queue continues",
                        request.SpotId, request.Kind);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Ad spot job service stopped");
    }

    /// <summary>
    /// A job whose token is missing (<see cref="CancelAsync"/> removed it while it was still queued) is
    /// skipped outright — its stamp is already cleared by that call (STORY-422 AC6). A cancel landing
    /// after this lookup is resolved inside the job's own handler below. Otherwise waits on
    /// <see cref="onAirRenderSignal"/>, then dispatches by kind. The own <c>try</c> below starts at
    /// the "starting" log line itself (PLAN T441 ruling) — every
    /// statement in this method's own body, from that first line onward, is covered by its own
    /// catch-all, so nothing after the token lookup above can ever reach <see cref="ExecuteAsync"/>'s
    /// outer catch instead. This method's own <c>finally</c> disposes <c>ownCts</c> for every job that
    /// reaches it — a job whose token was already missing at the lookup above returns before this try
    /// ever starts, so that source is disposed by no one instead, holding no registration or timer that
    /// would need it. <see cref="CancelAsync"/> only ever cancels a token, never disposes one, so
    /// linking it below can race a concurrent cancel but never a concurrent dispose.
    /// </summary>
    async Task RunOneAsync((long SpotId, string Kind) request, CancellationToken stoppingToken)
    {
        if (!jobTokens.TryGetValue(request.SpotId, out var ownCts))
            return;

        try
        {
            logger.LogInformation(JobStartingLogPrefix + "spotId={SpotId} kind={Kind}", request.SpotId, request.Kind);

            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, ownCts.Token);
            var jobCt = runCts.Token;

            while (onAirRenderSignal.InFlight)
            {
                Interlocked.Exchange(ref waitingSpotId, request.SpotId);
                await Task.Delay(StationPollInterval, timeProvider, jobCt);
            }

            Interlocked.Exchange(ref waitingSpotId, 0);

            switch (request.Kind)
            {
                case "write":
                    await RunWriteAsync(request.SpotId, jobCt);
                    break;
                case "preview":
                    await RunPreviewAsync(request.SpotId, jobCt);
                    break;
                default:
                    // A defensive floor for any other value reaching the queue — AdsController only
                    // ever enqueues "write" or "preview" (SPEC F174.2, F174.4).
                    await spotStore.ClearJobAsync(request.SpotId, "unknown job kind", CancellationToken.None);
                    break;
            }

            logger.LogInformation("Ad spot job finished spotId={SpotId} kind={Kind}", request.SpotId, request.Kind);
        }
        catch (OperationCanceledException) when (ownCts.IsCancellationRequested)
        {
            // The job's OWN token — CancelAsync already cleared the stamp (PLAN T441 ruling); nothing
            // to write here.
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown mid-job — this class's own remarks state the restart-orphan limitation;
            // the stamp is deliberately left in place for an operator's own DELETE to clear.
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Ad spot job failed unexpectedly spotId={SpotId} kind={Kind}", request.SpotId, request.Kind);
            try
            {
                await spotStore.ClearJobAsync(request.SpotId, LogSanitize.Strip(ex.Message), CancellationToken.None);
            }
            catch (Exception clearEx)
            {
                // Stated in this class's own remarks: the consumer loop never dies from a single job's
                // own failure, including a failure in this very cleanup call.
                logger.LogWarning(clearEx, "Ad spot job could not clear its own stamp spotId={SpotId}", request.SpotId);
            }
        }
        finally
        {
            Interlocked.Exchange(ref waitingSpotId, 0);

            // Always dispose the instance THIS call registered, whether or not it is still the entry
            // under request.SpotId in jobTokens — a requeue for the same id (STORY-422's own
            // instance-keyed skip path) may already have replaced it with a newer job's own token
            // before this finally runs, and TryRemove(KeyValuePair) then correctly fails without
            // touching that newer entry. Either way ownCts is THIS call's own instance and nothing
            // else still references it (PLAN T441 ruling: this finally is disposal's sole owner,
            // CancelAsync never disposes), so disposing it unconditionally here is always safe and
            // never leaks a displaced source that TryRemove alone failed to evict.
            jobTokens.TryRemove(new KeyValuePair<long, CancellationTokenSource>(request.SpotId, ownCts));
            ownCts.Dispose();
        }
    }

    /// <summary>
    /// STORY-423 — runs <see cref="AdScriptWriter"/> against <paramref name="spotId"/>'s current
    /// <see cref="AdSpot.Brief"/> and sponsor, landing <see cref="AdSpot.Script"/> on success or the
    /// row's <c>job_error</c> on failure — always through <see cref="IAdSpotStore.ClearJobAsync"/>,
    /// the one place <c>job_kind</c> is cleared regardless of outcome (SPEC F174.3).
    /// </summary>
    async Task RunWriteAsync(long spotId, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(spotId, ct);
        if (spot is null)
        {
            await spotStore.ClearJobAsync(spotId, null, CancellationToken.None);
            return;
        }

        var sponsor = await sponsorStore.GetAsync(spot.SponsorId, ct);
        if (sponsor is null)
        {
            await spotStore.ClearJobAsync(spotId, "the sponsor no longer exists", CancellationToken.None);
            return;
        }

        // tone: null — a spot has no tone column of its own; AdScriptRequests.Build still carries
        // sponsor.Tone through as HouseTone regardless (the house-tone fallback, PLAN T441 ruling).
        var (writeRequest, validate) = AdScriptRequests.Build(
            sponsor, spot.Brief, tone: null, spot.SpotSeconds, audiencePosture.Current,
            llmOptions.CurrentValue.MaxCopyChars, adsOptions.CurrentValue.DurationToleranceRatio, durationEstimator);

        var result = await scriptWriter.WriteAsync(writeRequest, validate, ct);

        switch (result)
        {
            case AdScriptWriteResult.Success success:
                var edit = new AdSpotEdit(null, null, null, success.Script, null, null, null);
                var outcome = await spotStore.UpdateAsync(spotId, edit, spot.Version, ct);

                // Updated lands the script cleanly; every other outcome (a concurrent edit landed
                // first, or the row vanished mid-write) reports the SAME "changed while working"
                // message — either way the operator's own next move is to re-read the row.
                await spotStore.ClearJobAsync(
                    spotId,
                    outcome.Result == AdSpotWriteResult.Updated ? null : "the spot changed while the writer was working",
                    CancellationToken.None);
                break;

            case AdScriptWriteResult.Failed failed:
                // Every Reason this writer can produce is already newline-free — the validator-refusal path via
                // AdScriptWriter.BoundReason, the rest by construction — so this Strip re-asserts that at the row
                // boundary and no fixture can vary it through the writer.
                await spotStore.ClearJobAsync(spotId, LogSanitize.Strip(failed.Reason), CancellationToken.None);
                break;
        }
    }

    /// <summary>
    /// SPEC F174.4; STORY-424; PLAN T442 — the SAME shape as <see cref="RunWriteAsync"/> (row lookup,
    /// sponsor lookup, always through <see cref="IAdSpotStore.ClearJobAsync"/> regardless of outcome),
    /// with the cast/bed stamp (<see cref="AdSpotStamper"/>, the SAME stamping pair
    /// <c>AdSpotWorker</c>'s own render pass uses) run first: <see cref="AdRenderService.RenderPreviewAsync"/>
    /// and <see cref="AdPreviewKey.Compute"/> both need a fully-stamped row (an ungenerated cast/bed
    /// would render a DIFFERENT file than the one an operator ends up seeing once the row's own eventual
    /// write-render stamps it) — the staleness key this job stores is therefore computed from the
    /// STAMPED row, not the one this method first read.
    /// </summary>
    async Task RunPreviewAsync(long spotId, CancellationToken ct)
    {
        var spot = await spotStore.GetByIdAsync(spotId, ct);
        if (spot is null)
        {
            await spotStore.ClearJobAsync(spotId, null, CancellationToken.None);
            return;
        }

        var sponsor = await sponsorStore.GetAsync(spot.SponsorId, ct);
        if (sponsor is null)
        {
            await spotStore.ClearJobAsync(spotId, "the sponsor no longer exists", CancellationToken.None);
            return;
        }

        var liveSettings = AdLiveSettingsReader.Read(configuration);
        var castStamped = await stamper.StampCastIfNeededAsync(spot, liveSettings, ct);
        var stamped = await stamper.StampBedIfNeededAsync(castStamped, ct);

        var outcome = await renderService.RenderPreviewAsync(stamped, sponsor, liveSettings, ct);
        switch (outcome)
        {
            case AdPreviewOutcome.Rendered rendered:
                await spotStore.StampPreviewAsync(spotId, rendered.Path, rendered.Key, CancellationToken.None);
                await spotStore.ClearJobAsync(spotId, null, CancellationToken.None);
                break;
            case AdPreviewOutcome.Failed failed:
                await spotStore.ClearJobAsync(spotId, LogSanitize.Strip(failed.Reason), CancellationToken.None);
                break;
        }
    }
}
