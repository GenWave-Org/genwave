namespace GenWave.Orchestration;

using System.Globalization;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

// The per-step slot builders BreakPlanner.PlanAsync kicks in order — split from BreakPlanner.cs
// purely for the ~300-line budget (csharp-best-practices); shares that file's ctor-level fields via
// the partial class.
public sealed partial class BreakPlanner
{
    // Step 1 — SPEC F186's BackAnnounce row: only when the previous track exists and the cadence
    // asks for one (verbatim copy of EnqueuePatterAsync's own step 1).
    async Task<PlannedSlot?> BuildBackAnnounceSlotAsync(BreakContext context, bool crosstalkAiredThisBreak, CancellationToken ct)
    {
        if (!context.Cadence.BackAnnounceAfterEachTrack || context.Previous is not { } prev)
            return null;

        var (voice, personaName) = await ResolvePersonaAsync(context.Identity.Voice, ct);
        var request = new SegmentRequest(
            SegmentKind.BackAnnounce, voice, context.Identity.Name, prev, StationLocalNow(), context.Identity.Id, personaName)
        {
            CrosstalkAiredThisBreak = crosstalkAiredThisBreak,
        };

        return new PlannedSlot(0, SegmentKind.BackAnnounce, new RenderSource(request), Reservation: null, DropPolicyFor(SegmentKind.BackAnnounce), ObserveDuration: true);
    }

    // Step 1.5 — SPEC F186's Crosstalk row: a vended exchange arrives already resolved (no render),
    // reserved by its own synthesized media id (verbatim copy of EnqueuePatterAsync's own step 1.5).
    PlannedSlot? BuildCrosstalkSlot(StockedCrosstalkExchange? vendedCrosstalk, BreakContext context)
    {
        if (vendedCrosstalk is not { } exchangeToAir)
            return null;

        var crosstalkMediaId = $"tts:crosstalk:{Path.GetFileNameWithoutExtension(exchangeToAir.AssetPath)}";
        var item = new MediaItem(
            crosstalkMediaId, exchangeToAir.AssetPath, context.Identity.Name, exchangeToAir.Loudness,
            Artist: context.UnitDjName ?? context.Identity.Name, Cue: exchangeToAir.Cue, DurationMs: exchangeToAir.DurationMs,
            DjName: context.UnitDjName, SegmentKind: SegmentKind.Crosstalk)
        {
            CrosstalkScript = exchangeToAir.Script,
        };

        crosstalkPlanner?.MarkVended(crosstalkMediaId, exchangeToAir);
        return new PlannedSlot(0, SegmentKind.Crosstalk, new ReadySource(item), new Reservation(ReservationKind.Crosstalk, crosstalkMediaId), DropPolicyFor(SegmentKind.Crosstalk), ObserveDuration: false);
    }

    // Step 1.75 — SPEC F186's Announcement row, cadence-independent (runs on every unit). No
    // IVerbatimSegmentRenderer/IAnnouncementCopyWriter dependency exists here (SPEC F188's own "no
    // copy-writer" constraint), so the planner never resolves flavored copy itself — it stamps
    // VerbatimSource.AllowFlavor from !announcement.Verbatim instead, carrying the fallback law (SPEC
    // F191.4) forward for the render phase to apply; the owner's own plain Message always rides as
    // Copy, ready to air unflavored on its own.
    async Task<IReadOnlyList<PlannedSlot>> BuildAnnouncementSlotsAsync(BreakContext context, CancellationToken ct)
    {
        if (announcementSource is not { } source)
            return [];

        var deliverable = await ClaimAnnouncementsAsync(source, ct);
        var slots = new List<PlannedSlot>(deliverable.Count);
        foreach (var announcement in deliverable)
        {
            var voice = await ResolveAnnouncementVoiceAsync(announcement.RequestedVoice, context.Identity.Voice, ct);
            var request = new SegmentRequest(SegmentKind.Announcement, voice, context.Identity.Name, null, StationLocalNow(), context.Identity.Id);
            var copy = new SegmentCopy(announcement.Message, FreshPerAiring: true);
            var reservation = new Reservation(ReservationKind.Announcement, announcement.Id.ToString(CultureInfo.InvariantCulture));

            slots.Add(new PlannedSlot(0, SegmentKind.Announcement, new VerbatimSource(request, copy, AllowFlavor: !announcement.Verbatim), reservation, DropPolicyFor(SegmentKind.Announcement), ObserveDuration: true));
        }

        return slots;
    }

    // Step 2 — SPEC F186's StationId cadence trigger (verbatim copy of EnqueuePatterAsync's own step
    // 2, reading BreakContext.UnitOrdinal in place of Orchestrator's own unitCount field): claims no
    // slot itself, only arms the drain below.
    void EnqueueStationIdCadence(BreakContext context)
    {
        if (context.Next is not null
            && context.Cadence.StationIdEveryNUnits > 0
            && context.UnitOrdinal > 0
            && context.UnitOrdinal % context.Cadence.StationIdEveryNUnits == 0)
        {
            deferralQueue.Enqueue(SpeechDeferralKind.StationId, "cadence: Station:Cadence:StationIdEveryNUnits");
        }
    }

    // Step 2.25 — SPEC F186's Ad cadence trigger (verbatim copy of EnqueuePatterAsync's own step
    // 2.25); same shape as the station-id trigger, its own provider read.
    void EnqueueAdCadence(BreakContext context)
    {
        var adEveryNUnits = adCadenceProvider.Current;
        if (context.Next is not null
            && adEveryNUnits > 0
            && context.UnitOrdinal > 0
            && context.UnitOrdinal % adEveryNUnits == 0)
        {
            deferralQueue.Enqueue(SpeechDeferralKind.Ad, "cadence: Station:Ads:EveryNUnits");
        }
    }

    // The drain — SPEC F186's five drained rows, in TryDequeueDue's own due order (verbatim copy of
    // EnqueuePatterAsync's own drain foreach/switch, minus step 2.5's handoff-ceremony ARM: draining
    // an already-armed SignOff/SignOn stays in scope, arming a new one does not — SPEC F190).
    async Task<IReadOnlyList<PlannedSlot>> DrainDueDeferralsAsync(BreakContext context, TimeSpan timeDateStaleBudget, CancellationToken ct)
    {
        var drainNow = context.DrainAsOf ?? context.Now;
        var slots = new List<PlannedSlot>();

        foreach (var deferral in deferralQueue.TryDequeueDue(
            drainNow, context.Hold, context.QueuedAhead, timeDateStaleBudget,
            onExpired: (expired, lateness) => LogTimeDateExpiry(expired, lateness, timeDateStaleBudget)))
        {
            var slot = deferral.Kind switch
            {
                SpeechDeferralKind.StationId => await BuildStationIdDrainSlotAsync(context, ct),
                SpeechDeferralKind.Ad => await BuildAdDrainSlotAsync(ct),
                SpeechDeferralKind.SignOff or SpeechDeferralKind.SignOn => BuildHandoffDrainSlot(deferral, context.Identity),
                SpeechDeferralKind.TimeDate => BuildTimeDateDrainSlot(deferral, context),
                SpeechDeferralKind.Context => await BuildContextDrainSlotAsync(deferral, context, drainNow, ct),
                _ => null,
            };

            if (slot is { } s) slots.Add(s);
        }

        return slots;
    }

    async Task<PlannedSlot?> BuildStationIdDrainSlotAsync(BreakContext context, CancellationToken ct)
    {
        var currentShow = scheduleResolver?.TryGetCurrent()?.Show;
        var pooled = catalog is null
            ? null
            : await catalog.GetRandomReadyByImagingKindAsync(scopeProvider.Current, ImagingKind.StationId, currentShow?.Id, ct);

        if (pooled is not null)
        {
            var item = BuildPooledStationIdItem(pooled);
            return new PlannedSlot(0, SegmentKind.StationId, new ReadySource(item), new Reservation(ReservationKind.StationIdPool, item.MediaId), DropPolicyFor(SegmentKind.StationId), ObserveDuration: false);
        }

        var templatedRequest = ShowIdentRequest.For(BuildStationIdRequest(context.Identity), currentShow);
        var templatedReservation = new Reservation(ReservationKind.Deferral, nameof(SpeechDeferralKind.StationId));
        return new PlannedSlot(0, SegmentKind.StationId, new RenderSource(templatedRequest), templatedReservation, DropPolicyFor(SegmentKind.StationId), ObserveDuration: true);
    }

    // Orchestrator.BuildAdRequest has no equivalent here: its only purpose was to hand KickResolved
    // a "kind tag" for a resolved item, but ReadySource carries no Request member at all — a vended
    // spot is already a MediaItem, so no request-builder call is needed for this rung.
    async Task<PlannedSlot?> BuildAdDrainSlotAsync(CancellationToken ct)
    {
        MediaItem? spot;
        try
        {
            spot = await adSpotVend.GetNextSpotAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot vend threw {ExceptionType}; no ad this break (SPEC F158.3)", ex.GetType().Name);
            spot = null;
        }

        if (spot is null)
        {
            logger.LogInformation("No ad spot available this break — the ads library is empty or has nothing ready (SPEC F158.3).");
            return null;
        }

        var stamped = spot with { SegmentKind = SegmentKind.Ad };
        return new PlannedSlot(0, SegmentKind.Ad, new ReadySource(stamped), new Reservation(ReservationKind.AdSpot, stamped.MediaId), DropPolicyFor(SegmentKind.Ad), ObserveDuration: false);
    }

    PlannedSlot? BuildHandoffDrainSlot(SpeechDeferral deferral, StationIdentity identity)
    {
        if (deferral.Handoff is not { } handoff) return null;

        var kind = deferral.Kind == SpeechDeferralKind.SignOff ? SegmentKind.SignOff : SegmentKind.SignOn;
        var request = BuildHandoffRequest(kind, handoff, identity);
        return new PlannedSlot(0, kind, new RenderSource(request), Reservation: null, DropPolicyFor(kind), ObserveDuration: true);
    }

    PlannedSlot BuildTimeDateDrainSlot(SpeechDeferral deferral, BreakContext context)
    {
        var lateness = SpeechDeferralQueue.AirTimeLateness(context.Now, context.QueuedAhead, deferral.Due);
        var freshness = lateness > TimeDateHonestyThreshold ? TimeAnnouncementFreshness.Late : TimeAnnouncementFreshness.OnTime;
        var request = BuildTimeDateRequest(deferral, context.Identity, freshness);

        return new PlannedSlot(0, SegmentKind.TimeDate, new RenderSource(request), Reservation: null, DropPolicyFor(SegmentKind.TimeDate), ObserveDuration: true);
    }

    async Task<PlannedSlot?> BuildContextDrainSlotAsync(SpeechDeferral deferral, BreakContext context, DateTimeOffset drainNow, CancellationToken ct)
    {
        var built = await BuildContextSegmentRequestAsync(deferral, context.Identity, drainNow, ct);
        return built is { } b
            ? new PlannedSlot(0, SegmentKind.ContextSegment, new RenderSource(b.Request), Reservation: null, DropPolicyFor(SegmentKind.ContextSegment), ObserveDuration: true)
            : null;
    }

    // Step 3 — SPEC F186's LeadIn row: only when the next track exists and the cadence asks for one
    // (verbatim copy of EnqueuePatterAsync's own step 3).
    async Task<PlannedSlot?> BuildLeadInSlotAsync(BreakContext context, bool crosstalkAiredThisBreak, CancellationToken ct)
    {
        if (!context.Cadence.LeadInBeforeEachTrack || context.Next is not { } next)
            return null;

        var (voice, personaName) = await ResolvePersonaAsync(context.Identity.Voice, ct);
        var request = new SegmentRequest(
            SegmentKind.LeadIn, voice, context.Identity.Name, next, StationLocalNow(), context.Identity.Id, personaName)
        {
            CrosstalkAiredThisBreak = crosstalkAiredThisBreak,
        };

        return new PlannedSlot(0, SegmentKind.LeadIn, new RenderSource(request), Reservation: null, DropPolicyFor(SegmentKind.LeadIn), ObserveDuration: true);
    }
}
