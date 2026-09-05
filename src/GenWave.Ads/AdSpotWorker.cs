namespace GenWave.Ads;

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Tts;

/// <summary>
/// The off-air-clock ad pipeline's own tick loop (SPEC F159.3, F159.4, F161.1; STORY-389, STORY-391;
/// PLAN T402) — the <c>CrosstalkStockWorker</c> posture applied to ads: a periodic tick, a try/catch so
/// one bad tick never kills the loop, and every actual DECISION delegated to a framework-free
/// collaborator (<see cref="AdScriptValidator"/>, <see cref="AdRenderService"/>) or a store. This class
/// only sequences four passes per tick — repair, retire, refill, render — and owns the two pieces of
/// state that are genuinely I/O-shell concerns: the render-budget/break-window cancellation plumbing
/// below.
///
/// <para>
/// <b>Four passes, one fixed order (PLAN T402's own design).</b> Every tick runs, in this order:
/// </para>
/// <list type="number">
/// <item><b>Repair</b> (PLAN T402 review block 4, RE-RULED at review F1/F4, WINDOW-CORRECTED at
/// review F6) — see <see cref="RepairReadyEligibilityAsync"/>'s own remarks for the guarded shape: a
/// Ready spot's media row is re-flipped <c>eligible=true</c> ONLY when it is currently ineligible AND
/// the spot's own ready transition is recent (inside <see cref="AdSpotRepairWindow"/>'s own window —
/// deliberately WIDER than the guardian's own grace alone, see that class's own remarks for why a
/// narrower window is mathematically unreachable at production defaults) — never an unconditional,
/// every-tick write. Running this FIRST, every tick, still means <see cref="RefillIfNeededAsync"/>'s
/// own <see cref="IAdSpotStore.CountStockGeneratedAsync"/> read never needs to change its own SQL to
/// stay honest — by the time it runs, every FRESH <see cref="AdState.Ready"/> row this pass could
/// still repair has already been. The repair MAKES the "does the stock count overcount" question
/// moot for a fresh row, rather than answering it a second, more complicated way; an OLD ineligible
/// ready row
/// (past the window) is left alone outright — see <see cref="RepairReadyEligibilityAsync"/>'s own
/// remarks for why that is operator intent, not a bug.</item>
/// <item><b>Retire</b> (SPEC F159.3) — every <see cref="AdState.Ready"/> llm/pack spot older than
/// <c>Station:Ads:RefreshDays</c> retires, its media row flipped <c>eligible=false</c> via
/// <see cref="IAuthoredCatalogWriter.SetEligibleAsync"/> (F159.3's own explicit choice, PLAN T402
/// review block 3) — never deleted (F159.1). <see cref="AdSource.Owner"/> spots never appear here at
/// all: <see cref="IAdSpotStore.ListReadyOlderThanAsync"/> excludes them at the store (SPEC F159.3's
/// exemption, STORY-389 AC5).</item>
/// <item><b>Refill</b> (SPEC F159.3, as-built rider gh-#689) — when the POST-repair, POST-retire
/// STOCK count (llm/pack spots in draft, approved, rendering, or ready — every generated spot that is
/// stock or on its way to it; never the ready shelf alone, which under <c>AutoApprove=false</c> stays
/// at zero while drafts pile up one per tick, forever) sits below
/// <c>Station:Ads:TargetCount</c>, samples one enabled brief and generates ONE new spot (never a
/// catch-up loop within a single tick — the SAME "opportunistic, paced by the tick cadence, never a
/// burst" posture <c>CrosstalkStockWorker</c> already keeps for banter). Lands <c>draft</c> or
/// <c>approved</c> per the live <c>Station:Ads:AutoApprove</c> read (SPEC F159.4, STORY-389 AC2/AC3) —
/// running BEFORE the render pass below means an auto-approved spot can render in the SAME tick it was
/// generated.</item>
/// <item><b>Render</b> (SPEC F161.1, STORY-391 AC4/AC6) — claims and renders AT MOST one
/// <see cref="AdState.Approved"/> spot, gated on <see cref="IOnAirRenderSignal.InFlight"/> and a
/// worker-owned render-budget <see cref="CancellationTokenSource"/>. See
/// <see cref="RenderOneIfDueAsync"/>'s own remarks for the full cancel-in-flight/budget shape.</item>
/// </list>
///
/// <para>
/// <b>The break-window gate covers RENDER only, never generation (T402's own scoping decision).</b>
/// SPEC F161.1's own text frames the gate around "spot renders never compete with a boundary's patter
/// budget" — STORY-391 AC4's own three facts are all about the render half too. Script generation (one
/// LLM completion, no TTS/ffmpeg) is comparatively cheap and, unlike a render, never wedges an audio
/// pipeline the on-air boundary itself depends on — gating it too would be a broader claim than either
/// the SPEC text or the pending specs actually make. A later task can widen this if the ear or the
/// metrics ever say otherwise.
/// </para>
/// </summary>
public sealed class AdSpotWorker(
    IAdSpotStore spotStore,
    IAdBriefStore briefStore,
    AdScriptWriter scriptWriter,
    AdRenderService renderService,
    IPatterDurationEstimator durationEstimator,
    IAudiencePostureProvider audiencePosture,
    IAuthoredCatalogWriter catalogWriter,
    IAdminMediaLookup adminLookup,
    IOnAirRenderSignal onAirRenderSignal,
    IOptionsMonitor<AdsOptions> adsOptions,
    IOptionsMonitor<LlmOptions> llmOptions,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<AdSpotWorker> logger) : BackgroundService
{
    /// <summary>
    /// Every generated spot targets this length (T402's own build-time choice — no signal upstream of
    /// this worker picks 15/30/60 yet; T403's owner editor is where a human eventually sets a
    /// different one). Matches <c>station.ad_spot.spot_seconds</c>'s own DB default (db/42) — the
    /// safest "no stronger signal exists yet" middle ground of the three shipped structures.
    /// </summary>
    const int GeneratedSpotSeconds = 30;

    /// <summary>How often an in-flight render re-checks <see cref="onAirRenderSignal"/> — the SAME
    /// order of magnitude as <c>CrosstalkStockWorker.WatchdogInterval</c>, for the identical reason: the
    /// fastest this state can actually change, so a break window opening mid-render is caught within a
    /// few seconds, never left to run the render's own full budget.</summary>
    static readonly TimeSpan RenderWatchdogInterval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tickInterval = TimeSpan.FromMinutes(adsOptions.CurrentValue.WorkerIntervalMinutes);
        logger.LogInformation("Ad spot worker started: every {IntervalMinutes}m", tickInterval.TotalMinutes);

        try
        {
            using var timer = new PeriodicTimer(tickInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TickOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected: host shutdown
        }

        logger.LogInformation("Ad spot worker stopped");
    }

    /// <summary>One tick, internal so a spec can drive it directly without the real timer (mirrors
    /// <c>CrosstalkStockWorker.TickOnceAsync</c>'s own precedent). Never throws past the "caller
    /// cancelled" case — every other fault is logged and swallowed, so one bad tick never stops the
    /// loop.</summary>
    internal async Task TickOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            var settings = AdStockSettingsReader.Read(configuration);

            await RepairReadyEligibilityAsync(stoppingToken);
            await RetireStaleAsync(settings.RefreshDays, stoppingToken);
            await RefillIfNeededAsync(settings, stoppingToken);
            await RenderOneIfDueAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot worker tick failed; continuing on the next tick");
        }
    }

    /// <summary>
    /// PLAN T402 review block 4's own repair sweep, RE-RULED at review F1/F4, WINDOW-CORRECTED at
    /// review F6: the write is guarded on BOTH the media row's current value and the spot's own
    /// recency — never the unconditional, every-tick <c>SetEligibleAsync(true)</c> the first cut ran
    /// (which bumped the row's own xmin/ETag on every single tick, whether or not the value actually
    /// changed, and fought an operator who had deliberately disabled an aged ready ad's own media
    /// row).
    ///
    /// <para>
    /// <b>The two operator levers stay the operator's (F1c).</b> <c>never_play</c> and retire are how
    /// an operator takes a spot off the air — this sweep must never override either. A Ready spot's
    /// own media <c>eligible</c> flag is WORKER-owned for roughly one tick interval plus the
    /// guardian's own grace after the ready transition (<see cref="AdSpotRepairWindow"/> — see that
    /// class's own remarks for why the window is wider than the guardian's own grace ALONE, PLAN T402
    /// review F6: this sweep is step ONE of a tick, a spot cannot go Ready before step FOUR of that
    /// SAME tick, so the earliest this sweep can ever see a fresh one is the NEXT tick):
    /// inside that window, an ineligible row can only be the <c>MarkReadyAsync</c>-committed/
    /// <c>SetEligibleAsync(true)</c>-never-ran race (a cancellation landing exactly between the two,
    /// inside <c>CastSegmentAuthor</c>) — a genuine orphan, safe to repair. OUTSIDE that window, an
    /// ineligible Ready row is never this sweep's doing (the worker itself always flips true within
    /// the window, and retire flips false only alongside its own <c>AdState.Retired</c> transition,
    /// which this sweep never sees again since it only reads <see cref="AdState.Ready"/>) — it can
    /// only be an operator's own hand, and this sweep leaves it alone outright, forever, no matter how
    /// far past the window. <c>GenWave.Host.Configuration.StationSettingsAllowlist</c>'s own
    /// <c>Station:Ads:*</c> remarks (GenWave.Ads must never reference that project directly, L10 —
    /// plain text, not a <c>cref</c>, on purpose) carry the SAME framing for an operator reading the
    /// settings surface.
    /// </para>
    ///
    /// <para>
    /// Bounded by <c>ListByStateAsync</c>'s own ceiling — PLAN T402 review F5b: the callee
    /// (<c>AdSpotRepository.ClampPaging</c>) clamps regardless of what this caller asks for, so no
    /// duplicate local ceiling is kept here. <c>state_changed_at desc, id desc</c> ordering (that
    /// method's own contract) is what lets this loop <c>break</c> the instant it reaches the first row
    /// outside the window — every row after it is strictly older, so none could qualify either.
    /// </para>
    /// </summary>
    async Task RepairReadyEligibilityAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var window = AdSpotRepairWindow.Compute(adsOptions.CurrentValue);

        var ready = await spotStore.ListByStateAsync(AdState.Ready, int.MaxValue, offset: 0, ct);
        foreach (var spot in ready.Items)
        {
            var readySince = new DateTimeOffset(spot.StateChangedAt, TimeSpan.Zero);
            if (now - readySince > window)
                break; // strictly-descending order: every later row is older still — operator intent.

            if (spot.MediaId is not { } mediaId)
                continue;

            var found = await adminLookup.GetByIdWithLibraryAsync(mediaId, ct);
            if (found is null || found.Value.Row.Eligible)
                continue; // already eligible (the common case) or the row is gone — nothing to repair.

            if (await catalogWriter.SetEligibleAsync(mediaId, eligible: true, ct))
            {
                logger.LogInformation(
                    "Ad spot {Id} ({Brand}) media row {MediaId} was ready but ineligible within the repair window — repaired",
                    spot.Id, LogSanitize.Strip(spot.Brand), mediaId);
            }
        }
    }

    /// <summary>SPEC F159.3's refresh half — retires every stale, non-owner ready spot
    /// <see cref="IAdSpotStore.ListReadyOlderThanAsync"/> finds (owner exemption already enforced at
    /// the store, STORY-389 AC5) and flips its media row ineligible (PLAN T402 review block 3).</summary>
    async Task RetireStaleAsync(int refreshDays, CancellationToken ct)
    {
        var stale = await spotStore.ListReadyOlderThanAsync(TimeSpan.FromDays(refreshDays), ct);
        foreach (var spot in stale)
        {
            var outcome = await spotStore.RetireAsync(spot.Id, spot.Version, ct);
            if (outcome.Result != AdSpotWriteResult.Updated)
            {
                // A concurrent operator retire/edit already moved this row — never this worker's to
                // force; the next tick re-reads whatever state actually won.
                logger.LogInformation(
                    "Ad spot {Id} refresh-retire did not apply (result={Result}); leaving it for a later tick",
                    spot.Id, outcome.Result);
                continue;
            }

            if (spot.MediaId is { } mediaId)
                await catalogWriter.SetEligibleAsync(mediaId, eligible: false, ct);
        }
    }

    /// <summary>SPEC F159.3's stock half — one generation attempt when the stock count (draft through
    /// ready, llm/pack — gh-#689's rider, see the class remarks) sits below target, never a catch-up
    /// burst (this class's own remarks).</summary>
    async Task RefillIfNeededAsync(AdStockSettings settings, CancellationToken ct)
    {
        var stockCount = await spotStore.CountStockGeneratedAsync(ct);
        if (stockCount >= settings.TargetCount)
            return;

        var brief = await briefStore.SampleEnabledAsync(ct);
        if (brief is null)
        {
            logger.LogInformation(
                "Ad stock below target ({StockCount}/{TargetCount}) but no enabled brief to sample from",
                stockCount, settings.TargetCount);
            return;
        }

        await GenerateOneAsync(brief, settings.AutoApprove, ct);
    }

    /// <summary>
    /// One brief → one script → one stored spot (SPEC F160.1-F160.3, F159.4; STORY-389 AC2/AC3;
    /// STORY-390). Builds the SAME validate-delegate adapter shape
    /// <c>GenWave.Ads.Tests.Specs.FeatureAdScriptWriterMeetsTheRealValidator</c> already previews (that
    /// file's own remarks name this exact method as the production destination): closes over the REAL
    /// <see cref="AdScriptValidator.Validate"/>, translating its result into the minimal
    /// <see cref="AdScriptValidationOutcome"/> contract <see cref="AdScriptWriter"/> (GenWave.Tts, which
    /// must never reference this project) accepts.
    /// </summary>
    async Task GenerateOneAsync(AdBrief brief, bool autoApprove, CancellationToken ct)
    {
        var writeRequest = new AdScriptWriteRequest(
            brief.Brand, brief.Premise, brief.Tone, GeneratedSpotSeconds, audiencePosture.Current,
            llmOptions.CurrentValue.MaxCopyChars, adsOptions.CurrentValue.DurationToleranceRatio);
        var validationRequest = new AdScriptValidationRequest(
            audiencePosture.Current, llmOptions.CurrentValue.MaxCopyChars, GeneratedSpotSeconds,
            adsOptions.CurrentValue.DurationToleranceRatio);

        var result = await scriptWriter.WriteAsync(writeRequest, BuildValidateDelegate(validationRequest), ct);

        // SPEC F159.1: a pack-installed brief's own spot is source=pack (T402's own reading of
        // "decide + document" — the ONLY signal this worker has for which of the two applies is
        // whether the sampled brief itself carries a pack_slug); every other generated spot is
        // source=llm. Never source=owner — that source is reachable only through T403's own future
        // manual editor, never this worker.
        var source = brief.PackSlug is not null ? AdSource.Pack : AdSource.Llm;

        switch (result)
        {
            case AdScriptWriteResult.Success success:
                await spotStore.CreateAsync(
                    new NewAdSpot(
                        brief.Brand, BuildTitle(brief), ComposeBriefSummary(brief), success.Script, source,
                        brief.PackSlug, GeneratedSpotSeconds, VoicePlan: null, BedMediaId: null,
                        InitialState: autoApprove ? AdState.Approved : AdState.Draft, FailReason: null),
                    ct);
                break;

            case AdScriptWriteResult.Failed { RuleId: not null } failed:
                // STORY-390 AC3: a script that never passed the validator (the ONE re-ask already
                // spent) is still recorded — visible, never silent (SPEC F159.1). Script stays null:
                // AdScriptWriteResult.Failed never carries the raw rejected text.
                await spotStore.CreateAsync(
                    new NewAdSpot(
                        brief.Brand, BuildTitle(brief), ComposeBriefSummary(brief), Script: null, source,
                        brief.PackSlug, GeneratedSpotSeconds, VoicePlan: null, BedMediaId: null,
                        AdState.Failed, failed.Reason),
                    ct);
                break;

            case AdScriptWriteResult.Failed failed:
                // A transport/generation fault (no RuleId) — SPEC F160.1's own skip-only floor:
                // nothing recorded, the deficit simply waits for a later tick (mirrors
                // CrosstalkScriptWriter's own discard; never a system Failed row for e.g. an extended
                // LLM outage flooding the table one row per tick).
                //
                // LogSanitize.Strip on BOTH interpolated values (PLAN T402 review F3, the CodeQL
                // cs/log-forging family, the AdRenderService.TryMarkFailedAsync precedent one file
                // over): brief.Brand is operator/pack-authored text and failed.Reason is a THIRD-PARTY
                // transport/generation detail (an exception message, a completion fragment) — neither
                // is bounded upstream the way a validator violation's own EchoForReason already is.
                logger.LogInformation(
                    "Ad script generation skipped for brand {Brand}: {Reason}",
                    LogSanitize.Strip(brief.Brand), LogSanitize.Strip(failed.Reason));
                break;
        }
    }

    static string BuildTitle(AdBrief brief) => $"{brief.Brand} spot";

    static string? ComposeBriefSummary(AdBrief brief) => (brief.Premise, brief.Tone) switch
    {
        (null, null) => null,
        ({ } premise, null) => premise,
        (null, { } tone) => tone,
        ({ } premise, { } tone) => $"{premise} ({tone})",
    };

    /// <summary>The exact adapter <c>GenWave.Ads.Tests.Specs.FeatureAdScriptWriterMeetsTheRealValidator</c>
    /// (T400) previews — see this file's own class remarks.</summary>
    Func<string, AdScriptValidationOutcome> BuildValidateDelegate(AdScriptValidationRequest validationRequest) =>
        rawScript => AdScriptValidator.Validate(rawScript, validationRequest, durationEstimator) switch
        {
            AdScriptValidationResult.Accepted => new AdScriptValidationOutcome.Accepted(),
            AdScriptValidationResult.Refused refused =>
                new AdScriptValidationOutcome.Refused(refused.Violation.RuleId, refused.Violation.Reason),
            _ => throw new UnreachableException($"Unhandled {nameof(AdScriptValidationResult)} case."),
        };

    /// <summary>
    /// SPEC F161.1, STORY-391 AC4/AC6: claims and renders AT MOST one <see cref="AdState.Approved"/>
    /// spot. No render even STARTS while <see cref="onAirRenderSignal"/> already reads in flight
    /// (STORY-391 AC4's own first fact); once started, a worker-owned linked
    /// <see cref="CancellationTokenSource"/> carries TWO independent cancellation sources into
    /// <see cref="AdRenderService.RenderAsync"/>:
    /// <list type="bullet">
    /// <item>a hard <see cref="AdsOptions.RenderBudgetSeconds"/> timeout (PLAN T402 review block 2 — a
    /// wedged Kokoro/ffmpeg must not hang this tick forever; sized generously above any real spot's
    /// render time — see that option's own remarks), and</item>
    /// <item>the SAME watchdog poll <c>CrosstalkStockWorker</c> already uses one project over
    /// (<see cref="WatchOnAirRenderAsync"/>) — cancels the instant a real on-air render starts mid-flight
    /// (STORY-391 AC4's own second fact).</item>
    /// </list>
    /// A cancellation from EITHER source reaches <see cref="AdRenderService.RenderAsync"/> identically
    /// (it never distinguishes them, by design — see that class's own remarks); THIS method is the one
    /// place that tells them apart, because only it knows which watcher actually fired
    /// (<paramref name="breakWindowOpened"/> in <see cref="RecoverFromCanceledRenderAsync"/>'s own
    /// signature) — a genuine budget timeout marks the spot <see cref="AdState.Failed"/> (an operator
    /// retry is the right next step for a systematically wedged backend), while a break-window yield
    /// re-arms it straight back to <see cref="AdState.Approved"/> via <see cref="IAdSpotStore.ReArmAsync"/>
    /// so the very next tick — no operator required — resumes it (STORY-391 AC4's own third fact).
    /// </summary>
    async Task RenderOneIfDueAsync(CancellationToken stoppingToken)
    {
        if (onAirRenderSignal.InFlight)
            return;

        if (await spotStore.ClaimNextApprovedAsync(stoppingToken) is not { } spot)
            return;

        using var budgetCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(adsOptions.CurrentValue.RenderBudgetSeconds), timeProvider);
        using var renderCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, budgetCts.Token);
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        var breakWindowOpened = false;
        var watchdog = WatchOnAirRenderAsync(renderCts, () => breakWindowOpened = true, watchdogCts.Token);

        try
        {
            var outcome = await renderService.RenderAsync(spot, renderCts.Token);
            LogOutcome(spot.Id, outcome);
        }
        catch (OperationCanceledException) when (renderCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            await RecoverFromCanceledRenderAsync(spot.Id, breakWindowOpened);
        }
        finally
        {
            await watchdogCts.CancelAsync();
            await watchdog;
        }
    }

    void LogOutcome(long spotId, AdRenderOutcome outcome)
    {
        switch (outcome)
        {
            case AdRenderOutcome.Rendered:
                logger.LogInformation("Ad spot {Id} rendered and is ready to air", spotId);
                break;
            case AdRenderOutcome.Failed:
                // AdRenderService.TryMarkFailedAsync already logged the reason — nothing to add.
                break;
            case AdRenderOutcome.ClaimConflict:
                logger.LogWarning(
                    "Ad spot {Id} claim conflict — the guardian likely re-armed it mid-render; stopping this tick without retry",
                    spotId);
                break;
        }
    }

    /// <summary>
    /// Best-effort recovery after <see cref="RenderOneIfDueAsync"/>'s own <c>renderCts</c> fired
    /// (PLAN T402 review block 1/2) — uses <see cref="CancellationToken.None"/> deliberately: the
    /// token that carried this render is already dead, and this bookkeeping write must still land even
    /// if <paramref name="spotId"/>'s own render was cancelled by the very budget/host-shutdown signal
    /// that would otherwise cancel this cleanup too.
    /// </summary>
    async Task RecoverFromCanceledRenderAsync(long spotId, bool breakWindowOpened)
    {
        if (breakWindowOpened)
        {
            logger.LogInformation(
                "Ad spot {Id} render yielded to an on-air break window; re-arming for a later tick", spotId);
            if (!await spotStore.ReArmAsync(spotId, CancellationToken.None))
            {
                logger.LogInformation(
                    "Ad spot {Id} ReArmAsync found it no longer Rendering — already resolved elsewhere", spotId);
            }
            return;
        }

        logger.LogWarning(
            "Ad spot {Id} render exceeded its {BudgetSeconds}s budget; marking failed",
            spotId, adsOptions.CurrentValue.RenderBudgetSeconds);
        if (!await spotStore.MarkFailedAsync(spotId, "render: exceeded the worker's own render budget", CancellationToken.None))
        {
            logger.LogInformation(
                "Ad spot {Id} MarkFailedAsync (budget) found it no longer Rendering — already resolved elsewhere",
                spotId);
        }
    }

    /// <summary>Polls <see cref="onAirRenderSignal"/> every <see cref="RenderWatchdogInterval"/> and
    /// cancels <paramref name="workCts"/> (calling <paramref name="onBreakWindowOpened"/> first, so the
    /// caller can tell this apart from the budget timeout) the instant it reads in flight. Mirrors
    /// <c>CrosstalkStockWorker.WatchBreakWindowAsync</c>'s own shape; exits cleanly once
    /// <paramref name="ct"/> fires (the render it watches already finished, cancelled or not).</summary>
    async Task WatchOnAirRenderAsync(CancellationTokenSource workCts, Action onBreakWindowOpened, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(RenderWatchdogInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (workCts.IsCancellationRequested)
                    return; // the budget timer already fired — nothing left for this watchdog to claim.

                if (onAirRenderSignal.InFlight)
                {
                    onBreakWindowOpened();
                    await workCts.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal: ct fired because the render this watches already finished (or the host is
            // shutting down) — nothing left to interrupt.
        }
    }
}
