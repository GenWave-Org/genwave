namespace GenWave.Orchestration;

using System.Collections.Frozen;
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
/// <see cref="GetNextAsync"/> (hoisted from <c>EnqueuePatterAsync</c> (now on <see cref="BreakPlanner"/>) by gh-#254 so the
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
/// drain in <c>EnqueuePatterAsync</c> (now on <see cref="BreakPlanner"/>)).
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
/// which <c>EnqueuePatterAsync</c> (now on <see cref="BreakPlanner"/>) drains in the same pass. This planning pass IS the next
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
/// <b>Handoff ceremony producer (SPEC F92.1-F92.6/F190, STORY-243/457, PLAN T124/T532):</b> moved
/// to <see cref="HandoffCeremonyProducer"/> at PLAN T532 — <see cref="PlanAndRenderAsync"/> calls its
/// <see cref="HandoffCeremonyProducer.ArmAsync"/> every unit, AFTER the deferral drain (unlike the
/// station-id cadence check, which enqueues BEFORE that same drain — see this method's own remarks
/// for why the ordering differs), arming <see cref="SpeechDeferralKind.SignOff"/>/
/// <see cref="SpeechDeferralKind.SignOn"/> into <paramref name="deferralQueue"/> the moment the
/// schedule resolver's resolved boundary enters <paramref name="boundaryBiasProvider"/>'s F74.3
/// lookahead window (the identical knob this Orchestrator's own <see cref="GetNextAsync"/> already
/// reads to build the fit <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> consumes, so
/// "in window" means one thing station-wide) — future-dated, drained by the very same loop on a
/// LATER unit. See <see cref="HandoffCeremonyProducer"/>'s own remarks for the F92.3 dedupe rules,
/// the no-schedule-resolver permanent no-op, and the <see cref="SpeechDeferralQueue.ClearStale"/> a
/// boundary leaving the window triggers.
/// </para>
///
/// <para>
/// <b>Context segments (SPEC F107.3-F107.7, STORY-297, PLAN T224):</b> a <see cref="SpeechDeferralKind.Context"/>
/// deferral (enqueued by the T226 Host ticker off <c>GenWave.Context.ContextPipeline.TickAsync</c>,
/// carrying the fetched <see cref="SpeechDeferral.Context"/> payload) drains through the SAME loop
/// the station-id/handoff kinds share, one boundary at a time (F74.1 — never mid-track). Freshness is
/// re-checked at DRAIN time, not trusted from enqueue time: a stale, content-less, or blank-facts
/// deferral is skipped with one Information line naming the provider key and cause, never echoing the
/// provider's own facts (F108.3) — music is unaffected either way. <c>Context:{Key}:PersonaId</c>
/// (read fresh, per drain, through <c>contextSettings</c>, now on <see cref="BreakPlanner"/>) picks the voice: a positive id
/// names an explicit persona (resolved through <c>personaStore</c>, now on <see cref="BreakPlanner"/>, degrading to the
/// station voice on any miss); zero, negative, or unset defers to the on-air DJ via the SAME
/// <c>ResolvePersonaAsync</c> (now on <see cref="BreakPlanner"/>) every LeadIn/BackAnnounce segment already uses — whose own
/// no-active-persona fallback already IS "music-only segment or gap ⇒ station voice" (the StationId
/// imaging precedent), so that half of F107.7 needed no new code here. A render that comes back null
/// (an LLM miss with no templated-filler rung — SPEC F107.6, mirrors F92.4's handoff-drop posture one
/// paragraph up) logs a WARN and leaves the buffer untouched; the next boundary's own drain retries.
/// </para>
///
/// <para>
/// <b>Top-of-hour idents and time (SPEC F110.2/F110.3, STORY-301/302, PLAN T232):</b> a
/// <see cref="SpeechDeferralKind.StationId"/> drain — from EITHER producer, the pre-existing
/// unit-count cadence check above or <see cref="ClockAnchoredImagingProducer"/>'s top-of-hour trigger
/// — tries <paramref name="catalog"/>'s authored <c>station_id</c> pool FIRST, in the SAME
/// <paramref name="scopeProvider"/> scope the music pick uses (an operator's live library, not the
/// F41.7 safe-scope never-silence floor — this path has a template fallback, so it is never that
/// floor). A non-null pool hit routes the authored <see cref="MediaItem"/> (<see cref="BuildPooledStationIdItem"/>)
/// through <c>KickResolved</c> — no TTS render, but the SAME buffer-ordering guarantee (and gh-#259
/// DjName stamp) every rendered segment gets, so a pool-first ident can never jump ahead of a
/// back-announce Kicked earlier in the same unit. An empty pool (or no <paramref name="catalog"/>
/// wired at all) falls through to the ORIGINAL templated TTS ident, byte-identical to pre-F110
/// behavior. <see cref="SpeechDeferralKind.TimeDate"/> (this enum value's
/// only producer, <see cref="ClockAnchoredImagingProducer"/>) always renders templated, station-voiced
/// copy — <c>BuildTimeDateRequest</c> (now on <see cref="BreakPlanner"/>) reads the hour off the deferral's own <c>Due</c> instant
/// (the top of the hour it was ARMED for), never a fresh drain-time clock read, so a drain landing
/// minutes after the hour still speaks the right hour and, since the SAME hour always renders the SAME
/// text, a second announcement within that hour is a forever-cache hit rather than a re-synthesis.
/// </para>
///
/// <para>
/// <b>TimeDate elapsed-due expiry (SPEC F124.4/F141.1, PLAN T269/T326).</b> Before the drain above
/// ever reaches a <see cref="SpeechDeferralKind.TimeDate"/> deferral, <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s
/// own expiry check may already have dropped it undrained — a drain landing too far past the armed hour
/// (the live <c>Station:Imaging:TimeAnnouncementBudgetSeconds</c> budget, read fresh once per unit
/// through <paramref name="imagingSettings"/>) speaks no hour at all rather than an invented one (the
/// F71.8 class). <see cref="LogTimeDateExpiry"/> is that check's <c>onExpired</c> callback, logging the
/// SPEC F124.4 WARN, unchanged by SPEC F141. StationId (idents) are exempt by design — an equally late
/// ident still drains and airs normally.
/// </para>
///
/// <para>
/// <b>The honest late variant (SPEC F141.2, STORY-355, PLAN T326).</b> A <see cref="SpeechDeferralKind.TimeDate"/>
/// deferral that survives the expiry check above (still inside the budget) is classified a second time —
/// on time vs. late — against the fixed 90-second <see cref="TimeDateHonestyThreshold"/>, using the SAME
/// air-time-lateness formula the expiry check itself uses (real now plus already-queued runtime, minus the
/// armed hour), read fresh at this SAME drain rather than reused from <c>drainNow</c> (which a straddle/
/// ceremony caller may have forced ahead of real time — the identical reason <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s
/// own remarks give for checking its expiry budget against real wall-clock time, never the caller's
/// <c>now</c>). <c>BuildTimeDateRequest</c> (now on <see cref="BreakPlanner"/>) stamps the result onto the <see cref="SegmentRequest"/>
/// it Kicks; <c>PatterTemplateRenderer</c> reads it to choose between the classic and "just past" lines.
/// </para>
///
/// <para>
/// <b>Show idents (SPEC F117.1/F117.2, STORY-309, PLAN T250):</b> the SAME
/// <see cref="SpeechDeferralKind.StationId"/> drain above additionally reads the on-air show (via
/// <paramref name="scheduleResolver"/>'s synchronous <c>TryGetCurrent()</c> snapshot, the T241
/// chokepoint) and hands its id to <paramref name="catalog"/>'s pool call, which now carries the
/// whole show-scope preference ladder in ONE query (show-scoped rows preferred, the station-wide
/// pool as fallback, a foreign-show row never a candidate — see
/// <c>IMediaCatalog.GetRandomReadyByImagingKindAsync</c>'s own remarks). Only when that combined pool
/// comes up empty AND a show is on the air does a NEW floor apply ahead of the plain templated ident:
/// the templated SHOW line ("You're listening to {show} on {station}." — still
/// <see cref="SegmentKind.StationId"/>, so zero-LLM, station-voiced, and forever-cached exactly like
/// every other StationId render, no new kind needed). No show on the air degrades this whole
/// paragraph away — byte-identical to F110.2/F110.3 above, the required outside-show posture.
/// </para>
///
/// <para>
/// <b>Crosstalk (SPEC F127.1/.8/.9, STORY-329, PLAN T287):</b> <paramref name="crosstalkPlanner"/> is
/// told the on-air show once per unit, at the very top of <see cref="GetNextAsync"/>
/// (<see cref="CrosstalkPlanner.NoteOnAirShow"/> — SPEC F127.8's own "EveryNthAiring counts AIRINGS,
/// not stock events" ruling depends on this call running EVERY unit, never only when a vend is
/// attempted, so a transition through a disabled show or a schedule gap is never missed). The vend
/// attempt itself lives in <c>EnqueuePatterAsync</c> (now on <see cref="BreakPlanner"/>), gated on THREE conditions — two structural,
/// one a peek — that together keep banter outside the F92/F124 boundary-ceremony ladder (SPEC F127.8's
/// own "never inside the boundary-ceremony window" ruling): an ordinary music unit
/// (<c>next is not null</c> — <see cref="TryServeCeremonyOnlyUnitAsync"/>'s own call passes null), a
/// drain not already forced ahead of a straddling boundary (<c>drainAsOf is null</c> — the straddle
/// branch below is the only OTHER caller that sets it), AND no pending SignOff/SignOn that will itself
/// leave the deferral queue at THIS SAME unit's own upcoming <see cref="SpeechDeferralQueue.TryDequeueDue"/>
/// call (SPEC F127.8 review F2 — <c>EnqueuePatterAsync</c>'s own <c>CeremonyDrainsThisBreak</c> local).
/// This third condition closes the gap the first two leave open: a ceremony piece already due (or
/// overdue) never gets a <see cref="BoundaryFitPlan"/> at all — <see cref="GetNextAsync"/>'s own peek
/// only builds one for a deferral STRICTLY in the future (<c>untilDue &gt; TimeSpan.Zero</c>) — so
/// neither the ceremony-only decline nor the straddle branch ever sees it, yet
/// <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s own <c>Due &lt;= now</c> check still drains it a
/// few lines later in the SAME unit regardless. Proven live, pre-fix: a stocked exchange plus a SignOff
/// due 1s in the past produced AIRED ORDER [Crosstalk, SignOff, LeadIn] — the ceremony piece airing
/// AFTER the banter it was supposed to structurally precede. A vended exchange supersedes the
/// F107.5/F116.3 shared-slot lanes for that SAME break (SPEC F127.9) via
/// <see cref="SegmentRequest.CrosstalkAiredThisBreak"/>, stamped onto that unit's own LeadIn/
/// BackAnnounce requests — see <c>EnqueuePatterAsync</c> (now on <see cref="BreakPlanner"/>)'s own remarks for the rest.
/// </para>
///
/// <para>
/// <b>Owner announcements (SPEC F144.1/F144.2, F145.2, STORY-358, PLAN T341):</b> the claim seam
/// (<c>IAnnouncementSource</c>) is the crosstalkPlanner precedent one feature over — an optional
/// constructor dependency, feature dark whenever null (no Host wiring). PLAN T522 moves the claim
/// itself onto <see cref="BreakPlanner"/> (its own <c>announcementSource</c> constructor parameter,
/// gated on <c>IVerbatimSegmentRenderer</c> at the construction site that builds THAT class, not
/// here — a null renderer must still mean no announcement is ever claimed, not merely that a
/// claimed one never airs); this class keeps only the render half below, and PLAN T534 moved even
/// that onto <see cref="BreakRenderer"/> (<paramref name="breakRenderer"/>). The planner's own
/// vend cap and claim ordering vends up to its own <c>AnnouncementVendCap</c> (a constant on THAT
/// class now) oldest deliverable announcements, atomically claimed the moment this unit
/// decides to vend them, and places each as a <see cref="SegmentKind.Announcement"/> segment after
/// the back-announce and before the lead-in — exactly like <see cref="SegmentKind.Crosstalk"/>'s own
/// placement one paragraph up, just a different pair of steps in the SAME unit. The station never
/// reads <c>Station:SpectatorMode</c> to decide whether to vend (SPEC F145.2's "the Orchestrator
/// never reads privacy state" ruling): an empty claim — refused because the station is public, or
/// genuinely nothing pending — looks identical from here, and needs no different handling either way.
/// <see cref="BreakRenderer"/>'s own <c>IVerbatimSegmentRenderer</c> seam renders each claimed item's
/// exact message text with ZERO LLM involvement (SPEC F144.2) — a SEPARATE seam from
/// <c>ITtsSegmentSource</c>, never routed through it, because neither <see cref="SegmentRequest"/>
/// nor <see cref="ISegmentCopyWriter"/> can carry a caller-supplied exact text without either
/// widening the published Abstractions record or forcing the render through the SAME copy-writer
/// chain an LLM writer sits in front of (see <see cref="IVerbatimSegmentRenderer"/>'s own remarks).
///
/// <b>The flavored path (SPEC F144.3/F144.4, PLAN T342):</b> a <c>Verbatim: false</c> announcement
/// FIRST attempts <see cref="BreakRenderer"/>'s own <c>IAnnouncementCopyWriter</c> seam — its OWN
/// dedicated seam, the crosstalkPlanner precedent one feature over (optional, feature-dark whenever
/// null), never <c>ITtsSegmentSource</c>/<see cref="ISegmentCopyWriter"/> either. THE FALLBACK LAW is
/// exactly one <c>??</c> at the vend step below: any failure there (a disabled/unreachable LLM, a
/// blown render budget, or the F138.4 re-ask ladder exhausting on either a fabrication or the F144.3
/// containment check) resolves to <see langword="null"/>, and the owner's own message renders
/// verbatim instead — through this SAME <c>IVerbatimSegmentRenderer</c> seam, since flavored copy IS
/// exact once written and needs no different rendering path from a verbatim read. A
/// <c>Verbatim: true</c> announcement never even asks the copy writer, the owner having asked for
/// their own unflavored words. <paramref name="voiceLister"/> (SPEC F144.2's own "when
/// known" clause) validates <see cref="AnnouncementItem.RequestedVoice"/> — untrusted free text —
/// against the TTS backend's own installed voice ids before ever stamping it onto a
/// <see cref="SegmentRequest.Voice"/>; unknown, invalid, or unreachable (the registry itself is a
/// live network call) all degrade to the station's own default voice, never an error and never a
/// path component of any kind. The announcement id survives onto the rendered segment's own MediaId
/// (<see cref="AnnouncementMediaId.Wrap"/>) rather than a new member on any published type — see
/// that helper's own remarks for why a later task's aired-stamp becomes a lookup, not a registry to
/// keep in sync.
/// </para>
///
/// <para>
/// <b>Ad cadence (SPEC F158.2/F158.3, STORY-388, PLAN T397):</b> moved to <see cref="BreakPlanner"/>
/// at PLAN T522 — the trigger (<c>EnqueueAdCadence</c>) and the drain-time vend
/// (<c>BuildAdDrainSlotAsync</c>, <see cref="IAdSpotVend.GetNextSpotAsync"/>) both live there now,
/// verbatim copies of this method's own pre-T522 shape. <paramref name="planner"/> owns both seams;
/// this constructor no longer takes <c>adCadenceProvider</c>/<c>adSpotVend</c> directly.
/// </para>
/// </summary>
public sealed partial class Orchestrator(
    IStationIdentityProvider identityProvider,
    IStationScopeProvider scopeProvider,
    ICadenceProvider cadenceProvider,
    IRotationSettingsProvider rotationProvider,
    MusicSelectionPolicy musicSelectionPolicy,
    IActivePersonaAccessor personaAccessor,
    ILogger<Orchestrator> logger,
    SpeechDeferralQueue deferralQueue,
    TimeProvider timeProvider,
    IBoundaryBiasProvider boundaryBiasProvider,
    BreakPlanner planner,
    HandoffCeremonyProducer handoffCeremonyProducer,
    BreakRenderer breakRenderer,
    CachingScheduleResolver? scheduleResolver = null,
    IStationEventSink? events = null,
    IPatterDurationEstimator? patterEstimator = null,
    IStationImagingSettingsProvider? imagingSettings = null,
    CrosstalkPlanner? crosstalkPlanner = null,
    IBreakPlanObserver? observer = null) : INextItemProvider, IBoundaryFitLog
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
    // Public (SPEC F142, PLAN T327): GenWave.Host's BoundaryCadenceCovenantPostConfigure reads this
    // constant as the covenant's signOffLeadTime term. It must never become a config knob (F142.2 —
    // "no new knobs").
    public static readonly TimeSpan SignOffLeadTime = TimeSpan.FromSeconds(15);

    /// <summary>
    /// SPEC F141.2 (STORY-355, PLAN T326) — the honesty threshold: a <see cref="SpeechDeferralKind.TimeDate"/>
    /// deferral draining within this long of its own armed hour still speaks the classic F110.3 line;
    /// past it (but still inside the live <c>Station:Imaging:TimeAnnouncementBudgetSeconds</c> budget)
    /// the honest "just past" variant airs instead. Judged, not spec'd to the second beyond gh-#526's
    /// own field data (the shallow overruns the fix targets landed 313-362s past Due) — 90 seconds
    /// comfortably separates "the break just arrived a beat late" from "the break was genuinely late."
    /// Not a live-tunable SPEC knob, the SAME posture <see cref="SignOffLeadTime"/> immediately above
    /// carries — just an implementation seam.
    /// </summary>
    static readonly TimeSpan TimeDateHonestyThreshold = TimeSpan.FromSeconds(90);

    // SPEC F111.2 (PLAN T235) — the straddle seam's drain hold-set: a single-purpose, never-mutated
    // singleton rather than allocating a fresh HashSet per straddle unit, since its one member never
    // varies. See GetNextAsync's own remarks (the straddle branch) for what this actually guards.
    // SPEC F124.1 (PLAN T267) reuses this SAME set for the queue-crossing decline path
    // (TryServeCeremonyOnlyUnitAsync) — a second "hold the SignOn" shape, never a second set: both
    // callers hold the identical one kind for the identical reason (a paired SignOff must not drain
    // its SignOn ahead of content that has not finished airing).
    static readonly IReadOnlySet<SpeechDeferralKind> HoldSignOnAtStraddle =
        new HashSet<SpeechDeferralKind> { SpeechDeferralKind.SignOn };

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

    // SPEC F124.4 (PLAN T269): same null-coalesced-default idiom as events/patterEstimator above.
    // A host that has not yet wired the real IOptionsMonitor-backed implementation (every
    // pre-T269 construction site, including every unit test) reads back NoOpStationImagingSettingsProvider's
    // both-false/5-minute answer — the shipped SPEC F124.4 default — never a null-check, never a stall.
    readonly IStationImagingSettingsProvider imagingSettings = imagingSettings ?? NoOpStationImagingSettingsProvider.Instance;

    // PLAN T522: same null-coalesced-default idiom as imagingSettings immediately above. A host that
    // never registers a real IBreakPlanObserver (every production host today, and every pre-T522
    // test) reads back silence — never a null-check, never a stall.
    readonly IBreakPlanObserver planObserver = observer ?? NoOpBreakPlanObserver.Instance;

    // SPEC F186.2a — SpeechDeferralQueue.TryDequeueDue treats a null hold and an empty set
    // identically (neither holds anything back); this sentinel exists purely so BreakContext.Hold,
    // a non-nullable member, never needs a null-check of its own downstream.
    static readonly IReadOnlySet<SpeechDeferralKind> EmptyHold = FrozenSet<SpeechDeferralKind>.Empty;

    readonly Queue<MediaItem> buffer = new();
    MediaItem? previousTrack;
    int unitCount;

    // SPEC F92.1/F92.3 arm-once state and the no-schedule-resolver warn-once flag both moved to
    // HandoffCeremonyProducer at PLAN T532 — see that type's own remarks (lastArmedHandoff,
    // scheduleResolverMissingWarned).

    // SPEC F141.1/F141.4 (STORY-355, PLAN T326, review advisory) — the SAME "fires at most once for
    // the life of this Orchestrator" idiom HandoffCeremonyProducer's own warn-once flag uses,
    // repurposed for a boot-log config echo rather than a missing-dependency WARN: an INFO-level
    // one-time snapshot of the bound TimeDate honesty budget, logged the first time GetNextAsync
    // reads it below. Originally a ContextTickerService line (review round-1: wrong altitude — that
    // class took an IStationImagingSettingsProvider dependency just to print it); this Orchestrator
    // IS the budget decision's own owner (it computes timeDateStaleBudget and the honesty
    // classification below), so the echo lives where the value is actually consumed, with no extra
    // constructor dependency anywhere. Live-editable afterward (imagingSettings.Current is read fresh,
    // per unit, regardless of this flag) — this line only names what a fresh boot actually bound, so
    // an operator (or Loki) can confirm a deploy's effective default without reading appsettings.json
    // off the box.
    bool timeDateBudgetLoggedOnce;

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

        // SPEC F127.8 (STORY-329, PLAN T287) — Crosstalk:EveryNthAiring's own counter: told the
        // on-air show EVERY unit, continuously, regardless of whether crosstalk is even enabled or a
        // vend is even attempted this unit — see CrosstalkPlanner.NoteOnAirShow's own remarks for why
        // this must never be narrowed to "only when a vend is about to be attempted". A null
        // crosstalkPlanner (the feature's Host wiring never ran) or a null scheduleResolver (no
        // format-clock schedule wired) both degrade this to a permanent no-op.
        crosstalkPlanner?.NoteOnAirShow(scheduleResolver?.TryGetCurrent()?.Show?.Slug);

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

        // F112 (STORY-295, PLAN T218): the pick ladder itself lives on MusicSelectionPolicy — this
        // Orchestrator (implementing IBoundaryFitLog explicitly, PLAN T234) is threaded in as the log
        // sink so every outcome line the resample loop logs still lands on the SAME Information sink
        // the ceremony-decline path ("declined") uses (see MusicSelectionPolicy.SelectMusicCandidateAsync's
        // own remarks). SPEC F111.1's outcome rides along on the result but changes nothing about
        // this unit's flow yet — Fit and Straddle both take today's ordinary music-unit path
        // unchanged; only PLAN T235 gives Straddle its own assembly shape. CeremonyOnly DOES reach
        // this call, routinely (T234 review finding F1 — corrects an earlier version of this comment
        // that claimed the opposite): ShouldDeclineFinalUnit only ever short-circuits handoff kinds
        // (SignOff/SignOn) below the floor. A StationId/TimeDate fit below the floor is NEVER
        // declined (see that method's own remarks) and lands here every time, classifying
        // CeremonyOnly on the policy's own least-late/unscored/drained line — an everyday path, not a
        // corner case. A SignOff/SignOn fit CAN also reach here below the floor: when the decline's
        // own TryServeCeremonyOnlyUnitAsync renders nothing at all, it returns null and this SAME
        // below-floor fit falls through to this call, which classifies CeremonyOnly the identical way.
        // T235's straddle-assembly implementer: CeremonyOnly is not exclusively the decline path's own
        // hard-coded literal.
        var selection = await musicSelectionPolicy.SelectMusicCandidateAsync(
            scopeProvider.Current, orderedRecentIds, artistSeparation, fit, this, ct);
        var candidate = selection.Candidate;
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

        // SPEC F152.4 (STORY-372, PLAN T361) — rides the SAME RotationCandidate -> MediaItem
        // carry-through PersonaPick/RequestFulfilled use above; null (envelope.Rotation was never set
        // for this pick, the byte-identical no-rotation path) omits the stamp member entirely once
        // BoothLogWriter builds it (STORY-372 AC10).
        if (candidate.RotationRelax is int rotationRelax)
            track = track with { RotationRelax = rotationRelax };

        // SPEC F151.1/F151.2 (STORY-371, PLAN T370) — rides the SAME RotationCandidate -> MediaItem
        // carry-through PersonaPick/RequestFulfilled/RotationRelax use above; null (the pick never
        // reached the persona ranker's rung 0) omits the stamp member entirely once BoothLogWriter
        // applies the F151.4 chip threshold.
        if (candidate.Nudge is double nudge)
            track = track with { Nudge = nudge };

        // gh-#259: stamp Now Playing attribution at PLAN time, onto the item itself — the single
        // per-unit accessor read resolved above (it also warms the F93.1 display-name memo every
        // unit, cadence config regardless). The spectator surface reads this off the AIRING item,
        // so after a schedule boundary the displayed DJ keeps naming whoever's queued items are
        // still draining and flips only when the new show's items actually reach air — never the
        // schedule's live answer.
        track = track with { DjName = unitDjName };

        // SPEC F111.2/F111.3 (gh-#320, PLAN T235) — the straddle assembly. A Straddle outcome whose
        // peeked deferral is a SignOff, AND whose picked candidate genuinely CROSSES the boundary
        // (<see cref="MusicSelectionResult.CrossesBoundary"/> — T235 review findings F1/F5: the
        // ladder's own <see cref="BoundaryOutcome.Straddle"/> rung fires for ANY off-tolerance pick
        // clearing the floor, including one running far SHORTER than desired, which cannot possibly
        // cross anything; only <see cref="MusicSelectionPolicy"/> ever measured the candidate's
        // effective length against the boundary, so it — not this Orchestrator — decides crossing, at
        // the source), means the track just picked is deliberately going to run past the boundary
        // (that IS the ladder's middle rung — nothing fit, but the room is there); left to the ordinary
        // drain (as-of "now", untilDue > 0 or the fit above would never have built at all) the SignOff
        // would sit pending until whatever LATER unit's normal drain finally reaches its due — quite
        // possibly the SAME one the SignOn (due at the boundary itself) also reaches by then,
        // reproducing gh-#300's own back-to-back field report one rung up the ladder. Force THIS unit's
        // drain forward to the SignOff's own Due instead (mirrors TryServeCeremonyOnlyUnitAsync's
        // identical "drain as of a future instant, not now" precedent two methods down) so it airs
        // ahead of the crossing track, and hold the paired SignOn out of that same forced call (SPEC
        // F111.2's hold-set) so it stays queued through this seam and drains first at the next one,
        // once the crossing track has actually aired.
        //
        // A NON-crossing off-tolerance pick (F1's short-track fact) takes the ordinary unforced path
        // below instead: the SignOff stays exactly where TryDequeueDue would have left it, airing near
        // its own due (T234 baseline) rather than being forced ahead of a track that was never going to
        // run past it. A one-sided straddle (SignOff with no SignOn queued at all — F92.3's "into
        // music-only" shape) takes the identical forced path when it crosses; holding a kind with
        // nothing pending is a no-op. A SignOn-headed fit (the opposite one-sided shape, "out of
        // music-only" — no SignOff exists to drain ahead of anything) needs none of this: it is not yet
        // due either, so the ordinary unforced drain below already leaves it queued until the seam
        // after this crossing track, with nothing to hold.
        //
        // drainAsOf/hold (SPEC F111.2) collapse to locals here — the single home for the F1/F2 guards
        // below (T235 review finding F4) — rather than three near-identical EnqueuePatterAsync call
        // sites: every path through this method falls through to the ONE call at the bottom, differing
        // only in what these two locals hold.
        DateTimeOffset? drainAsOf = null;
        IReadOnlySet<SpeechDeferralKind>? hold = null;

        if (selection.Outcome == BoundaryOutcome.Straddle
            && selection.CrossesBoundary
            && pending is { Kind: SpeechDeferralKind.SignOff })
        {
            // Reconcile the handoff producer's OWN state for this unit's boundary/schedule snapshot
            // before trusting the peeked SignOff as forceable (ScenarioSupersedeProtects regression,
            // T235 review): pending.Kind == SignOff with untilDue > 0 (fit above was only built
            // because of that) proves this SignOff is the GLOBALLY earliest-due pending entry across
            // every kind, so nothing else is currently due for HandoffCeremonyProducer.ArmAsync's own
            // re-evaluation to wrongly race ahead of — unlike its ordinary step-2.5 call (deliberately
            // placed AFTER the drain, see EnqueuePatterAsync's own remarks, so it never clears a piece
            // the drain was about to fire), calling it here first is safe precisely because there is no
            // due piece for it to preempt. A schedule write that already superseded or retracted this
            // ceremony (SPEC F92.1 revisit) takes effect right here, before this unit's own forced
            // drain would otherwise race past it. The SAME method runs again from inside
            // EnqueuePatterAsync's normal step 2.5 immediately below; seeing the identical
            // (now-reconciled) key, that call is a safe no-op (the arm-once guard).
            var now = timeProvider.GetUtcNow();
            await handoffCeremonyProducer.ArmAsync(identity, now, ct);

            // T235 review finding F2: only force when the reconciliation left the EXACT ceremony
            // peeked above untouched — reconciledSignOff.Due == pending.Due proves nothing changed
            // (same key, same due). A schedule write that MOVED the boundary re-arms a SignOff at a
            // DIFFERENT due (still non-null — it is still in-window, just for a new boundary); forcing
            // to that reconciled-but-different due fired the sign-off against the WRONG boundary (the
            // field report: 6:45 early), and HandoffCeremonyProducer.CaptureCrossingTrack would have
            // stamped the MOVED SignOn's copy with a track that has nothing to do with its own, later
            // boundary. The fresh, moved ceremony gets its own fair shot at a later, correctly-classified
            // straddle unit instead — this unit simply falls through to the ordinary unforced drain below.
            //
            // A null reconciledSignOff is the OTHER shape this SAME reconciliation can take (T235
            // review finding F3, the retraction half): the boundary left the F74.3 window entirely (or
            // collapsed to a gap-to-gap/self-handoff), and HandoffCeremonyProducer.ArmAsync's own
            // ClearCeremony wiped BOTH pieces — there is no ceremony left, moved or otherwise, to force.
            // That falls through to the SAME ordinary unforced drain below, which is simply a no-op for
            // a ceremony that no longer exists — this unit plans as an ordinary music unit.
            if (deferralQueue.Peek(SpeechDeferralKind.SignOff) is { } reconciledSignOff
                && reconciledSignOff.Due == pending.Due)
            {
                handoffCeremonyProducer.CaptureCrossingTrack(track);
                drainAsOf = reconciledSignOff.Due;
                hold = HoldSignOnAtStraddle;
            }
        }

        await PlanAndRenderAsync(
            previousTrack, track, unitDjName, cadence, identity, drainAsOf, hold,
            queuedAhead: TimeSpan.FromMilliseconds(ctx.QueuedAheadMs ?? 0), ct);

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
    /// (the exact inverse of how HandoffCeremonyProducer.ArmAsync armed it); every other kind's due IS
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

        // SPEC F117.2 (gh-#463) — the SAME synchronous TryGetCurrent() snapshot the F117.2 drain-side
        // StationId arm already trusts (this method's own file, the F110.2/F110.3 remarks block), read
        // ONCE here rather than per Estimate call below: every term this fit reasons about must
        // describe the SAME on-air show, not two snapshots straddling a boundary flip mid-build. A
        // null scheduleResolver (no format-clock schedule wired) or no show on the air both degrade to
        // null, which the estimator's own (voice, show-name-or-null) keying already treats as the
        // showless bucket — byte-identical to this fit's pre-gh-#463 behavior for every station that
        // has never assigned a show.
        var showName = scheduleResolver?.TryGetCurrent()?.Show?.Name;

        TimeSpan Estimate(SegmentKind kind, string? personaName, string voice)
        {
            var estimate = patterEstimator.Estimate(kind, personaName, voice, showName);
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
    /// front of the boundary is under <see cref="MusicSelectionPolicy.MusicFloor"/>, via
    /// <see cref="BoundaryFitPlan.IsBelowFloor"/> (PLAN T234, T234 review finding F3: the SAME
    /// predicate <see cref="MusicSelectionPolicy"/> classifies its own <see cref="BoundaryOutcome.CeremonyOnly"/>
    /// rung against — one predicate, called from both sites, never two hand-written complementary
    /// comparisons), in which case planning one more full track is strictly worse than planning none
    /// (see that constant for the arithmetic).
    ///
    /// <para>
    /// <b>Handoff kinds only.</b> A show boundary is an appointment the audience can hear being
    /// missed — the incoming DJ announcing "it's Thursday two o'clock" at 2:05 is the whole issue.
    /// A station ID is not: it is imaging that can ride the next seam quite happily, and skipping a
    /// whole track for one would trade a small blemish for a large one. This guard is scoped by KIND,
    /// not by whether a deferral happens to be future-dated (T235 review — corrects an earlier version
    /// of this comment, which claimed "today's ident producer only ever enqueues due-NOW deferrals,
    /// so such a fit is never even built"; false since <see cref="ClockAnchoredImagingProducer"/>,
    /// PLAN T230, future-dates <c>StationId</c>/<c>TimeDate</c> deferrals too, the identical shape a
    /// handoff's own SignOff/SignOn already used). A future-dated StationId/TimeDate fit reaches this
    /// method exactly like a due-now one always did — the <c>fit.Kind is SignOff or SignOn</c> check
    /// below is the ONLY thing that keeps it from ever declining, not its due time. This is why this
    /// method still short-circuits BEFORE
    /// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> ever runs (gh-#320, PLAN T234
    /// keeps <see cref="TryServeCeremonyOnlyUnitAsync"/>'s mechanics here, Orchestrator-side, rather
    /// than moving unit-assembly itself into the policy) — a StationId/TimeDate fit below the floor
    /// is never declined, exactly as today, even though the policy's OWN off-tolerance classification
    /// would report <see cref="BoundaryOutcome.CeremonyOnly"/> for it if asked (T234 review finding
    /// F1(a) — not a hypothetical: it is the everyday path a below-floor StationId/TimeDate fit
    /// actually takes, every time).
    /// </para>
    ///
    /// <para>
    /// <b>The decline can ALSO fall through to that same policy call (T234 review finding F1(b)).</b>
    /// A handoff kind's decline is not unconditional: when <see cref="TryServeCeremonyOnlyUnitAsync"/>'s
    /// own drain renders nothing at all (SPEC F92.4 — every piece of the ceremony dropped), it returns
    /// <see langword="null"/>, and <see cref="GetNextAsync"/> falls through to the ordinary
    /// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> call with the very SAME below-floor
    /// <see cref="BoundaryFitPlan"/> this method already evaluated — which classifies off
    /// <see cref="BoundaryFitPlan.ClassifyOffToleranceRung"/>, the SAME classifier
    /// <see cref="TryServeCeremonyOnlyUnitAsync"/> itself now consults too (PLAN T267):
    /// <see cref="BoundaryOutcome.CeremonyOnly"/> for a non-queue-crossing fit, or
    /// <see cref="BoundaryOutcome.Straddle"/> for a queue-crossing one. T235's straddle-assembly
    /// implementer should not assume <see cref="BoundaryOutcome.CeremonyOnly"/> only ever arrives via
    /// the decline branch, and should not assume the decline branch only ever logs that one rung either.
    /// </para>
    ///
    /// <para>
    /// <b>SPEC F124.1 ruling (STORY-320, PLAN T267 — recorded here per the T266 review's request):</b>
    /// this method's own condition did not need to widen for the queue-crossing case, and does not —
    /// see <see cref="BoundaryFitPlan.IsBelowFloor"/>'s own remarks for the "crossing implies
    /// below-floor" argument that makes that true, and <see cref="TryServeCeremonyOnlyUnitAsync"/>'s
    /// own remarks for what changed instead (and why the rejected alternative — yielding the decline
    /// into <see cref="GetNextAsync"/>'s straddle assembly — needed a no-new-track guard
    /// <see cref="MusicSelectionPolicy"/> does not have).
    /// </para>
    /// </summary>
    bool ShouldDeclineFinalUnit(BoundaryFitPlan fit) =>
        fit.Kind is SpeechDeferralKind.SignOff or SpeechDeferralKind.SignOn
        && fit.IsBelowFloor(MusicSelectionPolicy.MusicFloor);

    /// <summary>
    /// gh-#300 — plans the ceremony as a unit of its own: back-announce (the fit already reserved
    /// it) plus whatever the drain yields, and no music.
    ///
    /// <para>
    /// <b>The drain runs as-of a future instant, never "now".</b> A SignOff comes due at
    /// <c>boundary - SignOffLeadTime</c>, so at the moment this decision is taken it is still a few
    /// seconds in the future and an as-of-now drain would return nothing — which is precisely the
    /// bug: the ceremony then waited for a pull that a freshly-planned three-and-a-half-minute track
    /// had just pushed past the hour. For a SignOff-headed fit, SPEC F124.3 widens that instant to
    /// <c>Max(UntilBoundary, QueuedAhead)</c> when the queue crosses (a non-crossing fit's QueuedAhead
    /// is always &lt; UntilBoundary, so this degrades to exactly UntilBoundary — byte-identical to
    /// pre-F124) — clamped against <see cref="IBoundaryBiasProvider.Current"/> (round-1 review finding
    /// F5): the pending-air queue can legally hold hours under a backlog (SPEC F124.6's own watch
    /// item), and chasing an unbounded estimate would push this instant arbitrarily far past the
    /// lookahead window this whole fit was built inside of, for no benefit — anything past the window
    /// drains at a LATER unit's own forced instant instead, never lost. A SignOn-headed fit (the held
    /// SignOn itself, back on a later pull as the peeked fit) does NOT chase <c>QueuedAhead</c> at all
    /// — round-1's own defect (see below) — it clamps to its own <c>UntilBoundary</c>, full stop:
    /// nothing here needs this instant to reach any further, since what actually keeps the SignOn from
    /// airing early is <see cref="SpeechDeferral.NotBefore"/> below, not this clamp — the clamp is
    /// honesty (this instant should not overstate how far "as of" this pull is willing to pretend),
    /// the gate is what structurally holds.
    /// </para>
    ///
    /// <para>
    /// <b>T269 breadcrumb (not built yet):</b> a future <c>TimeDate</c> elapsed-due expiry (PLAN T269)
    /// reads beside this same drain — see <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s own remarks
    /// for why that predicate must compare against REAL wall-clock time, never the forced instant
    /// computed here.
    /// </para>
    ///
    /// <para>
    /// <b>SPEC F124.1 — a queue-crossing SignOff holds its own SignOn.</b> This method is reached only
    /// once <see cref="ShouldDeclineFinalUnit"/> has already proved the peeked fit below-floor — which
    /// a queue crossing the boundary ALWAYS also proves for a handoff kind (see
    /// <see cref="BoundaryFitPlan.IsBelowFloor"/>'s own remarks for the argument), so
    /// <see cref="ShouldDeclineFinalUnit"/>'s condition never needed to widen. What changes here is
    /// what this method DOES once one arrives: it consults the SAME
    /// <see cref="BoundaryFitPlan.ClassifyOffToleranceRung"/> <see cref="MusicSelectionPolicy"/>'s own
    /// ladder would apply to this identical fit — never a second, hand-written crossing check — and on
    /// a fit already proven below-floor, that classifier's Straddle verdict can only mean one thing:
    /// the queue itself is what crosses. A Straddle verdict on a <see cref="SpeechDeferralKind.SignOff"/>
    /// hands its paired SignOn to <see cref="HandoffCeremonyProducer.HoldSignOnPastQueuedTail"/> — the SAME
    /// <see cref="HoldSignOnAtStraddle"/> hold-set <see cref="GetNextAsync"/>'s own straddle branch
    /// uses (SPEC F111.2) EXCLUDES it from this same call, and <see cref="SpeechDeferral.NotBefore"/>
    /// (round-1 review finding F1 — see that method's own remarks) keeps it excluded from every LATER
    /// call too, until the queued tail it is held behind has actually had time to drain. A
    /// SignOn-headed fit (the held SignOn itself, back for its own later seam) has no OTHER piece to
    /// hold and drains here ordinarily once its own gates open. A CeremonyOnly verdict — below floor,
    /// not crossing — takes the ordinary unforced path: both due pieces drain together.
    /// </para>
    ///
    /// <para>
    /// <b>Candidate (i) vs (ii), the T266 review's ruling (recorded here at the review's request).</b>
    /// The alternative shape — yielding the decline into <see cref="GetNextAsync"/>'s own straddle
    /// assembly, so a full track plans in front of an already-overshot boundary — was rejected: every
    /// rung of <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/>'s ladder still returns SOME
    /// candidate short of a genuine catalog drain, so that path needs a no-new-track guard the policy
    /// does not have, and planning one more full track when the queue is ALREADY past the boundary only
    /// deepens the SPEC F124.6 buildup the review flagged. Keeping this method's own ceremony-only shape
    /// and widening what it holds was the smaller, honest fix — this method's own doc above is that fix.
    /// </para>
    ///
    /// <para>
    /// <b>Planning early is not airing early.</b> The ceremony is appended behind whatever audio is
    /// still draining, so it reaches air roughly when that audio runs out — the boundary in the common
    /// case, or the queued-tail estimate itself when that runs later (SPEC F124.5's accepted
    /// consequence: the spectator <c>dj</c> plan-time skew is now bounded by the ACTUAL drain, not a
    /// mis-aired ceremony stretched over it). Never-silent (F6.3) is untouched either way: this method
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
    ///
    /// <para>
    /// <b>T270 evidence note (round-2 review finding F9).</b> Before <see cref="SpeechDeferralQueue.PeekNextDue"/>
    /// learned to skip a <see cref="SpeechDeferral.NotBefore"/>-gated entry (SPEC F124.1/F124.2, PLAN
    /// T267, round-2 review findings F1/F2), a live hold's blind peek could make THIS method run again
    /// on every single pull for as long as the hold lasted — logging this SAME
    /// <c>"declined … rung=Straddle"</c> line once per pull, with no corresponding ceremony piece ever
    /// re-airing (<see cref="SpeechDeferralQueue.TryDequeueDue"/>'s own gate correctly refused to
    /// release the held entry every time). Anyone reading T270's log evidence and finding that exact
    /// repeating shape — many identical "declined" lines, one held SignOn, no matching new SignOn
    /// airing between them — is looking at that now-fixed signature, not a new defect to chase; see
    /// <see cref="SpeechDeferralQueue.PeekNextDue"/>'s own remarks for the fix.
    /// </para>
    /// </summary>
    async Task<MediaItem?> TryServeCeremonyOnlyUnitAsync(
        BoundaryFitPlan fit, string? unitDjName, CadenceConfig cadence, StationIdentity identity,
        CancellationToken ct)
    {
        // The SAME classifier MusicSelectionPolicy.SelectMusicCandidateAsync would apply to this
        // identical fit, consulted directly rather than duplicated by hand — see this method's own
        // remarks for why a Straddle verdict here can only mean the queue crosses.
        var rung = fit.ClassifyOffToleranceRung(MusicSelectionPolicy.MusicFloor);

        // ONE line, not two: the fit line already carries every term (desired, queuedAhead, the
        // lot), so a second human-readable "declining because…" would restate it. The floor is the
        // only fact the fit itself does not know, so it rides the outcome. "outcome=declined" stays
        // the greppable signature for T270's evidence regardless of which rung follows it (SPEC
        // F124.1) — only the rung token tells a queue-crossing decline (Straddle) from an ordinary
        // one (CeremonyOnly).
        LogBoundaryFit(
            fit,
            $"declined (floor={MusicSelectionPolicy.MusicFloor.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s)",
            rung,
            sampled: [],
            chosenDiff: null);

        // The forced drain instant — see this method's own remarks for the SignOff-headed chase
        // (clamped against the F74.3 lookahead window, round-1 review finding F5) versus the
        // SignOn-headed clamp to its own boundary alone (round-1 review finding F1: chasing QueuedAhead
        // here too is exactly what let a held SignOn's own re-evaluation drain it in the very next
        // call). fit.QueuedTailCrossesBoundary reuses the SAME crossing predicate the classifier above
        // just consulted (round-1 review finding F4 — no second, differently-spelled comparison).
        var chasedQueuedAhead = fit.QueuedAhead < boundaryBiasProvider.Current
            ? fit.QueuedAhead
            : boundaryBiasProvider.Current;
        var drainDelay = fit.Kind == SpeechDeferralKind.SignOff && fit.QueuedTailCrossesBoundary
            ? chasedQueuedAhead
            : fit.UntilBoundary;
        var now = timeProvider.GetUtcNow();
        var boundary = now + drainDelay;

        // Only a queue-crossing SignOff holds its paired SignOn — see this method's own remarks.
        // Everything else (a SignOn-headed fit already back for its own seam, or a non-crossing
        // CeremonyOnly fit) takes the ordinary unforced drain, hold: null.
        IReadOnlySet<SpeechDeferralKind>? hold = null;
        if (rung == BoundaryOutcome.Straddle && fit.Kind == SpeechDeferralKind.SignOff)
        {
            handoffCeremonyProducer.HoldSignOnPastQueuedTail(now, fit.QueuedAhead);
            hold = HoldSignOnAtStraddle;
        }

        await PlanAndRenderAsync(
            previousTrack, next: null, unitDjName, cadence, identity, boundary, hold,
            queuedAhead: fit.QueuedAhead, ct);

        return buffer.Count > 0 ? buffer.Dequeue() : null;
    }

    /// <summary>
    /// PLAN T522 — the ONE call every <see cref="GetNextAsync"/>/<see cref="TryServeCeremonyOnlyUnitAsync"/>
    /// unit makes to plan and render its break: builds this unit's <see cref="BreakContext"/>, hands
    /// it to <see cref="planner"/> (SPEC F188), publishes the result to <see cref="planObserver"/>,
    /// arms/clears the handoff-ceremony producer (SPEC F190 — this stays here, never migrated to
    /// <see cref="BreakPlanner"/>, and must run AFTER the plan's own drain so it never clears a piece
    /// the drain was about to fire this same unit), then renders every slot (<see cref="RenderPlanAsync"/>,
    /// declared in Orchestrator.Render.cs).
    /// </summary>
    async Task PlanAndRenderAsync(
        MediaItem? prev, MediaItem? next, string? unitDjName, CadenceConfig cadence, StationIdentity identity,
        DateTimeOffset? drainAsOf, IReadOnlySet<SpeechDeferralKind>? hold, TimeSpan queuedAhead, CancellationToken ct)
    {
        // SPEC F141.1/F141.4 (STORY-355, PLAN T326) — the boot-log config echo, unchanged from
        // EnqueuePatterAsync's own first line (see timeDateBudgetLoggedOnce's own remarks for why it
        // lives here rather than on a bystander Host service).
        if (!timeDateBudgetLoggedOnce)
        {
            timeDateBudgetLoggedOnce = true;
            logger.LogInformation(
                "TimeDate honesty budget bound: {TimeAnnouncementBudgetSeconds}s (SPEC F141.1)",
                imagingSettings.Current.TimeAnnouncementBudgetSeconds);
        }

        var now = timeProvider.GetUtcNow();
        var context = new BreakContext(
            unitCount, prev, next, unitDjName, cadence, identity, now,
            drainAsOf, hold ?? EmptyHold, queuedAhead);

        var plan = await planner.PlanAsync(context, ct);
        planObserver.Planned(plan);

        // 2.5. Handoff ceremony producer (SPEC F92.1-F92.6, STORY-243, PLAN T124/T190) — runs every
        // unit, AFTER the plan's own drain immediately above, for the exact reason EnqueuePatterAsync's
        // own remarks always gave: draining first, then arming/clearing for what comes next, means an
        // already-due ceremony always gets its chance to air before this producer ever re-evaluates the
        // (now different) boundary ahead. SPEC F190.1 gives the arm an explicit `now`
        // (ArmAsync(StationIdentity, DateTimeOffset now, ct)); this call site passes the SAME one it
        // already read for BreakContext above, so the drain and the arm reason from one instant
        // instead of two reads a unit apart.
        await handoffCeremonyProducer.ArmAsync(identity, now, ct);

        await RenderPlanAsync(plan, ct);
    }

    /// <summary>
    /// gh-#300 — the one line that makes a boundary fit arguable after the fact. The 2:05 handoff
    /// was reconstructible only from kokoro's own render timestamps because this method did not
    /// exist; every term the fit reasoned from is now on the record, alongside what the sampler did
    /// with it. <see cref="IBoundaryFitLog.Log"/> forwards here explicitly (PLAN T234) — see that
    /// interface's own remarks for why a named interface replaced the delegate
    /// <see cref="MusicSelectionPolicy.SelectMusicCandidateAsync"/> used to be threaded with.
    ///
    /// <para>
    /// <b>INFORMATION, deliberately.</b> The sibling per-pick "Pick —" line is Debug, and the demo
    /// fleet ships Information and above — a fact confirmed by querying it: zero <c>dbug:</c> lines
    /// exist in Loki. A Debug fit line would satisfy the issue's letter and none of its purpose.
    /// The volume is affordable because this fires only while a deferral sits inside the F74.3
    /// lookahead window — a handful of lines per boundary, not one per pick.
    /// </para>
    ///
    /// <para>
    /// <paramref name="rung"/> is SPEC F111.5's addition (gh-#320, PLAN T234): the SPEC F111.1 ladder
    /// rung <paramref name="fit"/> resolved to, appended as its own token so every existing
    /// grep/Loki query built against <paramref name="outcome"/>'s pre-existing "win"/"least-late"/
    /// "unscored"/"drained"/"declined …" vocabulary keeps matching unchanged (additive, never a
    /// reshape of the line).
    /// </para>
    /// </summary>
    void LogBoundaryFit(
        BoundaryFitPlan fit, string outcome, BoundaryOutcome rung, IReadOnlyList<TimeSpan> sampled,
        TimeSpan? chosenDiff)
    {
        static string Secs(TimeSpan value) => value.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);

        logger.LogInformation(
            "Boundary fit ({Kind}) — untilBoundary={UntilBoundary}s queuedAhead={QueuedAhead}s " +
            "preMusicPatter={PreMusicPatter}s breakPatter={BreakPatter}s desired={Desired}s " +
            "tolerance=±{Tolerance}s confidence={Confidence} sampled=[{Sampled}] " +
            "chosenDiff={ChosenDiff} outcome={Outcome} rung={Rung}",
            fit.Kind, Secs(fit.UntilBoundary), Secs(fit.QueuedAhead), Secs(fit.PreMusicPatter),
            Secs(fit.BreakPatter), Secs(fit.DesiredEffectiveLength), Secs(fit.Tolerance),
            fit.Confidence, string.Join(", ", sampled.Select(Secs)),
            chosenDiff is { } diff ? Secs(diff) + "s" : "n/a", outcome, rung);
    }

    /// <summary>
    /// Explicit <see cref="IBoundaryFitLog"/> implementation (PLAN T234) — forwards to
    /// <see cref="LogBoundaryFit"/> verbatim so every boundary-fit line, regardless of which class
    /// decided the outcome, still lands on this SAME <c>ILogger&lt;Orchestrator&gt;</c> sink. Kept
    /// explicit rather than public: <see cref="IBoundaryFitLog"/> is internal planning wiring (its
    /// own parameter types are), so a public member here would be the wrong shape for
    /// <see cref="Orchestrator"/>'s own public surface.
    /// </summary>
    void IBoundaryFitLog.Log(
        BoundaryFitPlan fit, string outcome, BoundaryOutcome rung, IReadOnlyList<TimeSpan> sampled,
        TimeSpan? chosenDiff) =>
        LogBoundaryFit(fit, outcome, rung, sampled, chosenDiff);

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
    /// SPEC F107.6 (STORY-297, PLAN T224) — a context segment that failed to render (budget
    /// exceeded, faulted, or a null result — e.g. <c>TtsSegmentSource</c>'s own drop of non-LLM-
    /// authored context copy, mirroring PLAN T123's handoff precedent) never airs and never blocks
    /// music: WARN only, one line, naming the provider and cause (T224 review finding — the earlier
    /// shape named only the cause, leaving an operator unable to tell which provider dropped when
    /// more than one is configured; <paramref name="providerKey"/> is the SAME discriminator the
    /// Information-level freshness/blank-facts skips two calls up already name, threaded through
    /// <c>pendingRenders</c> alongside the request so this AFTER-render drop can name it too — see
    /// this class's own <c>Kick</c> local for where it rides in). No <see cref="events"/> publish —
    /// unlike <see cref="LogHandoffDrop"/>'s F92.4 booth-log entry, F107 defines no drop-specific
    /// booth-log event, and a render miss here is ordinary skip-never-silence operation (the SAME
    /// posture the drain arm's own freshness/blank-facts skips already log at Information one call
    /// up), not a ceremony half going dark. The next boundary's own drain simply gets another chance.
    /// </summary>
    void LogContextSegmentDrop(string? providerKey, string cause) =>
        logger.LogWarning(
            "Context segment for provider {ProviderKey} dropped ({Cause}) — no context item reaches " +
            "air this boundary; music continues, and the next drain retries (SPEC F107.6).",
            providerKey ?? "(unknown)", cause);

    /// <summary>
    /// SPEC F144.5 (STORY-358, PLAN T341) — an announcement segment that failed to render (budget
    /// exceeded, faulted, or a null result) never airs and never blocks music: WARN only, mirroring
    /// <see cref="LogContextSegmentDrop"/>'s own posture one method up — no booth-log entry (a later
    /// task's mark-aired/re-arm guardian owns that surface, reading <c>station.announcement</c>
    /// directly; this Orchestrator only ever vends, never transitions the row). The claimed row
    /// itself is untouched by this drop — SPEC F144.5's own re-arm (claimed -&gt; pending after one
    /// break cycle with no air) is that guardian's job, not this log line's.
    ///
    /// <paramref name="announcementId"/> names WHICH claimed row dropped (T341 review finding F8 —
    /// the SAME <see cref="LogContextSegmentDrop"/> providerKey precedent immediately above: an
    /// operator staring at this WARN with more than one announcement claimed this unit needs to know
    /// which row is still sitting claimed, not merely that "an" announcement dropped), threaded
    /// through <c>pendingRenders</c> alongside the request exactly like <c>ContextProviderKey</c>
    /// already is — see that field's own remarks. <see langword="null"/> only for a hypothetical
    /// caller that reaches this method without ever having claimed a row; today's one call site
    /// (<c>KickAnnouncement</c>) always supplies the claimed <see cref="AnnouncementItem.Id"/>.
    /// </summary>
    void LogAnnouncementDrop(long? announcementId, string cause) =>
        logger.LogWarning(
            "Announcement {AnnouncementId} dropped ({Cause}) — the claimed row does not air this unit; " +
            "music continues (SPEC F144.5).",
            announcementId?.ToString(CultureInfo.InvariantCulture) ?? "(unknown)", cause);

    /// <summary>
    /// gh-#259 — resolves the display name the whole UNIT's items are attributed to (the music
    /// track's <see cref="MediaItem.DjName"/> stamp, and the StationId segment's), from ONE
    /// <paramref name="personaAccessor"/> read per unit. Deliberately separate from
    /// <c>ResolvePersonaAsync</c> (now on <see cref="BreakPlanner"/>)'s per-segment voice+name reads (SPEC F35.3/F39.1 —
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

    // SPEC F144.3/F144.4's flavored-render fallback moved to BreakRenderer.ResolveFlavoredCopyAsync
    // at PLAN T534 — quiet there (SPEC F191.5, no logger dependency) rather than logging its own WARN
    // on a caught fault, since a null announcementCopyWriter seam or the writer's own never-throws
    // contract slipping both still degrade to the plain verbatim read either way.
}
