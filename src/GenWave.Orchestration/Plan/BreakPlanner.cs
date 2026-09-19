namespace GenWave.Orchestration;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Builds one break's <see cref="BreakPlan"/> from a <see cref="BreakContext"/> (SPEC F188, PLAN
/// T521) — today's <c>Orchestrator.EnqueuePatterAsync</c> cadence/drain steps moved verbatim, minus
/// rendering (no <c>ITtsSegmentSource</c>/<c>IVerbatimSegmentRenderer</c>/copy-writer dependency
/// exists here) and minus the handoff-ceremony ARM (SPEC F190 — <c>EnqueueHandoffCeremonyAsync</c>,
/// "step 2.5," stays on <see cref="Orchestrator"/>). DRAINING an already-armed SignOff/SignOn stays
/// in scope — one drain-switch arm among five — only ARMING a new one does not. Every side effect
/// the old path performs (crosstalk <c>MarkVended</c>, an announcement claim, deferral-queue cadence
/// enqueues, the <see cref="SpeechDeferralQueue.TryDequeueDue"/> drain with the same
/// <c>onExpired</c>, the TimeDate-expiry WARN, the catalog pool lookup, the ad-vend try/catch log)
/// fires in the SAME order and with the SAME arguments as today (SPEC F188.2).
///
/// <para>
/// <b>Flavored announcements are out of scope here (SPEC F188's own "no copy-writer dependency"):
/// </b> the planner never calls <c>IAnnouncementCopyWriter</c> itself — no such seam exists on this
/// class at all. It still carries the law forward: each <see cref="SegmentKind.Announcement"/> slot's
/// <see cref="VerbatimSource.AllowFlavor"/> is stamped from <c>!announcement.Verbatim</c>, so the
/// render phase can resolve flavored copy through the announcement copy writer FIRST when true,
/// falling back to the owner's own plain <see cref="AnnouncementItem.Message"/> — carried as
/// <see cref="VerbatimSource.Copy"/> — on null or throw (SPEC F191.4). No behavior changes here:
/// <c>Orchestrator.ResolveFlavoredAnnouncementCopyAsync</c>'s law simply moves to the render phase
/// (PLAN T522), never dropped.
/// </para>
///
/// <para>
/// Resolves speaker snapshots (SPEC F189, PLAN T527) when an <see cref="ISpeakerSnapshotSource"/> is
/// wired — every Render/Verbatim slot's request carries the <see cref="SpeakerSnapshot"/> the plan
/// named it with (SPEC F187.4), memoized per plan by <see cref="SpeakerResolution"/> so the same
/// station or persona id resolves once however many slots name it (SPEC F188.4). A caller that passes
/// no source leaves every request's Speaker null — the pre-T527 ambient path, unchanged (SPEC F189.6).
/// </para>
///
/// <para>
/// <b>Two side effects stay at the <see cref="Orchestrator"/> call site for PLAN T522</b>, not
/// migrated here: the one-time <c>timeDateBudgetLoggedOnce</c> boot echo (Orchestrator.cs's own
/// <c>EnqueuePatterAsync</c>, SPEC F141.4) and the <c>announcementSource &amp;&amp;
/// announcementRenderer</c> double-seam gate (Orchestrator.cs's own step 1.75) — this planner gates
/// the announcement step on <see cref="announcementSource"/> alone, since no renderer seam exists
/// here at all. T522 must also read <see cref="BreakContext.Now"/> at the exact point
/// <see cref="Orchestrator"/> reads <see cref="TimeProvider.GetUtcNow"/> today, not before or after
/// it.
/// </para>
/// </summary>
public sealed partial class BreakPlanner(
    IActivePersonaAccessor personaAccessor,
    ILogger<BreakPlanner> logger,
    IRenderBudgetProvider renderBudgetProvider,
    SpeechDeferralQueue deferralQueue,
    TimeProvider timeProvider,
    IStationScopeProvider scopeProvider,
    IStationClockProvider? stationClock = null,
    CachingScheduleResolver? scheduleResolver = null,
    IContextSettingsProvider? contextSettings = null,
    IMediaCatalog? catalog = null,
    IStationImagingSettingsProvider? imagingSettings = null,
    CrosstalkPlanner? crosstalkPlanner = null,
    IAnnouncementSource? announcementSource = null,
    ITtsVoiceLister? voiceLister = null,
    IAdCadenceProvider? adCadenceProvider = null,
    IAdSpotVend? adSpotVend = null,
    IPersonaStore? personaStore = null,
    ISpeakerSnapshotSource? speakerSnapshots = null)
{
    static readonly TimeSpan TimeDateHonestyThreshold = TimeSpan.FromSeconds(90);
    const int AnnouncementVendCap = 2;

    readonly IContextSettingsProvider contextSettings = contextSettings ?? NoOpContextSettingsProvider.Instance;
    readonly IStationImagingSettingsProvider imagingSettings = imagingSettings ?? NoOpStationImagingSettingsProvider.Instance;
    readonly IAdCadenceProvider adCadenceProvider = adCadenceProvider ?? NoOpAdCadenceProvider.Instance;
    readonly IAdSpotVend adSpotVend = adSpotVend ?? NoOpAdSpotVend.Instance;

    /// <summary>
    /// Builds one break's plan (SPEC F188): reads the render budget and the time/date staleness
    /// budget exactly once (SPEC F188.3), then kicks slots in today's order — back-announce,
    /// crosstalk, announcements, station-id/ad cadence enqueue, the due-deferral drain, lead-in —
    /// numbering them from 1 in kick order (SPEC F187.2).
    /// </summary>
    public async Task<BreakPlan> PlanAsync(BreakContext context, CancellationToken ct)
    {
        var renderBudget = renderBudgetProvider.Current;
        var timeDateStaleBudget = TimeSpan.FromSeconds(imagingSettings.Current.TimeAnnouncementBudgetSeconds);

        // SPEC F187.4/F188.4 (PLAN T527): one memo per plan — null when no ISpeakerSnapshotSource is
        // wired, so every Render/Verbatim slot stays unstamped (SPEC F189.6, the ambient path).
        var speakers = speakerSnapshots is null ? null : new SpeakerResolution(speakerSnapshots);

        var vendedCrosstalk = context.Next is not null && context.DrainAsOf is null && !CeremonyDrainsThisBreak(context)
            ? TryVendCrosstalkForThisBreak()
            : null;
        var crosstalkAiredThisBreak = vendedCrosstalk is not null;

        var slots = new List<PlannedSlot>();

        if (await BuildBackAnnounceSlotAsync(context, crosstalkAiredThisBreak, speakers, ct) is { } backAnnounce)
            slots.Add(backAnnounce);

        if (BuildCrosstalkSlot(vendedCrosstalk, context) is { } crosstalk)
            slots.Add(crosstalk);

        slots.AddRange(await BuildAnnouncementSlotsAsync(context, speakers, ct));

        EnqueueStationIdCadence(context);
        EnqueueAdCadence(context);

        slots.AddRange(await DrainDueDeferralsAsync(context, timeDateStaleBudget, speakers, ct));

        if (await BuildLeadInSlotAsync(context, crosstalkAiredThisBreak, speakers, ct) is { } leadIn)
            slots.Add(leadIn);

        var numbered = slots.Select((slot, i) => slot with { Ordinal = i + 1 }).ToList();
        return new BreakPlan(context.UnitOrdinal, context, renderBudget, numbered);
    }

    // SPEC F186.3's per-kind drop-report assignment (T521 review, cross-referenced against
    // Orchestrator.EnqueuePatterAsync's render-await loop): an announcement (its claimed id) and a
    // context segment (its provider key) warn; a dropped sign-off/sign-on also publishes
    // HandoffPieceDropped; every other kind drops silently.
    static DropPolicy DropPolicyFor(SegmentKind kind) => kind switch
    {
        SegmentKind.Announcement or SegmentKind.ContextSegment => new WarnDrop(),
        SegmentKind.SignOff or SegmentKind.SignOn => new WarnAndEventDrop(),
        _ => new SilentDrop(),
    };

    // Verbatim copy of EnqueuePatterAsync's own crosstalk-vend gate (SPEC F127.9): a ceremony that
    // will drain THIS break, or an already-forced drain-as-of instant (a straddle reconciliation
    // pass), both suppress crosstalk this break.
    bool CeremonyDrainsThisBreak(BreakContext context)
    {
        var drainNow = context.DrainAsOf ?? context.Now;
        return WouldDrainAt(deferralQueue.Peek(SpeechDeferralKind.SignOff), drainNow, context.Now)
            || WouldDrainAt(deferralQueue.Peek(SpeechDeferralKind.SignOn), drainNow, context.Now);
    }

    static bool WouldDrainAt(SpeechDeferral? deferral, DateTimeOffset drainNow, DateTimeOffset realNow) =>
        deferral is not null
        && deferral.Due <= drainNow
        && (deferral.NotBefore is not { } notBefore || notBefore <= realNow);

    StockedCrosstalkExchange? TryVendCrosstalkForThisBreak()
    {
        if (crosstalkPlanner is null) return null;
        if (scheduleResolver?.TryGetCurrent() is not { Segment: { } hostBlock, Show: { Slug.Length: > 0 } show })
            return null;
        if (scheduleResolver.TryGetCurrentWeekSnapshot() is not { } week) return null;
        if (crosstalkPlanner.TryVend(show.Slug, hostBlock, week) is not { } exchange) return null;

        if (!File.Exists(exchange.AssetPath))
        {
            crosstalkPlanner.DiscardUnaired(exchange, "asset missing at vend");
            return null;
        }

        return exchange;
    }
}
