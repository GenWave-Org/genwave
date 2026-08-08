namespace GenWave.Orchestration;

using System.Globalization;
using Microsoft.Extensions.Logging;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;

/// <summary>
/// Plans and interleaves music tracks and TTS patter segments per <see cref="CadenceConfig"/>.
/// Maintains an internal buffer so that a single unit (back-announce + station-id + lead-in + music)
/// can be planned at once and dequeued one item at a time.
///
/// Cadence per unit:
///   1. Back-announce for the PREVIOUS track (if any, if configured)
///   2. Station-ID every N units (if configured)
///   3. Lead-in for the NEXT track (if configured)
///   4. The music track itself
///
/// TTS ids always start with "tts:"; music ids never do.  tts:* ids are stripped from the ordered
/// recent-ids list before calling <see cref="IMediaCatalog"/> so recent-repeat avoidance stays clean.
///
/// Music selection reads <paramref name="scopeProvider"/> on every call (SPEC F30.1) rather than a
/// scope stored on the station identity — this project references only <c>GenWave.Core</c>
/// and cannot see the Host's live options monitor directly, so <see cref="IStationScopeProvider"/>
/// is the thin seam it depends on instead. Never cache the read result in a field.
///
/// Cadence is read the same way, through <paramref name="cadenceProvider"/> (gitea-#211 — F30.1's
/// precedent applied to cadence): read exactly ONCE per unit, into a local, at the top of
/// <see cref="GetNextAsync"/> (hoisted from <see cref="EnqueuePatterAsync"/> by gh-#254 so the
/// boundary fit's patter estimates share the same snapshot) — not once per cadence check within
/// that unit — so one unit is planned under one consistent cadence snapshot rather than racing
/// live reads that could straddle a concurrent settings write mid-unit.
///
/// Music selection calls <see cref="IMediaCatalog.GetRotationCandidateAsync"/> (SPEC F41.1, closes
/// gitea-#210/gitea-#213) instead of the strict-exclude <c>GetRandomReadyAsync</c> — a tiered preference query
/// that relaxes rather than drains. <paramref name="rotationProvider"/> is read fresh on every call
/// (same F30.1/gitea-#211 discipline) for the artist-separation depth passed to that tier; a relaxed
/// candidate (<see cref="RotationCandidate.RepeatedRecent"/>/<see cref="RotationCandidate.RepeatedArtist"/>)
/// logs a WARN naming which constraint gave way, and a null candidate — now genuinely "zero playable
/// rows" (F41.2) — logs a WARN naming the drain and returns null non-fatally (F6.3 stands).
///
/// <para>
/// The pick ladder itself — request rung (SPEC F87.6, rung -1: a live pending listener request
/// short-circuits the pick entirely, ahead of persona ranking and the envelope-only ladder alike),
/// persona rung, trust-but-verify (SPEC F81.5), and the SPEC F81.6 degradation ladder (rotation, then
/// energy, then genres, each rung logging a loud WARN naming what gave way, before the plain
/// never-silence floor) — moved to <see cref="MusicSelectionPolicy"/> (F112, STORY-295, PLAN T218).
/// This Orchestrator hands it scope/recent-ids/artist-separation plus whatever <see cref="BoundaryFitPlan"/>
/// it already built (<see cref="BuildBoundaryFit"/> stays here — it reads unit-assembly state
/// <see cref="MusicSelectionPolicy"/> never sees) and gets back a <see cref="RotationCandidate"/> or
/// <see langword="null"/> on a genuine drain. See <see cref="MusicSelectionPolicy"/>'s own remarks for
/// the full rung order.
/// </para>
///
/// <paramref name="renderBudgetProvider"/> caps how long any single TTS render may take, read fresh
/// once per unit (SPEC F44.2, gitea-#197 — the same discipline <paramref name="cadenceProvider"/> and
/// <paramref name="identityProvider"/> follow) rather than a boot-frozen <see cref="TimeSpan"/> —
/// Program.cs used to compute this once at composition-root time and hand it in as a fixed value for
/// the life of the process. A segment that exceeds the budget, faults, or returns null is silently
/// dropped; the unit continues with the next ready item (typically music).
///
/// Each segment's <see cref="SegmentRequest.Voice"/> is resolved through <paramref name="personaAccessor"/>
/// fresh per render (SPEC F35.2, F35.3, F35.5) — the active persona's voice when non-empty, else
/// the station's own default voice — never cached, so a live activate/deactivate reaches the very
/// next segment with no restart. One DB read per segment is negligible at cadence scale; this is the
/// documented design, not a shortcut to revisit later. <see cref="SegmentKind.StationId"/> is the
/// one carve-out (gh-#96): station IDs are station imaging — always the station's own voice and
/// credit, never the persona's — so their build skips the accessor entirely (see the deferral
/// drain in <see cref="EnqueuePatterAsync"/>).
///
/// <see cref="SegmentRequest.PersonaName"/> is stamped from that SAME accessor read (SPEC F39.1,
/// gitea-#212) — never a second call — so <c>Voice</c> and <c>PersonaName</c> on one <see cref="SegmentRequest"/>
/// always describe the same persona, even mid-switch.
///
/// Station identity (<see cref="StationIdentity.Id"/>/<see cref="StationIdentity.Name"/>/
/// <see cref="StationIdentity.Voice"/>) is read through <paramref name="identityProvider"/> once per
/// unit, at the top of <see cref="GetNextAsync"/> (SPEC F44.1, gitea-#196, the same discipline —
/// and the same gh-#254 hoist — <paramref name="cadenceProvider"/> follows one line above) — never
/// cached in a field — so a live <c>Station:Name</c>/<c>Station:Voice</c> edit reaches the very
/// next unit's segments with no process restart.
///
/// The station-id cadence check (below) never builds its <see cref="SegmentRequest"/> directly
/// (SPEC F74.1/F74.2, STORY-197): it enqueues a deferral into <paramref name="deferralQueue"/>,
/// which <see cref="EnqueuePatterAsync"/> drains in the same pass. This planning pass IS the next
/// track boundary — a whole unit (back-announce/station-id/lead-in/music) is queued atomically
/// before the next track ever reaches air — so draining here can never land mid-track. Routing
/// even an always-immediately-due trigger through the queue formalizes the seam a future deferred
/// producer (e.g. a wall-clock-scheduled handoff) shares: enqueue whenever its own trigger fires,
/// drain only at a boundary.
///
/// Music selection is boundary-aware (SPEC F74.3, STORY-198) and, as of gh-#254, a genuine duration
/// FIT: <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> peeks <paramref name="deferralQueue"/>'s
/// earliest pending deferral before every pick and, only when it is due strictly in the future
/// within <paramref name="boundaryBiasProvider"/>'s lookahead window, softly biases the pick toward
/// whichever sampled candidate's effective end — after queued-ahead drift, this unit's own patter,
/// crossfade trim, and the break's expected patter (the gh-#253 estimator) — lands closest to the
/// boundary, with a first-within-tolerance win rule guarding against degenerate closest-fit
/// repetition (see that method's remarks). Never a hard filter, and subordinate to rotation
/// (F41.1/F41.3 tiering still governs which candidates even get sampled). Outside that window (the
/// no-imminent-boundary common case) this degrades to exactly the one
/// <see cref="IMediaCatalog.GetRotationCandidateAsync"/> call this Orchestrator has always made.
///
/// <para>
/// <b>Handoff ceremony producer (SPEC F92.1-F92.6, STORY-243, PLAN T124):</b>
/// <see cref="EnqueueHandoffCeremonyAsync"/> runs every unit, AFTER the deferral drain (unlike the
/// station-id cadence check, which enqueues BEFORE that same drain — see this method's own remarks
/// for why the ordering differs), and arms <see cref="SpeechDeferralKind.SignOff"/>/
/// <see cref="SpeechDeferralKind.SignOn"/> into <paramref name="deferralQueue"/> the moment
/// <paramref name="scheduleResolver"/>'s resolved <c>OnAirSnapshot.BoundaryAt</c> enters
/// <paramref name="boundaryBiasProvider"/>'s F74.3 lookahead window (the identical knob this
/// Orchestrator's own <see cref="GetNextAsync"/> already reads to build the fit
/// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> consumes, so "in window" means one thing
/// station-wide) — future-dated, drained by the very same loop on a LATER unit. A
/// <see langword="null"/> <paramref name="scheduleResolver"/> (no format-clock schedule wired)
/// makes this producer a permanent no-op — the pre-F91 station shape. See that method's own remarks
/// for the F92.3 dedupe rules and the explicit <see cref="SpeechDeferralQueue.Clear"/> a boundary
/// leaving the window triggers.
/// </para>
/// </summary>
public sealed class Orchestrator(
    IStationIdentityProvider identityProvider,
    IStationScopeProvider scopeProvider,
    ICadenceProvider cadenceProvider,
    IRotationSettingsProvider rotationProvider,
    MusicSelectionPolicy musicSelectionPolicy,
    ITtsSegmentSource tts,
    IActivePersonaAccessor personaAccessor,
    ILogger<Orchestrator> logger,
    IRenderBudgetProvider renderBudgetProvider,
    SpeechDeferralQueue deferralQueue,
    TimeProvider timeProvider,
    IBoundaryBiasProvider boundaryBiasProvider,
    CachingScheduleResolver? scheduleResolver = null,
    IPersonaStore? personaStore = null,
    IStationEventSink? events = null,
    IStationClockProvider? stationClock = null,
    IPatterDurationEstimator? patterEstimator = null) : INextItemProvider
{
    // gh-#254 — how far from the boundary a candidate may land and still count as a WIN ("±30s of
    // the boundary is a win"), widened as the gh-#253 estimate's confidence tier drops: the fit's
    // patter terms are only as good as their worst contributing estimate, and pretending exact-tier
    // precision over a chars-per-second guess would over-optimize the pick for false accuracy.
    // Consumed only by BuildBoundaryFit below (F112, STORY-295: MusicSelectionPolicy reads the
    // resulting BoundaryFitPlan.Tolerance, never these constants directly).
    static readonly TimeSpan FitToleranceExact = TimeSpan.FromSeconds(30);
    static readonly TimeSpan FitToleranceHistorical = TimeSpan.FromSeconds(45);
    static readonly TimeSpan FitToleranceHeuristic = TimeSpan.FromSeconds(60);

    /// <summary>
    /// gh-#117 — the ONE stamp every <see cref="SegmentRequest.LocalNow"/> this Orchestrator builds
    /// goes through: station-local now via the live <see cref="IStationClockProvider"/> seam
    /// (<c>Station:Timezone</c>, read fresh per call) when the composition supplies one, otherwise
    /// <paramref name="timeProvider"/>'s UTC now — the pre-gh-#117 behavior (the old raw
    /// <c>DateTimeOffset.UtcNow</c>), unchanged for every rig that never registers the seam, and
    /// what the templated time/date patter and the LLM's "Local time" line both render from.
    /// </summary>
    DateTimeOffset StationLocalNow() => stationClock?.LocalNow ?? timeProvider.GetUtcNow();

    /// <summary>
    /// How far ahead of the resolved boundary the SignOff half of a handoff ceremony is due (SPEC
    /// F92.1 — "sign-off due just before the boundary, sign-on due at it"). No exact interval is
    /// spec'd; this is a judged, smallest-honest constant just large enough to keep the two pieces'
    /// due times distinct from one another.
    ///
    /// <para>
    /// <b>What that distinctness actually buys (T124 review finding F5 — corrects an earlier version
    /// of this comment that overstated it):</b> a track is rarely shorter than this lead time, so in
    /// the overwhelmingly common case BOTH due times (<c>BoundaryAt - SignOffLeadTime</c> and
    /// <c>BoundaryAt</c> itself) fall inside the SAME gap between two consecutive track boundaries —
    /// both pieces drain together, in ONE <see cref="SpeechDeferralQueue.TryDequeueDue"/> call, at the
    /// first unit boundary at-or-after <c>BoundaryAt - SignOffLeadTime</c>. The distinct due times do
    /// NOT themselves guarantee sign-off airs before sign-on in that shared drain — that ordering
    /// comes from <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s own Due-ascending, kind-tiebreak
    /// contract (SignOff sorts before SignOn), which is what actually delivers "sign-off, then
    /// sign-on, at track seams." This constant's only job is giving that contract two genuinely
    /// different due times to sort in the rare case a boundary is reached exactly (both would tie on
    /// <c>Due</c> otherwise, falling through to the SAME kind tiebreak regardless). Not a live-tunable
    /// SPEC knob the way F74.3's own boundary-bias lookahead is — just an implementation seam.
    /// </para>
    /// </summary>
    static readonly TimeSpan SignOffLeadTime = TimeSpan.FromSeconds(15);

    /// <summary>
    /// gh-#300 — below this much room left in front of a handoff boundary, no music unit is planned
    /// at all: the ceremony itself becomes the unit.
    ///
    /// <para>
    /// <b>Where the number comes from.</b> Planning a track of length L into D of remaining room
    /// lands the ceremony <c>L - D</c> LATE; declining lands it <c>D</c> EARLY. Declining is
    /// therefore the better trade exactly while <c>D &lt; L / 2</c>. With a typical unit around
    /// three minutes that break-even sits at ninety seconds, and this is that number. Above it the
    /// gh-#254 fit keeps its existing least-late behavior, which is still the right answer there.
    /// </para>
    ///
    /// <para>
    /// The 2:05 incident sat far below this line — the queued audio already ran PAST the boundary,
    /// so <c>desired</c> was deeply negative and every candidate was hopeless. A judged constant in
    /// the spirit of <c>MusicSelectionPolicy.ExpectedCrossfadeTrim</c> and <see cref="SignOffLeadTime"/>, not a
    /// live knob: gh-#300's own fit logging is what makes promoting it to one an argument from
    /// field data rather than taste, and that data does not exist yet.
    /// </para>
    ///
    /// <para>
    /// <b>Interim, and known to be.</b> This floor is the bottom rung of three, and it only ever
    /// gets reached because an EARLIER unit overshot — it limits the damage rather than repairing
    /// it. The rung above (gh-#320, the straddle handoff) is the real answer for the band where
    /// room is positive but no track fits: sign off into a track that crosses the hour and sign on
    /// after it, which is what a live DJ does when the rotation traps them. Until that exists, this
    /// floor holds the middle band at "up to 90s early" instead of "up to ~2 minutes late", and the
    /// trade above 90s is still lateness — the honest bound, not a fix. Once gh-#320 lands, the
    /// straddle owns that band and this floor should collapse toward zero: bare-ceremony is right
    /// only once the boundary is genuinely unreachable.
    /// </para>
    /// </summary>
    static readonly TimeSpan MusicUnitFloor = TimeSpan.FromSeconds(90);

    // SPEC F92.4 (PLAN T124): the same null-coalesced-default idiom MusicSelectionPolicy's own
    // envelope/persona/request-fulfillment seams use (F112, STORY-295) — a dropped handoff piece
    // still needs somewhere to publish to even when no host binds a real sink (every pre-T124
    // construction site keeps compiling and behaving exactly as before).
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    // gh-#253: the patter-duration estimation seam — same default idiom as the fields above, but a
    // fresh per-Orchestrator instance rather than a shared NoOp: the default estimator carries
    // rolling state, and sharing one static instance across constructions would bleed one test's
    // (or one hypothetical second station's) observed history into another's estimates.
    readonly IPatterDurationEstimator patterEstimator = patterEstimator ?? new RollingPatterDurationEstimator();

    readonly Queue<MediaItem> buffer = new();
    MediaItem? previousTrack;
    int unitCount;

    // SPEC F92.1/F92.3 arm-once state (T124 review finding F2): the (BoundaryAt, outgoing persona
    // id, incoming persona id) triple EnqueueHandoffCeremonyAsync last acted on — null before the
    // first unit, or once a boundary has left the window and was explicitly cleared. Re-evaluating
    // this producer every unit is by design (a schedule write must be noticed promptly), but ACTING
    // on the SAME triple twice is not: see EnqueueHandoffCeremonyAsync's own remarks for the
    // double-sign-off bug this guards against.
    (DateTimeOffset BoundaryAt, long? OutgoingPersonaId, long? IncomingPersonaId)? lastArmedHandoff;

    // T124 review finding F7: fires at most once for the life of this Orchestrator — a null
    // scheduleResolver makes EnqueueHandoffCeremonyAsync a permanent no-op, which would otherwise be
    // completely silent (no format-clock schedule wired is a perfectly valid, common station shape,
    // but an operator who DID intend to wire one deserves one loud signal that it never arrived).
    bool scheduleResolverMissingWarned;

    /// <inheritdoc/>
    public async Task<MediaItem?> GetNextAsync(PlayoutContext ctx, CancellationToken ct)
    {
        if (buffer.Count > 0) return buffer.Dequeue();

        // Read cadence and station identity ONCE per unit, up front — the gitea-#211/F44.1
        // disciplines, hoisted here from EnqueuePatterAsync (gh-#254) so the boundary fit's patter
        // estimates and this unit's actual patter planning read the SAME snapshot: still exactly one
        // read of each per unit, just taken before selection instead of after it. Never read
        // cadenceProvider.Current or identityProvider.Current again below this line.
        var cadence = cadenceProvider.Current;
        var identity = identityProvider.Current;

        // gh-#259's one-accessor-read-per-unit attribution stamp, resolved BEFORE selection as of
        // gh-#254 (same single read, just earlier): the boundary fit keys its persona-owned patter
        // estimates by the unit's show persona.
        var unitDjName = await ResolveUnitDjNameAsync(ct);

        // Strip tts:* from the recent-ids list (F12.6 discipline) so the ordered-recent list
        // GetRotationCandidateAsync tiers against stays music-only. ctx.RecentMediaIds is already
        // the feeder's ring oldest-first, most-recent LAST (SPEC F41.1) — Where preserves that order.
        var orderedRecentIds = ctx.RecentMediaIds
            .Where(id => !id.StartsWith("tts:", StringComparison.Ordinal))
            .ToList();

        // Read the live scope and artist-separation depth on every selection call — never store
        // either — so a live scope edit (SPEC F30) or rotation edit (F41.6) takes effect on the
        // very next pull with no process restart.
        var artistSeparation = rotationProvider.Current.ArtistSeparation;

        // gh-#254's fit, built ONCE per unit and read twice: first by the gh-#300 decline check
        // immediately below, then by the sampler it was originally written for. Null whenever no
        // deferral sits strictly-future inside the F74.3 lookahead window — the common case, in
        // which both readers degrade to exactly their pre-boundary-awareness behavior.
        var pending = deferralQueue.PeekNextDue();
        var untilDue = pending is null ? default : pending.Due - timeProvider.GetUtcNow();
        var fit = pending is not null && untilDue > TimeSpan.Zero && untilDue <= boundaryBiasProvider.Current
            ? BuildBoundaryFit(pending, untilDue, cadence, identity, unitDjName, ctx.QueuedAheadMs)
            : null;

        // gh-#300 — the last unit before a due ceremony IS the ceremony. When no music unit can fit
        // in front of the boundary, plan the ceremony instead of a full track nobody has room for;
        // a returned segment ends this pull with no music planned at all.
        //
        // This sits ABOVE rung -1 (SPEC F87.6's request fulfillment, consulted inside
        // MusicSelectionPolicy.SelectMusicCandidateAsync), which is deliberate and safe: a declined
        // unit plays no music, so there is no slot for a requested track either. Rung -1's "exactly
        // once per pick" contract is about never CAS-stamping twice — a pick that never happens
        // stamps nothing, so the pending request simply waits for the next unit with its row untouched.
        if (fit is not null && ShouldDeclineFinalUnit(fit)
            && await TryServeCeremonyOnlyUnitAsync(fit, unitDjName, cadence, identity, ct) is { } ceremony)
        {
            return ceremony;
        }

        // F112 (STORY-295, PLAN T218): the pick ladder itself lives on MusicSelectionPolicy —
        // logBoundaryFit is this Orchestrator's own LogBoundaryFit, threaded in so every outcome
        // line the resample loop logs still lands on the SAME Information sink the ceremony-decline
        // path ("declined") uses (see MusicSelectionPolicy.SelectMusicCandidateAsync's own remarks).
        var candidate = await musicSelectionPolicy.SelectMusicCandidateAsync(
            scopeProvider.Current, orderedRecentIds, artistSeparation, fit, LogBoundaryFit, ct);
        if (candidate is null)
        {
            // F41.2: null now means a GENUINE drain — zero playable rows in scope, never merely
            // "everything playable happens to be recent". Non-fatal (F6.3 stands) — the feeder
            // retries next tick — but loud, since gitea-#210's silent version of this is the bug closed.
            logger.LogWarning(
                "Rotation selection found zero playable tracks in scope — a genuine drain " +
                "(SPEC F41.2), distinct from an anti-repeat or artist-separation adjustment.");
            return null;
        }

        if (candidate.RepeatedRecent)
        {
            logger.LogWarning(
                "Anti-repeat window relaxed — playable catalog smaller than the recent window; " +
                "selected {MediaId} despite it appearing in the recent list (SPEC F41.5).",
                candidate.Media.MediaId);
        }

        if (candidate.RepeatedArtist)
        {
            logger.LogWarning(
                "Artist-separation relaxed — no track avoided the last {ArtistSeparation} artists; " +
                "selected {MediaId} with a repeated artist (SPEC F41.5).",
                artistSeparation, candidate.Media.MediaId);
        }

        var track = candidate.Media.ToMediaItem();

        // Carries SPEC F82.6/F83.1's persona-pick diagnostics from the selection-time RotationCandidate
        // onto the playout-facing MediaItem (T65's staged carrier — see RotationCandidate.PersonaPick's
        // own remarks) — null for every envelope-only pick, including the common persona-off case.
        if (candidate.PersonaPick is { } personaPickDiagnostics)
            track = track with { PersonaPick = personaPickDiagnostics };

        // SPEC F87.6/F87.7 marker vehicle to a future copywriter consumer (T91) — rides the same
        // RotationCandidate -> MediaItem carry-through PersonaPick just used, immediately above.
        if (candidate.RequestFulfilled)
            track = track with { RequestFulfilled = true };

        // gh-#259: stamp Now Playing attribution at PLAN time, onto the item itself — the single
        // per-unit accessor read resolved above (it also warms the F93.1 display-name memo every
        // unit, cadence config regardless). The spectator surface reads this off the AIRING item,
        // so after a schedule boundary the displayed DJ keeps naming whoever's queued items are
        // still draining and flips only when the new show's items actually reach air — never the
        // schedule's live answer.
        track = track with { DjName = unitDjName };

        await EnqueuePatterAsync(previousTrack, track, unitDjName, cadence, identity, ct);
        buffer.Enqueue(track);

        previousTrack = track;
        unitCount++;

        return buffer.Dequeue();
    }

    /// <summary>
    /// gh-#254 — turns "a deferral is due in <paramref name="untilDue"/>" into the effective track
    /// length the sampler above should aim for, plus the tolerance a landing counts as a win at.
    /// All in relative time from the injected clock's "now"; every patter term is a gh-#253
    /// estimate, with the WORST contributing confidence tier setting the tolerance.
    ///
    /// <para>
    /// The accounting, in air order:
    /// <list type="bullet">
    /// <item><b>queued-ahead drift</b> (<paramref name="queuedAheadMs"/>, the feeder's own
    /// measurement — <see langword="null"/> = unknown = zero): the candidate does not start at
    /// "now", it starts after everything already committed ahead of this planning pass — the exact
    /// drift gh-#254's live repro named.</item>
    /// <item><b>this unit's own pre-music patter</b>: the segments EnqueuePatterAsync will plan
    /// between now and the candidate's first note — back-announce (when cadence says so and a
    /// previous track exists), a station ID (this unit's own cadence trigger, evaluated with the
    /// same guard the real check below uses), and the lead-in.</item>
    /// <item><b>the candidate itself</b>: scored by the caller as measured duration minus
    /// <c>MusicSelectionPolicy.ExpectedCrossfadeTrim</c>.</item>
    /// <item><b>the break's pre-boundary patter</b>: the back-announce that will open the NEXT
    /// unit, plus — when the pending deferral is a <see cref="SpeechDeferralKind.SignOff"/> — the
    /// sign-off piece itself (estimated for the OUTGOING persona from the deferral's own captured
    /// <see cref="HandoffContext"/>, the F92.2 source of truth). A sign-on (and the lead-in that
    /// follows it) airs on the far side of the boundary and deliberately never counts.</item>
    /// </list>
    /// For a SignOff-headed fit the boundary instant is recovered as <c>Due + SignOffLeadTime</c>
    /// (the exact inverse of how EnqueueHandoffCeremonyAsync armed it); every other kind's due IS
    /// its boundary. The result can go negative when the approach has already overshot — the
    /// caller's min-diff then simply prefers the least-late sample, which is the correct radio move
    /// (the observed 5–6-minute-late handover is the failure mode, not a slightly-early break).
    /// </para>
    /// </summary>
    BoundaryFitPlan BuildBoundaryFit(
        SpeechDeferral pending,
        TimeSpan untilDue,
        CadenceConfig cadence,
        StationIdentity identity,
        string? unitDjName,
        int? queuedAheadMs)
    {
        var worstConfidence = PatterEstimateConfidence.Exact;

        TimeSpan Estimate(SegmentKind kind, string? personaName, string voice)
        {
            var estimate = patterEstimator.Estimate(kind, personaName, voice);
            if (estimate.Confidence > worstConfidence) worstConfidence = estimate.Confidence;
            return estimate.Duration;
        }

        var untilBoundary = pending.Kind == SpeechDeferralKind.SignOff
            ? untilDue + SignOffLeadTime
            : untilDue;

        var breakPatter = TimeSpan.Zero;
        if (cadence.BackAnnounceAfterEachTrack)
            breakPatter += Estimate(SegmentKind.BackAnnounce, unitDjName, identity.Voice);
        if (pending.Kind == SpeechDeferralKind.SignOff && pending.Handoff is { } handoff)
            breakPatter += Estimate(SegmentKind.SignOff, handoff.PersonaName, handoff.Voice);

        var preMusicPatter = TimeSpan.Zero;
        if (cadence.BackAnnounceAfterEachTrack && previousTrack is not null)
            preMusicPatter += Estimate(SegmentKind.BackAnnounce, unitDjName, identity.Voice);
        if (cadence.StationIdEveryNUnits > 0 && unitCount > 0 && unitCount % cadence.StationIdEveryNUnits == 0)
            preMusicPatter += Estimate(SegmentKind.StationId, personaName: null, identity.Voice);
        if (cadence.LeadInBeforeEachTrack)
            preMusicPatter += Estimate(SegmentKind.LeadIn, unitDjName, identity.Voice);

        var queuedAhead = TimeSpan.FromMilliseconds(queuedAheadMs ?? 0);
        var desired = untilBoundary - breakPatter - queuedAhead - preMusicPatter;

        var tolerance = worstConfidence switch
        {
            PatterEstimateConfidence.Exact => FitToleranceExact,
            PatterEstimateConfidence.Historical => FitToleranceHistorical,
            _ => FitToleranceHeuristic,
        };

        return new BoundaryFitPlan(
            desired, tolerance, pending.Kind, untilBoundary, queuedAhead, preMusicPatter, breakPatter,
            worstConfidence);
    }

    /// <summary>
    /// gh-#300 — "the last unit before a due ceremony IS the ceremony". True when the room left in
    /// front of the boundary is under <see cref="MusicUnitFloor"/>, in which case planning one more
    /// full track is strictly worse than planning none (see that constant for the arithmetic).
    ///
    /// <para>
    /// <b>Handoff kinds only.</b> A show boundary is an appointment the audience can hear being
    /// missed — the incoming DJ announcing "it's Thursday two o'clock" at 2:05 is the whole issue.
    /// A station ID is not: it is imaging that can ride the next seam quite happily, and skipping a
    /// whole track for one would trade a small blemish for a large one. Today's ident producer only
    /// ever enqueues due-NOW deferrals, so such a fit is never even built (a due-now deferral takes
    /// the plain unbiased path) — this guard is what keeps that true if a future producer ever
    /// future-dates one.
    /// </para>
    /// </summary>
    bool ShouldDeclineFinalUnit(BoundaryFitPlan fit) =>
        fit.Kind is SpeechDeferralKind.SignOff or SpeechDeferralKind.SignOn
        && fit.DesiredEffectiveLength < MusicUnitFloor;

    /// <summary>
    /// gh-#300 — plans the ceremony as a unit of its own: back-announce (the fit already reserved
    /// it) plus whatever the drain yields, and no music.
    ///
    /// <para>
    /// <b>The drain runs as-of the BOUNDARY, not "now".</b> A SignOff comes due at
    /// <c>boundary - SignOffLeadTime</c>, so at the moment this decision is taken it is still a few
    /// seconds in the future and an as-of-now drain would return nothing — which is precisely the
    /// bug: the ceremony then waited for a pull that a freshly-planned three-and-a-half-minute track
    /// had just pushed past the hour. Draining as-of the boundary also keeps both halves together in
    /// ONE <see cref="SpeechDeferralQueue.TryDequeueDue"/> call, the shape
    /// <see cref="SignOffLeadTime"/>'s own remarks describe as the overwhelmingly common case.
    /// </para>
    ///
    /// <para>
    /// <b>Planning early is not airing early.</b> The ceremony is appended behind
    /// <c>QueuedAheadMs</c> of audio that is still draining, so it reaches air roughly when that
    /// audio runs out — at the boundary. Never-silent (F6.3) is untouched either way: this method
    /// only ever ADDS segments, and a unit that renders nothing at all returns
    /// <see langword="null"/> so the caller plans an ordinary music unit instead, exactly as if the
    /// decline had never fired.
    /// </para>
    ///
    /// <para>
    /// <see cref="previousTrack"/> and <see cref="unitCount"/> are deliberately NOT advanced — no
    /// music played, so the next unit's back-announce still refers to the track that really did,
    /// and the station-ID cadence still counts music units rather than being nudged by a ceremony.
    /// </para>
    /// </summary>
    async Task<MediaItem?> TryServeCeremonyOnlyUnitAsync(
        BoundaryFitPlan fit, string? unitDjName, CadenceConfig cadence, StationIdentity identity,
        CancellationToken ct)
    {
        // ONE line, not two: the fit line already carries every term (desired, queuedAhead, the
        // lot), so a second human-readable "declining because…" would restate it. The floor is the
        // only fact the fit itself does not know, so it rides the outcome.
        LogBoundaryFit(
            fit,
            $"declined (floor={MusicUnitFloor.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s)",
            sampled: [],
            chosenDiff: null);

        var boundary = timeProvider.GetUtcNow() + fit.UntilBoundary;
        await EnqueuePatterAsync(previousTrack, next: null, unitDjName, cadence, identity, ct, boundary);

        return buffer.Count > 0 ? buffer.Dequeue() : null;
    }

    /// <summary>
    /// gh-#300 — the one line that makes a boundary fit arguable after the fact. The 2:05 handoff
    /// was reconstructible only from kokoro's own render timestamps because this method did not
    /// exist; every term the fit reasoned from is now on the record, alongside what the sampler did
    /// with it.
    ///
    /// <para>
    /// <b>INFORMATION, deliberately.</b> The sibling per-pick "Pick —" line is Debug, and the demo
    /// fleet ships Information and above — a fact confirmed by querying it: zero <c>dbug:</c> lines
    /// exist in Loki. A Debug fit line would satisfy the issue's letter and none of its purpose.
    /// The volume is affordable because this fires only while a deferral sits inside the F74.3
    /// lookahead window — a handful of lines per boundary, not one per pick.
    /// </para>
    /// </summary>
    void LogBoundaryFit(BoundaryFitPlan fit, string outcome, IReadOnlyList<TimeSpan> sampled, TimeSpan? chosenDiff)
    {
        static string Secs(TimeSpan value) => value.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);

        logger.LogInformation(
            "Boundary fit ({Kind}) — untilBoundary={UntilBoundary}s queuedAhead={QueuedAhead}s " +
            "preMusicPatter={PreMusicPatter}s breakPatter={BreakPatter}s desired={Desired}s " +
            "tolerance=±{Tolerance}s confidence={Confidence} sampled=[{Sampled}] " +
            "chosenDiff={ChosenDiff} outcome={Outcome}",
            fit.Kind, Secs(fit.UntilBoundary), Secs(fit.QueuedAhead), Secs(fit.PreMusicPatter),
            Secs(fit.BreakPatter), Secs(fit.DesiredEffectiveLength), Secs(fit.Tolerance),
            fit.Confidence, string.Join(", ", sampled.Select(Secs)),
            chosenDiff is { } diff ? Secs(diff) + "s" : "n/a", outcome);
    }

    /// <param name="cadence">
    /// The unit's ONE cadence snapshot (gitea-#211) — read by <see cref="GetNextAsync"/> at the top
    /// of the unit (hoisted there by gh-#254 so the boundary fit shares it) and handed in, so this
    /// unit's back-announce/station-id/lead-in decisions all see the same snapshot even if a live
    /// PUT /api/settings edit lands mid-unit. Never read <see cref="cadenceProvider"/> in here.
    /// </param>
    /// <param name="identity">
    /// The unit's ONE station-identity snapshot (SPEC F44.1, gitea-#196) — same discipline, same
    /// gh-#254 hoist as <paramref name="cadence"/>: a live Station:Name/Station:Voice edit must not
    /// straddle a single unit's segment builds. Never read <see cref="identityProvider"/> in here.
    /// </param>
    /// <param name="next">
    /// The music this unit is planning patter around — <see langword="null"/> on gh-#300's
    /// ceremony-only unit, where there is no music to lead into. Null suppresses exactly two things:
    /// the lead-in (nothing to introduce) and the station-ID cadence check (a ceremony is not a
    /// music unit, and firing that check here would both wedge an ident into the handoff and, since
    /// <see cref="unitCount"/> deliberately does not advance, fire it again on the very next unit).
    /// The back-announce still runs: the track that just played deserves its outro, and
    /// <see cref="BuildBoundaryFit"/> has already reserved the time for it.
    /// </param>
    /// <param name="drainAsOf">
    /// The instant the deferral drain is evaluated against — <see langword="null"/> means "now",
    /// every pre-gh-#300 caller's behavior. The ceremony-only unit passes the BOUNDARY instead; see
    /// <see cref="TryServeCeremonyOnlyUnitAsync"/> for why an as-of-now drain is the exact shape of
    /// the bug.
    /// </param>
    async Task EnqueuePatterAsync(
        MediaItem? prev, MediaItem? next, string? unitDjName, CadenceConfig cadence, StationIdentity identity,
        CancellationToken ct, DateTimeOffset? drainAsOf = null)
    {
        // Read the render budget ONCE per unit, up front (SPEC F44.2, gitea-#197) — the same
        // per-unit-snapshot discipline cadence/identity arrive under (see the params above): a
        // live Tts:RenderBudgetSeconds edit must not straddle a single unit's renders. Never read
        // renderBudgetProvider.Current again below this line.
        var renderBudget = renderBudgetProvider.Current;

        // Each segment's voice+persona-name pair is resolved (a fast, local accessor call — SPEC
        // F35.3, F39.1) immediately before that segment's SegmentRequest is built, so the actual TTS
        // renders below still all kick off back-to-back with no render awaited in between
        // (render-ahead is unaffected — the accessor call is negligible next to a real render's
        // synthesis+mix+measure latency). ResolvePersonaAsync reads personaAccessor exactly ONCE per
        // call, returning both values from the same read (F39.1) — never resolve Voice and
        // PersonaName from two separate accessor calls, which could straddle a concurrent
        // activate/deactivate and pair a stale name with a fresh voice or vice versa.
        // The full request rides alongside each render task — Kind for the F92.4 drop
        // classification (SPEC F92.4, PLAN T124: the await loop below tells a handoff-kind drop,
        // WARN + booth row, from every other kind's ordinary silent skip), and Voice/PersonaName for
        // the gh-#253 measured-duration observation a successful render feeds the estimator — the
        // render itself is still kicked off immediately here, nothing awaited in between.
        var pendingRenders = new List<(SegmentRequest Request, Task<MediaItem?> Render)>();

        // Starts one render and remembers the request alongside the Task (T124 review simplify) —
        // every call site below used to repeat the Add call verbatim; the render itself is still
        // kicked off immediately, nothing awaited in between.
        void Kick(SegmentRequest request) => pendingRenders.Add((request, tts.RenderAsync(request, ct)));

        // 1. Back-announce for the previous track
        if (cadence.BackAnnounceAfterEachTrack && prev is not null)
        {
            var (voice, personaName) = await ResolvePersonaAsync(identity.Voice, ct);
            var req = new SegmentRequest(
                SegmentKind.BackAnnounce,
                voice,
                identity.Name,
                prev,
                StationLocalNow(),
                identity.Id,
                personaName);
            Kick(req);
        }

        // 2. Station ID every N units (checked BEFORE incrementing unitCount). unitCount > 0 joins
        // the guard (SPEC F42.1, STORY-136, closes gitea-#216): the FIRST station ID airs only once N
        // units have elapsed, never at boot — unitCount == 0 % N == 0 used to fire on the very
        // first unit, which is exactly the boot-blast this guard now excludes.
        //
        // The trigger no longer builds the segment itself (SPEC F74.1/F74.2, STORY-197): it
        // enqueues a deferral, and the drain immediately below picks it up in this SAME boundary
        // pass (see class remarks for why that is still "never mid-track"). Supersede (F74.2) is
        // the queue's job, not this check's — a second same-kind enqueue before the next drain
        // would simply replace this one.
        // next is null on gh-#300's ceremony-only unit — see this method's own param remarks for why
        // an ident must not be triggered by a unit that plans no music.
        if (next is not null
            && cadence.StationIdEveryNUnits > 0
            && unitCount > 0
            && unitCount % cadence.StationIdEveryNUnits == 0)
        {
            deferralQueue.Enqueue(SpeechDeferralKind.StationId, "cadence: Station:Cadence:StationIdEveryNUnits");
        }

        // Drain every deferral due at this boundary — BEFORE the handoff producer below runs (T124
        // review finding). Reads the SAME injected clock GetNextAsync compares NextDue against
        // (SPEC F74.3) — one clock for both halves of this seam, never a mix of a real and a fake
        // one. Written for ANY due deferral, including one enqueued several units ago (SPEC
        // F74.1 — "regardless of wall-clock slip").
        foreach (var deferral in deferralQueue.TryDequeueDue(drainAsOf ?? timeProvider.GetUtcNow()))
        {
            switch (deferral.Kind)
            {
                case SpeechDeferralKind.StationId:
                    // Station IDs are station imaging (gh-#96): ALWAYS the station's own voice and
                    // credit, never the active persona's — real-radio convention, the ID is the brand
                    // speaking, not the DJ. Deliberately no ResolvePersonaAsync here (LeadIn/BackAnnounce
                    // below stay persona-voiced), and deliberately not solved by touching
                    // Station:Persona:ActiveId — a future multi-DJ scheduler slots personas in and out,
                    // and imaging must stay the station's voice regardless of who is in the chair.
                    // PersonaName stays null so the airing credits the station
                    // (TtsSegmentSource: Artist = PersonaName ?? StationName). The TTS cache key
                    // contains the voice, so a live Station:Voice edit re-keys and re-renders the ID at
                    // its next slot with no regen tooling.
                    var stationIdReq = new SegmentRequest(
                        SegmentKind.StationId,
                        identity.Voice,
                        identity.Name,
                        null,
                        StationLocalNow(),
                        identity.Id,
                        PersonaName: null);
                    Kick(stationIdReq);
                    break;

                case SpeechDeferralKind.SignOff:
                case SpeechDeferralKind.SignOn:
                    // SPEC F92.2 (PLAN T124): built from the deferral's OWN captured HandoffContext —
                    // NEVER a fresh ResolvePersonaAsync/accessor read here — see HandoffContext's own
                    // remarks for why (a piece can drain after the wall clock has already flipped past
                    // the boundary, when the accessor would answer with the WRONG persona). A deferral
                    // of this kind is never enqueued without one (EnqueueHandoffCeremonyAsync always
                    // supplies it) — the null-check below is defensive only.
                    if (deferral.Handoff is not { } handoff) break;
                    var handoffKind = deferral.Kind == SpeechDeferralKind.SignOff
                        ? SegmentKind.SignOff : SegmentKind.SignOn;
                    var handoffReq = new SegmentRequest(
                        handoffKind,
                        handoff.Voice,
                        identity.Name,
                        null,
                        StationLocalNow(),
                        identity.Id,
                        handoff.PersonaName,
                        handoff.CounterpartName);
                    Kick(handoffReq);
                    break;
            }
        }

        // 2.5. Handoff ceremony producer (SPEC F92.1-F92.6, STORY-243, PLAN T124) — runs every unit,
        // independent of the cadence config above, AFTER the drain just above (T124 review finding):
        // the moment the wall clock reaches an already-armed boundary, the resolver's own "current
        // segment" flips to the INCOMING one, which makes the NEXT boundary (that new segment's own
        // end) look far away and out of window — evaluating this producer first would clear the very
        // SignOff/SignOn the drain above was about to fire, the instant they became due. Draining
        // first, then arming/clearing for what comes next, means an already-due ceremony always gets
        // its chance to air before this producer ever re-evaluates the (now different) boundary
        // ahead. See the method's own remarks for the F92.3 dedupe rules and the window-exit clear.
        await EnqueueHandoffCeremonyAsync(identity.Voice, ct);

        // 3. Lead-in for the next track — skipped entirely on a ceremony-only unit (gh-#300): there
        // is no next track to introduce, and the sign-on's own copy is the handoff's lead-in.
        if (next is not null && cadence.LeadInBeforeEachTrack)
        {
            var (voice, personaName) = await ResolvePersonaAsync(identity.Voice, ct);
            var req = new SegmentRequest(
                SegmentKind.LeadIn,
                voice,
                identity.Name,
                next,
                StationLocalNow(),
                identity.Id,
                personaName);
            Kick(req);
        }

        // Await each render with the budget; skip any that time out, fault, or return null. A
        // handoff-kind (SignOff/SignOn) drop additionally logs a WARN + booth-log entry (SPEC F92.4)
        // — every other kind's drop stays the pre-existing silent skip. Classified from the
        // COMPLETED task's own state below, never from which race member <c>Task.WhenAny</c> named
        // the winner (T124 review finding F6): <c>Task.WhenAny</c> completes successfully the moment
        // EITHER task completes, fault or not — it never throws or otherwise signals "the winner
        // faulted," so a ternary keyed on "did renderTask win the race" mislabeled every synth outage
        // that happened to beat the budget delay as "render returned null" instead of "render
        // faulted".
        foreach (var (request, renderTask) in pendingRenders)
        {
            var kind = request.Kind;
            var winner = await Task.WhenAny(renderTask, Task.Delay(renderBudget, ct));

            if (winner != renderTask)
            {
                if (kind is SegmentKind.SignOff or SegmentKind.SignOn)
                    LogHandoffDrop(kind, "render budget exceeded");
                continue; // timed out — the still-running render is left unawaited, unchanged behavior
            }

            if (renderTask.IsCompletedSuccessfully && renderTask.Result is { } seg)
            {
                // gh-#253: feed the MEASURED duration (F66.1's cue-derived stamp — null when cue
                // analysis failed, in which case nothing is observed: never fabricated) back into
                // the estimation seam, keyed by the request's own kind/persona/voice, so the
                // historical tier self-improves with every segment that actually rendered.
                if (seg.DurationMs is int measuredMs)
                    patterEstimator.ObserveRendered(
                        kind, request.PersonaName, request.Voice, TimeSpan.FromMilliseconds(measuredMs));

                // gh-#259: a station ID keeps the station's CREDIT (Artist, gh-#96 untouched) but
                // still airs inside the unit's show — stamp the unit persona so Now Playing
                // attribution never flickers to "no DJ" for a few seconds of imaging mid-show.
                // Every other kind already carries its own speaker's name from TtsSegmentSource
                // (SegmentRequest.PersonaName — the handoff kinds' outgoing/incoming included).
                if (kind == SegmentKind.StationId)
                    seg = seg with { DjName = unitDjName };
                buffer.Enqueue(seg);
            }
            else if (kind is SegmentKind.SignOff or SegmentKind.SignOn)
            {
                LogHandoffDrop(kind, renderTask.IsFaulted ? "render faulted" : "render returned null");
            }
            // else: renderTask completed with a null segment → silently skip (every non-handoff kind)
        }
    }

    /// <summary>
    /// SPEC F92.1/F92.3 (STORY-243, PLAN T124): arms the two-piece handoff ceremony once
    /// <paramref name="scheduleResolver"/>'s resolved <c>OnAirSnapshot.BoundaryAt</c> enters
    /// <paramref name="boundaryBiasProvider"/>'s F74.3 lookahead window — the SAME window
    /// <see cref="GetNextAsync"/> already reads to build the fit
    /// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> consumes, so "in window" means one
    /// thing station-wide. A <see langword="null"/> <paramref name="scheduleResolver"/> (no format-clock
    /// schedule wired, the pre-F91 station shape) makes this a permanent no-op — logged with ONE WARN
    /// on the very first unit (T124 review finding F7) so that inert case is never silent, then never
    /// again for the life of this Orchestrator.
    ///
    /// <para>
    /// <b>Arm once per triple, never every unit (T124 review finding F2 — the double-sign-off bug this
    /// fixes):</b> this producer runs on EVERY unit while a boundary sits in-window, but it only ever
    /// ACTS the first time it sees a given <c>(BoundaryAt, outgoing persona id, incoming persona id)</c>
    /// triple — <see cref="lastArmedHandoff"/> remembers the last one it armed or cleared for, and an
    /// unchanged triple returns immediately, touching neither <paramref name="deferralQueue"/> nor
    /// <paramref name="personaStore"/> again. Without this, re-running the OLD unconditional
    /// enqueue-every-unit logic on a seam landing in <c>[BoundaryAt - SignOffLeadTime, BoundaryAt)</c>
    /// would: drain SignOff at this unit (its due has arrived) — see it drain, then IMMEDIATELY
    /// re-<see cref="SpeechDeferralQueue.Enqueue"/> a FRESH SignOff for the very same boundary with a
    /// due time that is now itself already in the past (the resolver's "current" segment has not yet
    /// flipped, so <c>BoundaryAt</c>/the persona ids still read identically) — which the NEXT unit's
    /// drain would fire AGAIN, a second sign-off airing for one boundary. The two elapsed-due guards
    /// below (skip arming SignOff once <c>BoundaryAt - SignOffLeadTime &lt;= now</c>; skip arming
    /// SignOn once <c>BoundaryAt &lt;= now</c>) are the belt to this triple-check's suspenders: a
    /// piece is never handed to <see cref="SpeechDeferralQueue.Enqueue"/> with a due time that has
    /// already elapsed, full stop, even on the very first unit a triple is ever seen.
    /// </para>
    ///
    /// <para>
    /// A CHANGED triple — the common case is a schedule write moving the boundary, or the resolver's
    /// own "current" segment finally flipping to the incoming one once <c>now</c> passes the old
    /// boundary — re-arms fresh: <see cref="SpeechDeferralQueue.Enqueue"/>'s own supersede-by-kind
    /// (SPEC F74.2) discards whatever the OLD triple left pending of the same kind, and this method's
    /// own <c>ClearCeremony</c> local retracts anything the old triple armed that the new one has no
    /// replacement for (window exit, gap-to-gap, self-handoff — see the dedupe list below). Nothing
    /// here is left to expire on its own.
    /// </para>
    ///
    /// <para>
    /// <b>Why <see cref="EnqueuePatterAsync"/> calls this AFTER the deferral drain, not before (T124
    /// review finding):</b> the resolver's <c>OnAirSnapshot</c> is defined by wall-clock "now," so the
    /// instant "now" reaches an already-armed boundary, <c>ResolveAsync</c>'s own idea of the CURRENT
    /// segment flips to the INCOMING one — which makes the boundary THIS method would compute next
    /// (that new segment's own end) look far away, outside the window. Calling this before the drain
    /// would clear the very SignOff/SignOn deferrals the drain was about to fire, in the same pass
    /// they finally became due — a real defect this ordering fixes: drain whatever a PRIOR unit armed
    /// first, THEN decide what (if anything) to arm or clear for what comes next. A genuine schedule
    /// WRITE that moves a boundary away is unaffected — it is detected and cleared on some EARLIER
    /// unit, well before the (moved) due time would ever have elapsed.
    /// </para>
    ///
    /// <para>
    /// <b>Dedupe (SPEC F92.3, the T119-review build clarification):</b> the resolver's own
    /// <c>BoundaryAt</c>/<c>NextSegment</c> stay row-accurate even across a same-persona adjacency —
    /// THIS method is where "no ceremony airs" for that case is decided, never the resolver. Five
    /// shapes, by outgoing/incoming persona id:
    /// <list type="bullet">
    /// <item>both null (a genuine gap, or a gap followed by an explicit persona-less/music-only
    /// scheduled segment) — gap-to-gap: nothing airs.</item>
    /// <item>equal and non-null (the F91.6 seeded grid's own midnight roll) — self-handoff: nothing
    /// airs.</item>
    /// <item>outgoing non-null, incoming null — SignOff only, <see cref="HandoffContext.CounterpartName"/>
    /// null ("the music keeps rolling").</item>
    /// <item>outgoing null, incoming non-null — SignOn only, <see cref="HandoffContext.CounterpartName"/>
    /// null ("no predecessor").</item>
    /// <item>both non-null and different — both pieces, each naming the other.</item>
    /// </list>
    /// A persona id present on the schedule row but unresolvable through <paramref name="personaStore"/>
    /// (deleted out of band) degrades that HALF to "no DJ" (never-throws, SPEC F12.4) — the OTHER
    /// half still enqueues if it has one; see <see cref="ResolveHandoffPersonaAsync"/>.
    /// </para>
    /// </summary>
    async Task EnqueueHandoffCeremonyAsync(string stationVoice, CancellationToken ct)
    {
        if (scheduleResolver is null)
        {
            if (!scheduleResolverMissingWarned)
            {
                scheduleResolverMissingWarned = true;
                logger.LogWarning(
                    "No CachingScheduleResolver wired — the handoff ceremony producer (SPEC F92.1) is " +
                    "a permanent no-op for this process (no format-clock schedule in play, the pre-F91 " +
                    "station shape). Logged once.");
            }
            return;
        }

        // Clears both SignOff/SignOn (SPEC F92.1 revisit) — the one action every "no ceremony airs"
        // branch below shares, so the dedupe matrix in this method's own remarks reads as a matrix of
        // conditions rather than five repeated two-line clear blocks (T124 review simplify).
        void ClearCeremony()
        {
            deferralQueue.Clear(SpeechDeferralKind.SignOff);
            deferralQueue.Clear(SpeechDeferralKind.SignOn);
        }

        var onAir = await scheduleResolver.ResolveAsync(ct);
        var boundaryAt = onAir.BoundaryAt;
        var now = timeProvider.GetUtcNow();
        var untilBoundary = boundaryAt is { } b ? b - now : (TimeSpan?)null;

        if (boundaryAt is null || untilBoundary is not { } gap || gap <= TimeSpan.Zero || gap > boundaryBiasProvider.Current)
        {
            if (lastArmedHandoff is not null)
            {
                ClearCeremony();
                lastArmedHandoff = null;
            }
            return;
        }

        var outgoingId = onAir.PersonaId;
        var incomingId = onAir.NextSegment?.PersonaId;
        var triple = (boundaryAt.Value, outgoingId, incomingId);

        // Arm-once (T124 review finding F2): this exact triple was already armed/cleared by a prior
        // unit — nothing has changed, so touch neither the queue nor personaStore again.
        if (lastArmedHandoff == triple) return;
        lastArmedHandoff = triple;

        if (outgoingId is null && incomingId is null)
        {
            ClearCeremony(); // gap-to-gap
            return;
        }

        if (outgoingId is not null && outgoingId == incomingId)
        {
            // F92.3 build clarification: same persona on both sides of a row-accurate boundary airs
            // no ceremony at all — never even attempted, so this never shows up as a "drop" either.
            ClearCeremony(); // self-handoff
            return;
        }

        var outgoing = await ResolveHandoffPersonaAsync(outgoingId, stationVoice, ct);
        var incoming = await ResolveHandoffPersonaAsync(incomingId, stationVoice, ct);

        // Never hand the queue a piece whose due time has already elapsed (T124 review finding F2's
        // belt-and-suspenders guard) — a boundary can enter the window with less than SignOffLeadTime
        // left on the clock, in which case the SignOff half is simply skipped, never armed stale.
        var signOffDue = boundaryAt.Value - SignOffLeadTime;
        if (outgoing is null || signOffDue <= now)
        {
            deferralQueue.Clear(SpeechDeferralKind.SignOff);
        }
        else
        {
            deferralQueue.Enqueue(
                SpeechDeferralKind.SignOff,
                "handoff: boundary entered the F74.3 window",
                signOffDue,
                new HandoffContext(outgoing.Value.Voice, outgoing.Value.Name, incoming?.Name));
        }

        if (incoming is null || boundaryAt.Value <= now)
        {
            deferralQueue.Clear(SpeechDeferralKind.SignOn);
        }
        else
        {
            deferralQueue.Enqueue(
                SpeechDeferralKind.SignOn,
                "handoff: boundary entered the F74.3 window",
                boundaryAt.Value,
                new HandoffContext(incoming.Value.Voice, incoming.Value.Name, outgoing?.Name));
        }
    }

    /// <summary>
    /// Resolves one half of a handoff (SPEC F92.2) from <paramref name="personaStore"/> — never
    /// throws (F12.4): a null <paramref name="personaId"/> (no DJ on this side), an unwired
    /// <paramref name="personaStore"/>, a missing row (deleted out of band), or any store fault all
    /// degrade to <see langword="null"/>, which <see cref="EnqueueHandoffCeremonyAsync"/> treats as
    /// "this half is music-only" (SPEC F92.3). Voice mirrors <see cref="ResolvePersonaAsync"/>'s own
    /// empty-sentinel rule: the persona's own voice when set, else <paramref name="stationVoice"/>.
    /// </summary>
    async Task<(string Voice, string Name)?> ResolveHandoffPersonaAsync(
        long? personaId, string stationVoice, CancellationToken ct)
    {
        if (personaId is null || personaStore is null) return null;

        try
        {
            var persona = await personaStore.GetByIdAsync(personaId.Value, ct);
            if (persona is null)
            {
                logger.LogWarning(
                    "Handoff boundary names persona id={PersonaId} with no matching persona row — " +
                    "treating that half as music-only (SPEC F92.3 degrade).",
                    personaId);
                return null;
            }

            return (VoiceOf(persona, stationVoice), persona.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to resolve handoff persona id={PersonaId} — treating that half as music-only (F12.4).",
                personaId);
            return null;
        }
    }

    /// <summary>
    /// SPEC F92.4: a handoff piece that failed to render (budget exceeded, faulted, or a null result
    /// — e.g. <c>TtsSegmentSource</c>'s own drop of non-LLM-authored handoff copy, PLAN T123)
    /// degrades that HALF of the ceremony only. WARN here, plus a booth-log entry via
    /// <see cref="events"/> (mirrors the <c>DegradationModeChanged</c>/<c>SegmentGenerated</c> event
    /// idiom <c>BoothLogWriter</c> already reacts to) so an operator sees it without grepping logs.
    /// The OTHER piece of the same boundary still airs if it rendered — this method never touches
    /// <c>pendingRenders</c>/<c>buffer</c> itself — and the next boundary retries the full ceremony
    /// from scratch: nothing here latches a failure.
    /// </summary>
    void LogHandoffDrop(SegmentKind kind, string cause)
    {
        logger.LogWarning(
            "Handoff piece {Kind} dropped ({Cause}) — that half of the ceremony airs nothing; the " +
            "other piece still airs if it rendered, and the next boundary retries the full ceremony " +
            "(SPEC F92.4).",
            kind, cause);
        events.Publish(new HandoffPieceDropped(kind.ToString(), cause));
    }

    /// <summary>
    /// gh-#259 — resolves the display name the whole UNIT's items are attributed to (the music
    /// track's <see cref="MediaItem.DjName"/> stamp, and the StationId segment's), from ONE
    /// <paramref name="personaAccessor"/> read per unit. Deliberately separate from
    /// <see cref="ResolvePersonaAsync"/>'s per-segment voice+name reads (SPEC F35.3/F39.1 —
    /// unchanged): this read never influences a voice, only the attribution stamp. Same F12.4
    /// never-fault posture: any accessor fault degrades to "no DJ", never a lost slot.
    /// </summary>
    async Task<string?> ResolveUnitDjNameAsync(CancellationToken ct)
    {
        try
        {
            return (await personaAccessor.ResolveAsync(ct))?.Name;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the voice AND persona name for one segment render from a SINGLE
    /// <paramref name="personaAccessor"/> read (SPEC F35.3, F39.1) — never two separate calls, so
    /// the returned pair always describes the same persona even mid-switch. Voice is the active
    /// persona's voice when non-empty, else <paramref name="stationVoice"/> (SPEC F44.1 — the
    /// caller's single per-unit <see cref="IStationIdentityProvider"/> read, never a second live
    /// read from in here); persona name is the active persona's <see cref="Persona.Name"/> whenever
    /// a persona resolved (regardless of whether its own <see cref="Persona.Voice"/> is the empty
    /// sentinel), else <see langword="null"/>.
    ///
    /// Re-read fresh per call — never cached in a field — so a live activate/deactivate (F35.5)
    /// reaches the very next segment. <paramref name="personaAccessor"/>'s own contract never
    /// throws, but this Orchestrator stays defensive per F12.4 regardless: any unexpected fault
    /// still degrades to <c>(stationVoice, null)</c> rather than costing the segment.
    /// </summary>
    async Task<(string Voice, string? PersonaName)> ResolvePersonaAsync(string stationVoice, CancellationToken ct)
    {
        try
        {
            var persona = await personaAccessor.ResolveAsync(ct);
            if (persona is not null)
                return (VoiceOf(persona, stationVoice), persona.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Falls through to (stationVoice, null) below — an accessor fault must never cost the slot.
        }

        return (stationVoice, null);
    }

    /// <summary>
    /// The empty-sentinel voice rule <see cref="ResolvePersonaAsync"/> and
    /// <see cref="ResolveHandoffPersonaAsync"/> both apply (SPEC F35.2/F92.2): a persona's own
    /// <see cref="Persona.Voice"/> when set, else <paramref name="stationVoice"/> — <c>""</c> is
    /// <see cref="Persona"/>'s own documented "use the station's default" sentinel, never "unset".
    /// </summary>
    static string VoiceOf(Persona persona, string stationVoice) =>
        string.IsNullOrEmpty(persona.Voice) ? stationVoice : persona.Voice;
}
