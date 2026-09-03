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
/// for the F92.3 dedupe rules and the explicit <see cref="SpeechDeferralQueue.ClearStale"/> a boundary
/// leaving the window triggers.
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
/// (read fresh, per drain, through <paramref name="contextSettings"/>) picks the voice: a positive id
/// names an explicit persona (resolved through <paramref name="personaStore"/>, degrading to the
/// station voice on any miss); zero, negative, or unset defers to the on-air DJ via the SAME
/// <see cref="ResolvePersonaAsync"/> every LeadIn/BackAnnounce segment already uses — whose own
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
/// copy — <see cref="BuildTimeDateRequest"/> reads the hour off the deferral's own <c>Due</c> instant
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
/// <c>now</c>). <see cref="BuildTimeDateRequest"/> stamps the result onto the <see cref="SegmentRequest"/>
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
/// attempt itself lives in <see cref="EnqueuePatterAsync"/>, gated on THREE conditions — two structural,
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
/// BackAnnounce requests — see <see cref="EnqueuePatterAsync"/>'s own remarks for the rest.
/// </para>
///
/// <para>
/// <b>Owner announcements (SPEC F144.1/F144.2, F145.2, STORY-358, PLAN T341):</b> <paramref name="announcementSource"/>
/// is the crosstalkPlanner precedent one feature over — an optional constructor dependency, feature
/// dark whenever null (no Host wiring — every pre-T341 construction site, including every unit
/// test), so this widening is diff-free for every existing caller. <see cref="EnqueuePatterAsync"/>'s
/// own announcement step vends up to <see cref="AnnouncementVendCap"/> (a constant on THIS class —
/// <see cref="IAnnouncementSource.ClaimDeliverableAsync"/> places no ceiling of its own, see that
/// seam's own remarks) oldest deliverable announcements, atomically claimed the moment this unit
/// decides to vend them, and places each as a <see cref="SegmentKind.Announcement"/> segment after
/// the back-announce and before the lead-in — exactly like <see cref="SegmentKind.Crosstalk"/>'s own
/// placement one paragraph up, just a different pair of steps in the SAME unit. The station never
/// reads <c>Station:SpectatorMode</c> to decide whether to vend (SPEC F145.2's "the Orchestrator
/// never reads privacy state" ruling): an empty claim — refused because the station is public, or
/// genuinely nothing pending — looks identical from here, and needs no different handling either way.
/// <paramref name="announcementRenderer"/> renders each claimed item's exact message text with ZERO
/// LLM involvement (SPEC F144.2) — a SEPARATE seam from <paramref name="tts"/>, never routed through
/// it, because neither <see cref="SegmentRequest"/> nor <see cref="ISegmentCopyWriter"/> can carry a
/// caller-supplied exact text without either widening the published Abstractions record or forcing
/// the render through the SAME copy-writer chain an LLM writer sits in front of (see
/// <see cref="IVerbatimSegmentRenderer"/>'s own remarks).
///
/// <b>The flavored path (SPEC F144.3/F144.4, PLAN T342):</b> a <c>Verbatim: false</c> announcement
/// FIRST attempts <paramref name="announcementCopyWriter"/> — its OWN dedicated seam, the
/// crosstalkPlanner precedent one feature over (optional, feature-dark whenever null), never
/// <paramref name="tts"/>/<see cref="ISegmentCopyWriter"/> either. THE FALLBACK LAW is exactly one
/// <c>??</c> at the vend step below: any failure there (a disabled/unreachable LLM, a blown render
/// budget, or the F138.4 re-ask ladder exhausting on either a fabrication or the F144.3 containment
/// check) resolves to <see langword="null"/>, and the owner's own message renders verbatim instead —
/// through this SAME <paramref name="announcementRenderer"/>, since flavored copy IS exact once
/// written and needs no different rendering path from a verbatim read. A <c>Verbatim: true</c>
/// announcement never even asks <paramref name="announcementCopyWriter"/>, the owner having asked for
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
/// <b>Ad cadence (SPEC F158.2/F158.3, STORY-388, PLAN T397):</b> <paramref name="adCadenceProvider"/>
/// is <paramref name="cadenceProvider"/>'s twin one seam over — read fresh, once, inside
/// <c>EnqueuePatterAsync</c> (never hoisted alongside <c>cadence</c>/<c>identity</c> at the top of
/// <see cref="GetNextAsync"/>: unlike the StationId trigger, no boundary-fit estimate ever needs to
/// account for an ad's patter length ahead of the drain — the vend is pre-rendered, its duration
/// already fixed the instant it airs, and a null answer costs the boundary nothing to plan around).
/// <c>unitCount % EveryNUnits == 0</c>, unit 0 never fires — <c>cadence.StationIdEveryNUnits</c>'s own
/// guard, mirrored exactly. The trigger enqueues a <see cref="SpeechDeferralKind.Ad"/> deferral,
/// never builds a segment directly (the SAME F74.1/F74.2 queue-not-inline discipline the StationId
/// trigger already follows); the SAME drain loop picks it up and vends through
/// <paramref name="adSpotVend"/> — <see cref="IAdSpotVend.GetNextSpotAsync"/>'s pre-rendered
/// <see cref="MediaItem"/>, routed through <c>KickResolved</c> (zero render at air, the pool-first
/// StationId item's own precedent). <see cref="SpeechDeferralKind.Ad"/>'s own declared-LAST position
/// in that enum is what actually orders "ident → spot → back to the music" (SPEC F158.3) whenever
/// both cadences coincide on the SAME unit — see that member's own remarks for the full tiebreak
/// argument. <paramref name="adSpotVend"/> throwing is caught at this drain arm's own boundary (WARN,
/// no ad this break, never a faulted unit); a null answer is one INFO, never a WARN — an empty Ads
/// library is a normal day (F158.3). Both seams default to a permanent no-op (zero/null) for any
/// composition that never wires <c>GenWave.Ads</c>' own <c>AddGenWaveAds</c> — every pre-T397
/// construction site, including every unit test, stays byte-identical.
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
    IPatterDurationEstimator? patterEstimator = null,
    IContextSettingsProvider? contextSettings = null,
    IMediaCatalog? catalog = null,
    IStationImagingSettingsProvider? imagingSettings = null,
    CrosstalkPlanner? crosstalkPlanner = null,
    IAnnouncementSource? announcementSource = null,
    IVerbatimSegmentRenderer? announcementRenderer = null,
    ITtsVoiceLister? voiceLister = null,
    IAnnouncementCopyWriter? announcementCopyWriter = null,
    IAdCadenceProvider? adCadenceProvider = null,
    IAdSpotVend? adSpotVend = null) : INextItemProvider, IBoundaryFitLog
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

    // SPEC F144.1 (STORY-358, PLAN T341) — the vend CEILING belongs to the caller, not the seam:
    // IAnnouncementSource.ClaimDeliverableAsync places none of its own (see that method's own
    // remarks). A plain positive constant, the same posture SignOffLeadTime/TimeDateHonestyThreshold
    // immediately above carry — not a live-tunable SPEC knob.
    const int AnnouncementVendCap = 2;

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

    // SPEC F107.7 (STORY-297, PLAN T224): the Context:{Key}:PersonaId seam — same null-coalesced-
    // default idiom as events/patterEstimator above. A host that has not yet wired T226's real
    // IOptionsMonitor-backed implementation (every pre-T226 construction site, including every unit
    // test) reads back "no explicit persona configured for any key", which the drain arm's own
    // resolution degrades to the on-air DJ — never a null-check, never a stall.
    readonly IContextSettingsProvider contextSettings = contextSettings ?? NoOpContextSettingsProvider.Instance;

    // SPEC F124.4 (PLAN T269): same null-coalesced-default idiom as contextSettings immediately
    // above. A host that has not yet wired the real IOptionsMonitor-backed implementation (every
    // pre-T269 construction site, including every unit test) reads back NoOpStationImagingSettingsProvider's
    // both-false/5-minute answer — the shipped SPEC F124.4 default — never a null-check, never a stall.
    readonly IStationImagingSettingsProvider imagingSettings = imagingSettings ?? NoOpStationImagingSettingsProvider.Instance;

    // SPEC F158.3 (STORY-388, PLAN T397): the ad cadence trigger's own live knob
    // (Station:Ads:EveryNUnits) — same null-coalesced-default idiom as imagingSettings immediately
    // above. A host that has not yet wired the real IOptionsMonitor-backed implementation (every
    // pre-T397 construction site, including every unit test) reads back NoOpAdCadenceProvider's own
    // zero (disabled) answer — never a null-check, never a stall.
    readonly IAdCadenceProvider adCadenceProvider = adCadenceProvider ?? NoOpAdCadenceProvider.Instance;

    // SPEC F158.2/F158.3 (STORY-388, PLAN T397): the ad drain's own vend seam — same
    // null-coalesced-default idiom immediately above. A host that has not yet wired GenWave.Ads'
    // real AdSpotPipeline (every pre-T397 construction site, including every unit test) reads back
    // NoOpAdSpotVend's own permanent-null answer — "no ad ever airs" — never a null-check, never a
    // stall.
    readonly IAdSpotVend adSpotVend = adSpotVend ?? NoOpAdSpotVend.Instance;

    readonly Queue<MediaItem> buffer = new();
    MediaItem? previousTrack;
    int unitCount;

    // SPEC F92.1/F92.3 arm-once state (T124 review finding F2), widened at F116.2/PLAN T248 to also
    // key on show id: the (BoundaryAt, outgoing persona id, incoming persona id, outgoing show id,
    // incoming show id) tuple EnqueueHandoffCeremonyAsync last acted on — null before the first unit,
    // or once a boundary has left the window and was explicitly cleared. Re-evaluating this producer
    // every unit is by design (a schedule write must be noticed promptly), but ACTING on the SAME
    // tuple twice is not: see EnqueueHandoffCeremonyAsync's own remarks for the double-sign-off bug
    // this guards against. The two show-id members are additive (T248): a showless schedule always
    // reads both as null on both sides, so this tuple behaves byte-identically to the pre-T248 triple
    // for every station that has never assigned a show — the widening only ever matters for an
    // in-window edit that changes a block's show_id without also changing its persona_id, which
    // otherwise this arm-once guard would wrongly treat as "nothing changed".
    (DateTimeOffset BoundaryAt, long? OutgoingPersonaId, long? IncomingPersonaId, long? OutgoingShowId, long? IncomingShowId)? lastArmedHandoff;

    // T124 review finding F7: fires at most once for the life of this Orchestrator — a null
    // scheduleResolver makes EnqueueHandoffCeremonyAsync a permanent no-op, which would otherwise be
    // completely silent (no format-clock schedule wired is a perfectly valid, common station shape,
    // but an operator who DID intend to wire one deserves one loud signal that it never arrived).
    bool scheduleResolverMissingWarned;

    // SPEC F141.1/F141.4 (STORY-355, PLAN T326, review advisory) — the SAME "fires at most once for
    // the life of this Orchestrator" idiom as scheduleResolverMissingWarned immediately above,
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
            // every kind, so nothing else is currently due for EnqueueHandoffCeremonyAsync's own
            // re-evaluation to wrongly race ahead of — unlike its ordinary step-2.5 call (deliberately
            // placed AFTER the drain, see EnqueuePatterAsync's own remarks, so it never clears a piece
            // the drain was about to fire), calling it here first is safe precisely because there is no
            // due piece for it to preempt. A schedule write that already superseded or retracted this
            // ceremony (SPEC F92.1 revisit) takes effect right here, before this unit's own forced
            // drain would otherwise race past it. The SAME method runs again from inside
            // EnqueuePatterAsync's normal step 2.5 immediately below; seeing the identical
            // (now-reconciled) key, that call is a safe no-op (the arm-once guard).
            await EnqueueHandoffCeremonyAsync(identity.Voice, ct);

            // T235 review finding F2: only force when the reconciliation left the EXACT ceremony
            // peeked above untouched — reconciledSignOff.Due == pending.Due proves nothing changed
            // (same key, same due). A schedule write that MOVED the boundary re-arms a SignOff at a
            // DIFFERENT due (still non-null — it is still in-window, just for a new boundary); forcing
            // to that reconciled-but-different due fired the sign-off against the WRONG boundary (the
            // field report: 6:45 early), and CaptureCrossingTrackForHeldSignOn would have stamped the
            // MOVED SignOn's copy with a track that has nothing to do with its own, later boundary. The
            // fresh, moved ceremony gets its own fair shot at a later, correctly-classified straddle
            // unit instead — this unit simply falls through to the ordinary unforced drain below.
            //
            // A null reconciledSignOff is the OTHER shape this SAME reconciliation can take (T235
            // review finding F3, the retraction half): the boundary left the F74.3 window entirely (or
            // collapsed to a gap-to-gap/self-handoff), and EnqueueHandoffCeremonyAsync's own
            // ClearCeremony wiped BOTH pieces — there is no ceremony left, moved or otherwise, to force.
            // That falls through to the SAME ordinary unforced drain below, which is simply a no-op for
            // a ceremony that no longer exists — this unit plans as an ordinary music unit.
            if (deferralQueue.Peek(SpeechDeferralKind.SignOff) is { } reconciledSignOff
                && reconciledSignOff.Due == pending.Due)
            {
                CaptureCrossingTrackForHeldSignOn(track);
                drainAsOf = reconciledSignOff.Due;
                hold = HoldSignOnAtStraddle;
            }
        }

        await EnqueuePatterAsync(
            previousTrack, track, unitDjName, cadence, identity, ct, drainAsOf, hold,
            queuedAhead: TimeSpan.FromMilliseconds(ctx.QueuedAheadMs ?? 0));

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
    /// hands its paired SignOn to <see cref="HoldSignOnPastQueuedTail"/> — the SAME
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
        var boundary = timeProvider.GetUtcNow() + drainDelay;

        // Only a queue-crossing SignOff holds its paired SignOn — see this method's own remarks.
        // Everything else (a SignOn-headed fit already back for its own seam, or a non-crossing
        // CeremonyOnly fit) takes the ordinary unforced drain, hold: null.
        IReadOnlySet<SpeechDeferralKind>? hold = null;
        if (rung == BoundaryOutcome.Straddle && fit.Kind == SpeechDeferralKind.SignOff)
        {
            HoldSignOnPastQueuedTail(fit.QueuedAhead);
            hold = HoldSignOnAtStraddle;
        }

        await EnqueuePatterAsync(
            previousTrack, next: null, unitDjName, cadence, identity, ct, boundary, hold,
            queuedAhead: fit.QueuedAhead);

        return buffer.Count > 0 ? buffer.Dequeue() : null;
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
    /// the bug. The straddle unit (SPEC F111.2, PLAN T235) passes a pending SignOff's own Due for the
    /// identical reason — see <see cref="GetNextAsync"/>'s own straddle branch.
    /// </param>
    /// <param name="hold">
    /// SPEC F111.2 (PLAN T235) — forwarded verbatim to <see cref="SpeechDeferralQueue.TryDequeueDue"/>;
    /// see that method's own remarks. <see langword="null"/> (nothing held) for every caller except the
    /// straddle branch.
    /// </param>
    /// <param name="queuedAhead">
    /// SPEC F124.4 (PLAN T269) — forwarded verbatim to <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s
    /// own TimeDate elapsed-due expiry math (air-time lateness, never wall-clock-vs-Due alone — see
    /// that method's own remarks). <see cref="GetNextAsync"/>'s ordinary unit path passes
    /// <c>PlayoutContext.QueuedAheadMs</c> (coalesced null-to-zero); <see cref="TryServeCeremonyOnlyUnitAsync"/>
    /// passes the SAME <c>fit.QueuedAhead</c> its own drain-instant arithmetic reads — the feeder's raw,
    /// UNCLAMPED estimate in both cases, never the window-clamped local either method may compute for
    /// its own, separate purpose. Defaults to <see langword="default"/> (zero), harmless on its own —
    /// the expiry math never runs unless a non-null budget is also in play (see
    /// <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s own remarks).
    /// </param>
    async Task EnqueuePatterAsync(
        MediaItem? prev, MediaItem? next, string? unitDjName, CadenceConfig cadence, StationIdentity identity,
        CancellationToken ct, DateTimeOffset? drainAsOf = null, IReadOnlySet<SpeechDeferralKind>? hold = null,
        TimeSpan queuedAhead = default)
    {
        // Read the render budget ONCE per unit, up front (SPEC F44.2, gitea-#197) — the same
        // per-unit-snapshot discipline cadence/identity arrive under (see the params above): a
        // live Tts:RenderBudgetSeconds edit must not straddle a single unit's renders. Never read
        // renderBudgetProvider.Current again below this line.
        var renderBudget = renderBudgetProvider.Current;

        // SPEC F124.4/F141.1 (PLAN T269/T326) — the SAME per-unit-snapshot discipline applied to the
        // live TimeDate elapsed-due expiry budget: a live Station:Imaging:TimeAnnouncementBudgetSeconds
        // edit must not straddle a single unit's drain. Converted to a TimeSpan here, at the point of
        // use — GenWave.Orchestration references only GenWave.Core/GenWave.Abstractions and stays
        // options/config-agnostic, so SpeechDeferralQueue itself never sees the raw seconds shape.
        var timeDateStaleBudget = TimeSpan.FromSeconds(imagingSettings.Current.TimeAnnouncementBudgetSeconds);

        // SPEC F141.1/F141.4 (STORY-355, PLAN T326, review advisory) — the boot-log config echo (see
        // timeDateBudgetLoggedOnce's own remarks for why it lives here rather than on a bystander
        // Host service): logs the bound value exactly once, on this Orchestrator's first unit, then
        // never again — every later unit still reads imagingSettings.Current fresh above, live-edits
        // included, this flag only silences the REPEAT logging.
        if (!timeDateBudgetLoggedOnce)
        {
            timeDateBudgetLoggedOnce = true;
            logger.LogInformation(
                "TimeDate honesty budget bound: {TimeAnnouncementBudgetSeconds}s (SPEC F141.1)",
                imagingSettings.Current.TimeAnnouncementBudgetSeconds);
        }

        // Each segment's voice+persona-name pair is resolved (a fast, local accessor call — SPEC
        // F35.3, F39.1) immediately before that segment's SegmentRequest is built, so the actual TTS
        // renders below still all kick off back-to-back with no render awaited in between
        // (render-ahead is unaffected — the accessor call is negligible next to a real render's
        // synthesis+mix+measure latency). ResolvePersonaAsync reads personaAccessor exactly ONCE per
        // call, returning both values from the same read (F39.1) — never resolve Voice and
        // PersonaName from two separate accessor calls, which could straddle a concurrent
        // activate/deactivate and pair a stale name with a fresh voice or vice versa.
        // Carried exception (T224 review, accepted as-is): a SpeechDeferralKind.Context drain with a
        // positive Context:{Key}:PersonaId resolves through a genuine personaStore.GetByIdAsync round
        // trip between two Kicks instead — not the negligible accessor read every other kind gets.
        // The full request rides alongside each render task — Kind for the F92.4 drop
        // classification (SPEC F92.4, PLAN T124: the await loop below tells a handoff-kind drop,
        // WARN + booth row, from every other kind's ordinary silent skip), and Voice/PersonaName for
        // the gh-#253 measured-duration observation a successful render feeds the estimator — the
        // render itself is still kicked off immediately here, nothing awaited in between.
        // ContextProviderKey (T224 review finding — the WARN this key ultimately feeds must name
        // WHICH provider dropped, not just "a context segment") rides alongside for exactly one
        // kind: null for every other kind's render, the drain arm's own providerKey for
        // SpeechDeferralKind.Context's Kick call below.
        // ObserveDuration (SPEC F110.2, PLAN T232) gates the gh-#253 estimator feed below — true for
        // every genuine TTS render (unchanged), false for a pool-first StationId item (KickResolved):
        // an authored ident's measured duration reflects whatever the operator produced, not a
        // synthesis the estimator should learn from — blending it into a templated-TTS StationId
        // bucket would skew future boundary-fit estimates for the fallback rung, which is still a
        // real TTS render with its own, separate duration profile. "A templated-TTS StationId bucket"
        // is now plural, not singular (SPEC F117.2, PLAN T250 review finding F1): the estimator keys
        // its Exact tier on (voice, show-or-null), so the plain ident and each show's own templated
        // line each land in their OWN bucket — a pool-first item still observes into NONE of them.
        // AnnouncementId (T341 review finding F8) rides alongside for exactly one kind — same
        // shape as ContextProviderKey immediately to its left: null for every other kind's render,
        // the claimed row's own id for SegmentKind.Announcement's Kick call below, so a drop this
        // unit's own drain WARN can name WHICH claimed row is still sitting undelivered.
        var pendingRenders =
            new List<(SegmentRequest Request, Task<MediaItem?> Render, string? ContextProviderKey, bool ObserveDuration, long? AnnouncementId)>();

        // Starts one render and remembers the request alongside the Task (T124 review simplify) —
        // every call site below used to repeat the Add call verbatim; the render itself is still
        // kicked off immediately, nothing awaited in between.
        void Kick(SegmentRequest request, string? contextProviderKey = null) =>
            pendingRenders.Add((request, tts.RenderAsync(request, ct), contextProviderKey, true, null));

        // SPEC F110.2 (STORY-301, PLAN T232) — the pool-first sibling of Kick: no render to start,
        // but the SAME ordering guarantee every other segment gets. The render-await loop below
        // enqueues buffer items strictly in Kick/KickResolved CALL order (never completion order —
        // see that loop's own remarks), which is what keeps a pool-first ident from ever jumping
        // ahead of a back-announce or behind a lead-in Kicked on either side of it. An already-
        // completed Task stands in for the (nonexistent) render, so Task.WhenAny below resolves it
        // instantly, no render-budget delay spent on an item that never needed one.
        void KickResolved(SegmentRequest request, MediaItem item) =>
            pendingRenders.Add((request, Task.FromResult<MediaItem?>(item), null, false, null));

        // SPEC F127.1/.7/.8/.9 (STORY-329, PLAN T287) — the crosstalk vend attempt: gated on THREE
        // conditions (see this class's own "Crosstalk" remarks above for why the third exists) — next
        // is not null (an ordinary music unit; gh-#300's ceremony-only call passes null), drainAsOf is
        // null (excludes the straddle branch's own forced-ahead SignOff drain — the only OTHER caller
        // that sets it), AND CeremonyDrainsThisBreak() is false. The third is a PEEK, not a structural
        // exclusion like the first two: a SignOff/SignOn already due (or overdue) never gets a
        // BoundaryFitPlan at all, so the first two conditions alone never see it, yet it still drains a
        // few lines below via the ordinary TryDequeueDue(drainNow) call regardless (SPEC F127.8 review
        // F2). Decided BEFORE step 1 below, never after: SPEC F127.9's supersede must already be known
        // before this SAME unit's BackAnnounce/LeadIn requests are built, a few lines down.
        var vendedCrosstalk = next is not null && drainAsOf is null && !CeremonyDrainsThisBreak()
            ? TryVendCrosstalkForThisBreak()
            : null;
        var crosstalkAiredThisBreak = vendedCrosstalk is not null;

        // SPEC F127.8 review F2 — mirrors SpeechDeferralQueue.TryDequeueDue's own Due/NotBefore
        // eligibility check (its Pass 1) for exactly the two handoff kinds ever drained here
        // (SignOff/SignOn), without dequeuing anything: true when either would ACTUALLY leave the
        // queue at THIS SAME unit's own drain, a few lines below (drainAsOf ?? now — collapses to
        // plain "now" on every path that reaches this branch, since the caller above already proved
        // drainAsOf is null before ever calling this). The hold parameter TryDequeueDue itself takes is
        // never consulted here: hold is non-null ONLY together with a non-null drainAsOf (the straddle
        // branch), which the caller's own drainAsOf-is-null check already excludes before this runs.
        bool CeremonyDrainsThisBreak()
        {
            var realNow = timeProvider.GetUtcNow();
            var drainNow = drainAsOf ?? realNow;
            return WouldDrainAt(deferralQueue.Peek(SpeechDeferralKind.SignOff), drainNow, realNow)
                || WouldDrainAt(deferralQueue.Peek(SpeechDeferralKind.SignOn), drainNow, realNow);
        }

        static bool WouldDrainAt(SpeechDeferral? deferral, DateTimeOffset drainNow, DateTimeOffset realNow) =>
            deferral is not null
            && deferral.Due <= drainNow
            && (deferral.NotBefore is not { } notBefore || notBefore <= realNow);

        // Local to this method (mirrors Kick/KickResolved's own placement one function up) —
        // CrosstalkPlanner.TryVend is pure, in-memory, synchronous state; there is no render to await,
        // so nothing here needs async. The failure-path delete this integration owns (PLAN T287
        // rider): TryVend has ALREADY removed exchange from stock the instant it hands it back, so a
        // vanished asset (deleted out of band, or a race with a fresh CrosstalkStockWorker's own
        // startup purge) must not silently leak.
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
                personaName)
            {
                CrosstalkAiredThisBreak = crosstalkAiredThisBreak,
            };
            Kick(req);
        }

        // 1.5. Crosstalk banter (SPEC F127.1/.6/.11, STORY-329, PLAN T287) — one cached asset the
        // feeder treats as a normal item (SPEC F66.1's shape, exactly like a pool-first StationId
        // ident's own KickResolved precedent): no render, the exchange was already fully mixed and
        // measured ahead of air (T284/T286's off-clock generation). Kicked AFTER the back-announce and
        // BEFORE the station-id/lead-in steps below, so it airs as this break's own mid-block color:
        // the outgoing track's back-announce, then banter, then whatever imaging/lead-in follows.
        //
        // MediaItem presentation (build-time decision, T287): Title is the STATION name and Artist is
        // "unitDjName ?? identity.Name" — the SAME shape every other TTS-kind segment already presents
        // (TtsSegmentSource.RenderAsync's own Title/Artist stamp) — never the neighbor persona's name,
        // which would be a NEW disclosure the spectator now-playing surface has never carried for any
        // kind. DjName is the unit's own on-air host persona (unitDjName) — the host's own voice opens
        // the exchange (SPEC F127.2), so Now Playing attribution stays exactly what the rest of this
        // unit already carries; the neighbor voice is audible on air but never a NEW distinct
        // attribution field (F127.11's own booth-log stamp, not the spectator surface, is where "who
        // else spoke" is answerable). MediaId keeps the tts: prefix (excluded from the recent-ids list
        // the SAME way every other TTS segment already is, SPEC F12.6) even though it is not a
        // TtsSegmentSource cache key — the asset's own filename (a GUID, CrosstalkAssembler.AssembleAsync)
        // is unique per exchange, so no second id-uniqueness scheme is needed.
        if (vendedCrosstalk is { } exchangeToAir)
        {
            var crosstalkMediaId = $"tts:crosstalk:{Path.GetFileNameWithoutExtension(exchangeToAir.AssetPath)}";
            var crosstalkRequest = new SegmentRequest(
                SegmentKind.Crosstalk,
                identity.Voice,
                identity.Name,
                null,
                StationLocalNow(),
                identity.Id,
                PersonaName: unitDjName);
            var crosstalkItem = new MediaItem(
                crosstalkMediaId, exchangeToAir.AssetPath, identity.Name, exchangeToAir.Loudness,
                Artist: unitDjName ?? identity.Name, Cue: exchangeToAir.Cue, DurationMs: exchangeToAir.DurationMs,
                DjName: unitDjName, SegmentKind: SegmentKind.Crosstalk)
            {
                CrosstalkScript = exchangeToAir.Script,
            };
            crosstalkPlanner?.MarkVended(crosstalkMediaId, exchangeToAir);
            KickResolved(crosstalkRequest, crosstalkItem);
        }

        // 1.75. Owner announcements (SPEC F144.1/F144.2, STORY-358, PLAN T341) — up to
        // AnnouncementVendCap oldest deliverable, atomically claimed the moment this unit decides to
        // vend them (pending -> claimed, IAnnouncementSource's own SQL), placed after the
        // back-announce and before the lead-in (F144.1) — Kicked here, after crosstalk, before the
        // station-id/lead-in steps below, the SAME slot crosstalk's own remarks describe one step up.
        // A null announcementSource OR announcementRenderer (no Host wiring, or a pre-T341
        // construction site) makes this whole step a permanent no-op — the crosstalkPlanner
        // precedent. See this class's own remarks for the full feature (privacy, voice validation,
        // the id-in-MediaId carry).
        //
        // CADENCE-INDEPENDENT (T341 review ruling): this step runs on EVERY unit, gated only on the
        // two seams being wired — unlike the back-announce/station-id/lead-in steps around it, no
        // CadenceConfig knob can turn it off or on, so "after the back-announce" degrades to "same
        // slot in unit order" when the cadence airs no back-announce at all, and the vend also runs
        // on the ceremony-only unit path. An owner's message must never be hostage to a cadence flag.
        if (announcementSource is { } source && announcementRenderer is { } renderer)
        {
            async Task<MediaItem?> RenderAnnouncementAsync(SegmentRequest announcementRequest, AnnouncementItem announcement)
            {
                // SPEC F144.3/F144.4 (STORY-358, PLAN T342) — THE FALLBACK LAW, in one `??`:
                // Verbatim:false attempts the flavored path FIRST, through the dedicated
                // IAnnouncementCopyWriter seam (never tts/ISegmentCopyWriter — see this class's own
                // remarks); ANY failure there (feature dark, a disabled/unreachable LLM, a blown
                // render budget, or the F138.4 ladder exhausting on either a fabrication or the F144.3
                // containment check) resolves to null, and the owner's own verbatim message airs
                // instead. Verbatim:true skips the attempt entirely — the owner asked for their own
                // unflavored words.
                var flavoredText = announcement.Verbatim
                    ? null
                    : await ResolveFlavoredAnnouncementCopyAsync(announcementRequest, announcement.Message, ct);

                // FreshPerAiring: true is THE contract TtsSegmentSource's own drop guard pins (SPEC
                // F144.2/F144.4, the T338 review carry-forward): per-announcement owner text — flavored
                // or verbatim alike — is fresh by definition (the operator's own words, or the active
                // persona's own in-character rendering of them, never a templated fixed phrase) and
                // must land in the swept blurbs/ dir, never the forever-cache.
                var copy = new SegmentCopy(flavoredText ?? announcement.Message, FreshPerAiring: true);
                var rendered = await renderer.RenderAsync(announcementRequest, copy, ct);

                // The announcement id rides the rendered segment's own MediaId (SPEC F144.1's carry
                // requirement) rather than a new member on SegmentRequest/MediaItem — see
                // AnnouncementMediaId's own remarks for why, and for T343's own lookup this enables.
                return rendered is { } item
                    ? item with { MediaId = AnnouncementMediaId.Wrap(announcement.Id, item.MediaId) }
                    : null;
            }

            void KickAnnouncement(AnnouncementItem announcement, string voice)
            {
                var req = new SegmentRequest(
                    SegmentKind.Announcement, voice, identity.Name, null, StationLocalNow(), identity.Id);
                pendingRenders.Add((req, RenderAnnouncementAsync(req, announcement), null, true, announcement.Id));
            }

            var deliverable = await ClaimAnnouncementsAsync(source, ct);
            foreach (var announcement in deliverable)
            {
                var voice = await ResolveAnnouncementVoiceAsync(announcement.RequestedVoice, identity.Voice, ct);
                KickAnnouncement(announcement, voice);
            }
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

        // 2.25. Ad cadence every N units (SPEC F158.3, STORY-388, PLAN T397) — the StationId
        // trigger's own twin, one field up: SAME unitCount > 0 boot-guard, SAME "enqueue a deferral,
        // let the drain immediately below pick it up" shape (F74.1/F74.2's queue-not-inline
        // discipline applies here too). Read into a local ONCE — never adCadenceProvider.Current
        // twice — and reuse it for both the guard and the divisor: a second, independent read here
        // could race a live Station:Ads:EveryNUnits reload between the two (5 -> 0), turning
        // `unitCount % adCadenceProvider.Current` into a DivideByZeroException the guard just
        // proved wouldn't happen. SpeechDeferralKind.Ad is declared LAST in that enum specifically
        // so its own Kind tiebreak sorts AFTER StationId whenever both fire on the exact same
        // instant (the common case: both triggers enqueue with Due = now on THIS unit) — see that
        // enum member's own remarks for why that, not enqueue-call order, is what actually orders
        // the drain "ident → spot".
        var adEveryNUnits = adCadenceProvider.Current;
        if (next is not null
            && adEveryNUnits > 0
            && unitCount > 0
            && unitCount % adEveryNUnits == 0)
        {
            deferralQueue.Enqueue(SpeechDeferralKind.Ad, "cadence: Station:Ads:EveryNUnits");
        }

        // Drain every deferral due at this boundary — BEFORE the handoff producer below runs (T124
        // review finding). Reads the SAME injected clock GetNextAsync compares NextDue against
        // (SPEC F74.3) — one clock for both halves of this seam, never a mix of a real and a fake
        // one. Written for ANY due deferral, including one enqueued several units ago (SPEC
        // F74.1 — "regardless of wall-clock slip"). Hoisted into a local (T224) so the
        // SpeechDeferralKind.Context arm's own freshness re-check below compares against the EXACT
        // same instant the dequeue decision itself was made against, rather than a second, later
        // clock read.
        var drainNow = drainAsOf ?? timeProvider.GetUtcNow();
        foreach (var deferral in deferralQueue.TryDequeueDue(
            drainNow, hold, queuedAhead, timeDateStaleBudget,
            onExpired: (expiredDeferral, lateness) => LogTimeDateExpiry(expiredDeferral, lateness, timeDateStaleBudget)))
        {
            switch (deferral.Kind)
            {
                case SpeechDeferralKind.StationId:
                {
                    // SPEC F110.2 (STORY-301, PLAN T232) — pool-first: an authored station_id row
                    // from the operator's own imaging collection airs ahead of the templated TTS
                    // ident whenever one is ready. The DB call happens HERE, at drain time inside
                    // this render-ahead section — the SAME discipline exception T224 ruled for the
                    // Context arm's persona-id lookup two cases down: a genuine catalog round trip
                    // rather than the fast per-unit accessor read every other kind gets, safe
                    // because a drain only ever runs at a boundary (SPEC F74.1 — never mid-track).
                    // A null catalog (no IMediaCatalog wired — an older host, or a test double that
                    // never scripts one) skips the pool outright, same as a genuinely empty one.
                    //
                    // SPEC F117.2 (STORY-309, PLAN T250) — the on-air show, read via the SAME
                    // CachingScheduleResolver.TryGetCurrent() synchronous snapshot
                    // OnAirPersonaAccessor's own hot path already trusts: no extra store round trip,
                    // and a null scheduleResolver or a not-yet-warm cache both degrade to "no show" —
                    // exactly the branch below that keeps this arm byte-identical to F110.2. Read
                    // ONCE into a local, never twice: the Id handed to the pool query below and the
                    // Name that may decide the templated floor further down must describe the SAME
                    // on-air show, not two snapshots straddling a boundary flip.
                    var currentShow = scheduleResolver?.TryGetCurrent()?.Show;

                    // The pool query itself now carries the WHOLE show-scope preference ladder
                    // (MediaRepository.GetRandomReadyByImagingKindAsync's own remarks, T250):
                    // show-scoped rows win when currentShow is set, the station-wide (unscoped) pool
                    // is the fallback, and a foreign-show row is never a candidate — currentShow?.Id
                    // is exactly "no show" (null) on the F110.2 path this arm has always had.
                    var pooled = catalog is null
                        ? null
                        : await catalog.GetRandomReadyByImagingKindAsync(
                            scopeProvider.Current, ImagingKind.StationId, currentShow?.Id, ct);
                    var stationIdReq = BuildStationIdRequest(identity);
                    if (pooled is not null)
                    {
                        KickResolved(stationIdReq, BuildPooledStationIdItem(pooled));
                        break;
                    }

                    // SPEC F117.2 — the templated show line is the floor for a show with no ready
                    // pool row at all (scoped or station-wide): the SAME SegmentKind.StationId
                    // request, ShowName additionally stamped, so PatterTemplateRenderer's StationId
                    // arm renders "You're listening to {show} on {station}." instead of the plain
                    // ident — station-voiced, zero LLM, forever-cached exactly like every other
                    // StationId render (BuildStationIdRequest's own remarks), no new SegmentKind
                    // needed. currentShow null (no show on the air) falls straight through to the
                    // ORIGINAL plain ident — byte-identical to F110.2, the required outside-show
                    // posture.
                    Kick(currentShow is { } show ? stationIdReq with { ShowName = show.Name } : stationIdReq);
                    break;
                }

                case SpeechDeferralKind.Ad:
                {
                    // SPEC F158.2/F158.3 (STORY-388, PLAN T397) — the vend IS the whole segment: a
                    // pre-rendered MediaItem, zero render at air, routed through KickResolved exactly
                    // like the pool-first StationId item above (the SAME buffer-ordering guarantee,
                    // so an ad can never jump ahead of a back-announce Kicked earlier this unit, nor
                    // behind the lead-in Kicked below). A throwing vend is caught HERE, at this
                    // seam's own boundary — never letting an IAdSpotVend fault this whole unit — and
                    // logged WARN with no ad this break (F158.3's own "never a failed unit" contract);
                    // AdSpotPipeline itself already never throws (every source's own exception is
                    // WARN-skipped inside the pipeline), but this drain arm does not assume today's
                    // one implementation is the only one that will ever back this seam.
                    //
                    // Review fold: SegmentKind is stamped HERE, defensively, on the vended item
                    // itself (the SAME `with` expression BuildPooledStationIdItem uses one arm up) —
                    // never trusted from the vend alone. AdSpotPipeline's own floor
                    // (LibraryAdSpotSource) already stamps it, but IAdSpotVend is a seam a future
                    // second implementation (a plugin-backed source, or a different pipeline) could
                    // back too; an unstamped item reaching this line would silently miss the
                    // render-await loop's own DjName carve-out (`kind is SegmentKind.StationId or
                    // Announcement or Ad`) below — a booth-log/now-playing honesty gap, not a
                    // playability one (SPEC F158.4's own DB-level rotation fence is enforced at the
                    // SQL layer regardless of what this in-memory stamp holds).
                    MediaItem? spot;
                    try
                    {
                        spot = await adSpotVend.GetNextSpotAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw; // Caller cancellation, not a vend fault.
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex, "Ad spot vend threw {ExceptionType}; no ad this break (SPEC F158.3)",
                            ex.GetType().Name);
                        spot = null;
                    }

                    if (spot is null)
                    {
                        // F158.3: a null answer is a normal day, one INFO — never a WARN. An empty
                        // Ads library (or no AddGenWaveAds wiring at all, the NoOpAdSpotVend default)
                        // is not an error.
                        logger.LogInformation(
                            "No ad spot available this break — the ads library is empty or has " +
                            "nothing ready (SPEC F158.3).");
                        break;
                    }

                    KickResolved(BuildAdRequest(identity), spot with { SegmentKind = SegmentKind.Ad });
                    break;
                }

                case SpeechDeferralKind.SignOff:
                case SpeechDeferralKind.SignOn:
                    // SPEC F92.2 (PLAN T124): built from the deferral's OWN captured HandoffContext —
                    // NEVER a fresh ResolvePersonaAsync/accessor read here — see HandoffContext's own
                    // remarks for why (a piece can drain after the wall clock has already flipped past
                    // the boundary, when the accessor would answer with the WRONG persona). A deferral
                    // of this kind is never enqueued without one (EnqueueHandoffCeremonyAsync always
                    // supplies it) — the null-check below is defensive only.
                    if (deferral.Handoff is not { } handoff) break;
                    Kick(BuildHandoffRequest(deferral.Kind, handoff, identity));
                    break;

                case SpeechDeferralKind.TimeDate:
                {
                    // SPEC F110.3 (STORY-302, PLAN T232) — the clock-anchored time announcement:
                    // always the templated rung (TimeDate is not one of LlmCopyWriter.IsLlmAuthored's
                    // kinds, so there is no LLM rung above it to miss). See BuildTimeDateRequest's
                    // own remarks for why the hour comes from the deferral's Due, not StationLocalNow.
                    //
                    // SPEC F141.2 (STORY-355, PLAN T326) — the honesty classification: the SAME
                    // SpeechDeferralQueue.AirTimeLateness formula TryDequeueDue's own budget-expiry
                    // check uses (real now plus already-queued runtime, minus the armed hour; the
                    // shared static, not a re-typed copy — review advisory, connascence of algorithm),
                    // read fresh here rather than reused from drainNow — a straddle/ceremony caller may
                    // have forced drainNow ahead of real time, and a forced-forward "now" must never
                    // make a genuinely punctual TimeDate read as late (the identical reason that queue
                    // method's own remarks give for checking its expiry budget against real wall-clock
                    // time, never the caller's now). Always OnTime or Late here: a deferral this stale
                    // never reaches this arm at all — TryDequeueDue's own expiry check above already
                    // dropped it, unchanged by this feature (SPEC F141.3/F124.4).
                    var timeDateLateness = SpeechDeferralQueue.AirTimeLateness(
                        timeProvider.GetUtcNow(), queuedAhead, deferral.Due);
                    var timeDateFreshness = timeDateLateness > TimeDateHonestyThreshold
                        ? TimeAnnouncementFreshness.Late
                        : TimeAnnouncementFreshness.OnTime;
                    Kick(BuildTimeDateRequest(deferral, identity, timeDateFreshness));
                    break;
                }

                case SpeechDeferralKind.Context:
                {
                    var built = await BuildContextSegmentRequestAsync(deferral, identity, drainNow, ct);
                    if (built is { } b) Kick(b.Request, b.ProviderKey);
                    break;
                }
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
                personaName)
            {
                CrosstalkAiredThisBreak = crosstalkAiredThisBreak,
            };
            Kick(req);
        }

        // Await each render with the budget; skip any that time out, fault, or return null. A
        // handoff-kind (SignOff/SignOn) drop additionally logs a WARN + booth-log entry (SPEC F92.4);
        // a ContextSegment drop logs a WARN only, no booth-log entry (SPEC F107.6, PLAN T224 — the
        // F107 epic has no booth-log-drop event of its own, and this drop is expected, ordinary
        // skip-never-silence operation rather than the handoff ladder's own two-piece degrade); every
        // OTHER kind's drop stays the pre-existing silent skip (StationId today). Classified from the
        // COMPLETED task's own state below, never from which race member <c>Task.WhenAny</c> named
        // the winner (T124 review finding F6): <c>Task.WhenAny</c> completes successfully the moment
        // EITHER task completes, fault or not — it never throws or otherwise signals "the winner
        // faulted," so a ternary keyed on "did renderTask win the race" mislabeled every synth outage
        // that happened to beat the budget delay as "render returned null" instead of "render
        // faulted".
        foreach (var (request, renderTask, contextProviderKey, observeDuration, announcementId) in pendingRenders)
        {
            var kind = request.Kind;
            // The budget delay rides the injected timeProvider (gh-#554) — behavior-identical under
            // TimeProvider.System, but it lets a spec drive this race off a fake clock's due-order
            // instead of wall-clock timer scheduling, which full-suite load contention can skew.
            var winner = await Task.WhenAny(renderTask, Task.Delay(renderBudget, timeProvider, ct));

            if (winner != renderTask)
            {
                if (kind is SegmentKind.SignOff or SegmentKind.SignOn)
                    LogHandoffDrop(kind, "render budget exceeded");
                else if (kind == SegmentKind.ContextSegment)
                    LogContextSegmentDrop(contextProviderKey, "render budget exceeded");
                else if (kind == SegmentKind.Announcement)
                    LogAnnouncementDrop(announcementId, "render budget exceeded");
                continue; // timed out — the still-running render is left unawaited, unchanged behavior
            }

            if (renderTask.IsCompletedSuccessfully && renderTask.Result is { } seg)
            {
                // gh-#253: feed the MEASURED duration (F66.1's cue-derived stamp — null when cue
                // analysis failed, in which case nothing is observed: never fabricated) back into
                // the estimation seam, keyed by the request's own kind/persona/voice — and, as of
                // SPEC F117.2 (PLAN T250 review finding F1), request.ShowName too, so the Exact tier's
                // (voice, show) memo never lets a show-branded StationId render's duration stand in
                // for a DIFFERENT show's (or the plain ident's) airing under the SAME voice. ShowName
                // is null for every non-StationId kind and for a showless/pool-served StationId
                // airing, which the estimator treats as its own (voice, null) bucket — byte-identical
                // to pre-F117 behavior for every one of those. The historical tier self-improves with
                // every segment that actually rendered. observeDuration is false only for a
                // KickResolved pool-first item (SPEC F110.2, PLAN T232) — see pendingRenders' own
                // remarks for why that duration must not join this bucket.
                if (observeDuration && seg.DurationMs is int measuredMs)
                    patterEstimator.ObserveRendered(
                        kind, request.PersonaName, request.Voice, TimeSpan.FromMilliseconds(measuredMs), request.ShowName);

                // gh-#259: a station ID keeps the station's CREDIT (Artist, gh-#96 untouched) but
                // still airs inside the unit's show — stamp the unit persona so Now Playing
                // attribution never flickers to "no DJ" for a few seconds of imaging mid-show.
                // Every other kind already carries its own speaker's name from TtsSegmentSource
                // (SegmentRequest.PersonaName — the handoff kinds' outgoing/incoming included).
                // Announcement (SPEC F144.1, PLAN T341) joins this SAME carve-out for the SAME
                // reason: it has no DJ of its own either (the station voice, or the owner's own
                // requested voice — never a persona identity), yet still airs inside the unit's show.
                // Ad (SPEC F158.2/F158.3, PLAN T397) joins on the identical reasoning one more time:
                // it is imaging, not DJ content (F161.4), yet still airs inside the unit's show.
                if (kind is SegmentKind.StationId or SegmentKind.Announcement or SegmentKind.Ad)
                    seg = seg with { DjName = unitDjName };
                buffer.Enqueue(seg);
            }
            else if (kind is SegmentKind.SignOff or SegmentKind.SignOn)
            {
                LogHandoffDrop(kind, renderTask.IsFaulted ? "render faulted" : "render returned null");
            }
            else if (kind == SegmentKind.ContextSegment)
            {
                LogContextSegmentDrop(contextProviderKey, renderTask.IsFaulted ? "render faulted" : "render returned null");
            }
            else if (kind == SegmentKind.Announcement)
            {
                LogAnnouncementDrop(announcementId, renderTask.IsFaulted ? "render faulted" : "render returned null");
            }
            // else: renderTask completed with a null segment → silently skip (every other kind)
        }
    }

    /// <summary>
    /// The ONE <see cref="SegmentKind.StationId"/> request shape (SPEC F110.2): station's own voice
    /// and credit, never the active persona's — real-radio convention, the ID is the brand speaking,
    /// not the DJ. Deliberately no <see cref="ResolvePersonaAsync"/> here (LeadIn/BackAnnounce stay
    /// persona-voiced), and deliberately not solved by touching <c>Station:Persona:ActiveId</c> — a
    /// future multi-DJ scheduler slots personas in and out, and imaging must stay the station's voice
    /// regardless of who is in the chair. <see cref="SegmentRequest.PersonaName"/> stays null so the
    /// airing credits the station (<c>TtsSegmentSource</c>: <c>Artist = PersonaName ?? StationName</c>).
    ///
    /// <para>
    /// Serves BOTH StationId rungs, not just the templated fallback its pre-F110 name implied: a
    /// direct <c>Kick</c> for the templated TTS ident (the TTS cache key contains the voice, so a
    /// live <c>Station:Voice</c> edit re-keys and re-renders it at its next slot with no regen
    /// tooling), or <c>KickResolved</c>'s classification tag for a pool-first authored item (SPEC
    /// F110.2, PLAN T232) — the render-await loop below reads only this request's own <c>Kind</c>
    /// off a resolved item, so the SAME shape serves as an honest, non-garbage tag rather than
    /// inventing a second one.
    /// </para>
    ///
    /// <para>
    /// SPEC F117.2 (STORY-309, PLAN T250) — the StationId drain arm layers
    /// <c>with { ShowName = show.Name }</c> onto this SAME shape for the templated show-line floor,
    /// never a second request-builder: <see cref="SegmentRequest.ShowName"/> is additive and
    /// <see langword="null"/> here, so every call site that does NOT layer it keeps producing the
    /// original plain-ident request unchanged.
    /// </para>
    /// </summary>
    SegmentRequest BuildStationIdRequest(StationIdentity identity) =>
        new(
            SegmentKind.StationId,
            identity.Voice,
            identity.Name,
            null,
            StationLocalNow(),
            identity.Id,
            PersonaName: null);

    /// <summary>
    /// SPEC F110.2 (STORY-301, PLAN T232) — the pool-first rung: an authored <c>station_id</c> row
    /// airs verbatim, no TTS render involved. <see cref="MediaReferenceExtensions.ToMediaItem"/>'s
    /// own 12-arg mapping never sets <see cref="MediaItem.SegmentKind"/> (its only OTHER production
    /// caller is the music pick, where a null SegmentKind is correct) — a <c>with</c> expression
    /// stamps it honestly here instead of silently leaving the F113 booth-log stamp off an ident that
    /// plainly has one. <see cref="MediaItem.DjName"/> (gh-#259) is deliberately NOT stamped here:
    /// this item is routed through <c>KickResolved</c>, so the render-await loop's own
    /// <c>if (kind == SegmentKind.StationId) seg = seg with { DjName = unitDjName };</c> line stamps
    /// it at the exact same point the templated fallback's own render result would have — one
    /// stamping site for both rungs, never two copies of the same rule.
    /// </summary>
    static MediaItem BuildPooledStationIdItem(MediaReference pooled) =>
        pooled.ToMediaItem() with { SegmentKind = SegmentKind.StationId };

    /// <summary>
    /// SPEC F158.2/F158.3 (STORY-388, PLAN T397) — the ONE <see cref="SegmentKind.Ad"/> request
    /// shape, the <see cref="BuildStationIdRequest"/> precedent one member up: station's own voice
    /// and credit, never the active persona's (an ad is imaging, not DJ content — F161.4's own
    /// "parody spots are visibly station content" ruling). Deliberately no
    /// <see cref="ResolvePersonaAsync"/> call. Every field past <see cref="SegmentRequest.Kind"/>
    /// goes unread by the render-await loop below — this request is routed through
    /// <c>KickResolved</c>, whose loop reads only <c>Kind</c> off a resolved item (the SAME "honest,
    /// non-garbage tag" posture <see cref="BuildStationIdRequest"/>'s own remarks describe for its
    /// pool-first rung).
    /// </summary>
    SegmentRequest BuildAdRequest(StationIdentity identity) =>
        new(
            SegmentKind.Ad,
            identity.Voice,
            identity.Name,
            null,
            StationLocalNow(),
            identity.Id,
            PersonaName: null);

    /// <summary>
    /// Builds a <see cref="SegmentKind.SignOff"/>/<see cref="SegmentKind.SignOn"/> request from the
    /// deferral's own captured <paramref name="handoff"/> (SPEC F92.2, PLAN T124) — see
    /// <see cref="HandoffContext"/>'s own remarks for why this is never a fresh
    /// <see cref="ResolvePersonaAsync"/>/accessor read. <see cref="HandoffContext.CrossingTrackTitle"/>/
    /// <see cref="HandoffContext.CrossingTrackArtist"/> (SPEC F111.3, PLAN T235) ride straight across
    /// onto <see cref="SegmentRequest.CrossingTrackTitle"/>/<see cref="SegmentRequest.CrossingTrackArtist"/>
    /// — null for every non-straddle piece, exactly like every other optional field here.
    /// <see cref="HandoffContext.ShowName"/>/<see cref="HandoffContext.ShowFlavor"/>/
    /// <see cref="HandoffContext.CounterpartShowName"/> (SPEC F116.2, PLAN T248) ride across the same
    /// way, onto <see cref="SegmentRequest.ShowName"/>/<see cref="SegmentRequest.ShowFlavor"/>/
    /// <see cref="SegmentRequest.CounterpartShowName"/>.
    /// </summary>
    SegmentRequest BuildHandoffRequest(SpeechDeferralKind kind, HandoffContext handoff, StationIdentity identity)
    {
        var handoffKind = kind == SpeechDeferralKind.SignOff ? SegmentKind.SignOff : SegmentKind.SignOn;
        return new SegmentRequest(
            handoffKind,
            handoff.Voice,
            identity.Name,
            null,
            StationLocalNow(),
            identity.Id,
            handoff.PersonaName,
            handoff.CounterpartName,
            CrossingTrackTitle: handoff.CrossingTrackTitle,
            CrossingTrackArtist: handoff.CrossingTrackArtist,
            ShowName: handoff.ShowName,
            ShowFlavor: handoff.ShowFlavor,
            CounterpartShowName: handoff.CounterpartShowName);
    }

    /// <summary>
    /// SPEC F111.3 (PLAN T235) — captures the crossing track's title/artist into the HELD SignOn's own
    /// <see cref="HandoffContext"/> at straddle plan time (the F92.2 immutable-capture pattern this
    /// whole record already follows — see its own remarks — extended to a fact that is not knowable
    /// until the very unit that straddles it). A no-op when no SignOn is pending: <see cref="GetNextAsync"/>'s
    /// straddle branch calls this only for a crossing SignOff-headed straddle whose reconciliation left
    /// the ceremony unchanged (see that branch's own remarks), including the one-sided F92.3 "into
    /// music-only" shape, which has nothing to enrich.
    ///
    /// <para>
    /// <b>Peek then Enqueue — two separate lock acquisitions, not one atomic operation</b> (T235 review
    /// finding F6, corrects an earlier version of this comment that called it "the SAME atomic
    /// supersede-by-(kind, discriminator) path"; it re-uses that supersede's KEY, never its atomicity —
    /// <see cref="SpeechDeferralQueue.Peek"/> and <see cref="SpeechDeferralQueue.Enqueue"/> each take
    /// and release the queue's lock independently). This read-then-write is safe here ONLY because the
    /// SignOn slot has exactly one OTHER writer — <see cref="EnqueueHandoffCeremonyAsync"/> — and both
    /// it and this method run on the SAME feeder thread, one <see cref="GetNextAsync"/> pull at a time,
    /// never concurrently with each other or with this method. Nothing inside
    /// <see cref="SpeechDeferralQueue"/> itself enforces that; it is an Orchestrator-side invariant
    /// (single-writer-thread), not a queue-side guarantee.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="SpeechDeferral.NotBefore"/> rides along unchanged (round-2 review finding F6).</b>
    /// This re-Enqueue is the SAME supersede-by-key path every other caller uses — an omitted
    /// <c>notBefore:</c> argument defaults to <see langword="null"/> and would silently drop any gate
    /// already sitting on the peeked <paramref name="signOn"/>. Not reachable with a non-null gate
    /// TODAY (<see cref="GetNextAsync"/>'s own straddle branch calls this BEFORE
    /// <see cref="HoldSignOnPastQueuedTail"/> ever runs on this same slot — the two straddle/decline
    /// paths never interleave on one SignOn), but passing it through costs nothing and removes a
    /// landmine for whichever future call ordering changes that invariant.
    /// </para>
    /// </summary>
    void CaptureCrossingTrackForHeldSignOn(MediaItem crossingTrack)
    {
        if (deferralQueue.Peek(SpeechDeferralKind.SignOn) is not { } signOn || signOn.Handoff is not { } handoff)
            return; // one-sided straddle (SignOff only, F92.3) — no SignOn to enrich

        deferralQueue.Enqueue(
            SpeechDeferralKind.SignOn,
            signOn.Reason,
            signOn.Due,
            handoff with { CrossingTrackTitle = crossingTrack.Title, CrossingTrackArtist = crossingTrack.Artist },
            notBefore: signOn.NotBefore);
    }

    /// <summary>
    /// SPEC F124.1/F124.2 — arms a held SignOn's <see cref="SpeechDeferral.NotBefore"/> gate at
    /// <c>max(existing NotBefore, now + queuedAhead)</c>: <see cref="HoldSignOnAtStraddle"/> only ever
    /// excludes a deferral from a SINGLE <see cref="SpeechDeferralQueue.TryDequeueDue"/> call, which a
    /// queued tail spanning several units (SPEC F124.6's own watch item — 230s queued at T-64s, several
    /// tracks' worth) outruns — without a gate that survives past that one call, the SAME SignOn would
    /// simply become the peeked fit on the very next pull and drain right back out through the
    /// ORDINARY (unforced) due comparison, the hold having bought it nothing (round-1 review finding
    /// F1: this is exactly what re-stamping <see cref="SpeechDeferral.Due"/> instead of arming a
    /// separate gate failed to prevent — a SignOn-headed fit's own <c>UntilBoundary</c> IS <c>Due -
    /// now</c>, so ANY drain instant this method's own caller computes for that later pull already
    /// reaches at least <c>Due</c>, hold-set or not).
    ///
    /// <para>
    /// <b><see cref="SpeechDeferral.Due"/> is deliberately left untouched.</b> It keeps meaning "the
    /// boundary this deferral belongs to" — <see cref="EnqueueHandoffCeremonyAsync"/>'s own
    /// reconcile/window-exit logic (SPEC F124's round-1 review finding F2) reads it as exactly that,
    /// and a re-stamped Due would have made a live-held SignOn indistinguishable from one whose own
    /// boundary genuinely moved. <see cref="SpeechDeferral.NotBefore"/> alone carries "not before REAL
    /// wall-clock time reaches this instant" — see <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s
    /// own remarks for why that must never be satisfiable by a forced-forward "as of" instant the way
    /// <c>Due</c> legitimately is.
    /// </para>
    ///
    /// <para>
    /// <paramref name="queuedAhead"/> is an HONEST FLOOR (SPEC F124.2 — the feeder's own measurement at
    /// THIS pass, deliberately not a forecast of anything a later unit might still add), and drains
    /// only ever happen at a seam, so the SignOn lands at the first seam AT-OR-AFTER the estimate
    /// rather than exactly on it. <c>max</c>, never a blind overwrite: a gate already later than
    /// <c>now + queuedAhead</c> is left exactly where it was, the same "never move a pending deferral
    /// earlier" posture <see cref="EnqueueIfAbsent"/> already keeps for its own callers.
    /// </para>
    ///
    /// <para>
    /// Peek then Enqueue — the SAME two-lock-acquisition pattern
    /// <see cref="CaptureCrossingTrackForHeldSignOn"/> documents (T235 review finding F6), safe here
    /// for the identical reason: the SignOn slot's only OTHER writer is
    /// <see cref="EnqueueHandoffCeremonyAsync"/>, and both run on the single feeder thread this class's
    /// whole straddle/decline machinery assumes (see that method's own remarks). A no-op when nothing
    /// is pending — a one-sided SignOff-only decline (SPEC F92.3) has no SignOn to hold.
    /// </para>
    ///
    /// <para>
    /// <b>The GATE is clamped to <see cref="IBoundaryBiasProvider.Current"/>, not just
    /// <see cref="TryServeCeremonyOnlyUnitAsync"/>'s own drain instant (SPEC F124.1, round-2 review
    /// finding F5).</b> <paramref name="queuedAhead"/> arrives here as the feeder's RAW measurement —
    /// <see cref="TryServeCeremonyOnlyUnitAsync"/>'s own <c>chasedQueuedAhead</c> local clamps a
    /// SEPARATE copy for that method's own drain-instant arithmetic, but hands this method the
    /// unclamped <c>fit.QueuedAhead</c> (SPEC F124.6's own watch item: <c>pendingAirQueue</c> can
    /// legally hold HOURS under a genuine backlog). Clamping only the drain instant and not this gate
    /// would let a multi-hour queued tail arm a NotBefore floor arbitrarily far past the SAME F74.3
    /// lookahead window every fit in this class is built inside of — a hold that could never be
    /// reasoned about by the very machinery (<see cref="PeekNextDue"/>'s window check, one call up in
    /// <see cref="GetNextAsync"/>) that decides whether a boundary is even "coming soon" at all. Capping
    /// at the window bound, exactly like <c>chasedQueuedAhead</c>'s own clamp one call site over, keeps
    /// this gate honest about how far forward THIS pass is willing to reason, never a promise about
    /// when the tail will ACTUALLY finish draining hours from now — a later straddle/decline unit, once
    /// the SignOn is peeked again as the earliest UN-GATED entry, re-evaluates and re-arms exactly as
    /// today's un-clamped estimate always could, just one hold-call at a time instead of one giant leap.
    /// </para>
    /// </summary>
    void HoldSignOnPastQueuedTail(TimeSpan queuedAhead)
    {
        if (deferralQueue.Peek(SpeechDeferralKind.SignOn) is not { } signOn)
            return; // one-sided decline (SignOff only, F92.3) — no SignOn to hold

        var clampedQueuedAhead = queuedAhead < boundaryBiasProvider.Current ? queuedAhead : boundaryBiasProvider.Current;
        var estimatedDrain = timeProvider.GetUtcNow() + clampedQueuedAhead;
        var notBefore = signOn.NotBefore is { } existing && existing > estimatedDrain ? existing : estimatedDrain;

        deferralQueue.Enqueue(
            SpeechDeferralKind.SignOn, signOn.Reason, signOn.Due, signOn.Handoff, notBefore: notBefore);
    }

    /// <summary>
    /// SPEC F110.3 (STORY-302, PLAN T232) — the clock-anchored time announcement: templated,
    /// station-voiced (<see cref="SegmentRequest.PersonaName"/> stays null, the StationId imaging
    /// precedent), zero LLM (<c>TimeDate</c> is not one of <c>LlmCopyWriter.IsLlmAuthored</c>'s
    /// kinds, so this always reaches <see cref="PatterTemplateRenderer"/> — there is no LLM rung
    /// above it to miss). <see cref="SegmentRequest.LocalNow"/> carries <paramref name="deferral"/>'s
    /// own <see cref="SpeechDeferral.Due"/> — the station-local top of the hour this announcement was
    /// ARMED for — deliberately NOT <see cref="StationLocalNow"/>'s drain-time read: a drain landing
    /// at 14:02 must still speak "two o'clock" (the 14:00 top), not whatever hour the wall clock
    /// happens to read once the boundary is finally reached. <c>Due</c> is already a real,
    /// correctly-offset instant (<see cref="WallClockInstantResolver.Resolve"/>, via
    /// <c>ClockAnchoredImagingProducer</c>), so formatting it directly names the right station-local
    /// hour with no second zone conversion here. This is also what makes SPEC F110.3's cache-hit
    /// acceptance true: the SAME hour always renders the SAME text
    /// (<see cref="PatterTemplateRenderer.Expand"/> reads only the hour component), so a second
    /// drain within that hour hashes identically and hits the forever-cache
    /// (<c>TtsSegmentSource</c>'s <c>FreshPerAiring=false</c> path) rather than re-synthesizing — the
    /// SAME cache-hit reasoning holds for the late variant (SPEC F141.2) too, since the rendered TEXT
    /// (not <paramref name="freshness"/> itself) is the cache key.
    /// </summary>
    /// <param name="freshness">
    /// SPEC F141.2 (STORY-355, PLAN T326) — the caller's own honesty classification for this drain,
    /// stamped verbatim onto <see cref="SegmentRequest.TimeDateFreshness"/>; never re-derived here.
    /// </param>
    static SegmentRequest BuildTimeDateRequest(
        SpeechDeferral deferral, StationIdentity identity, TimeAnnouncementFreshness freshness) =>
        new(
            SegmentKind.TimeDate,
            identity.Voice,
            identity.Name,
            null,
            deferral.Due,
            identity.Id,
            PersonaName: null)
        {
            TimeDateFreshness = freshness,
        };

    /// <summary>
    /// Builds the <see cref="SegmentKind.ContextSegment"/> request for a due
    /// <see cref="SpeechDeferralKind.Context"/> <paramref name="deferral"/> (SPEC
    /// F107.3/F107.6/F107.7, STORY-297, PLAN T224), or <see langword="null"/> when the drain-time
    /// re-check finds nothing to build from — no captured content, a stale
    /// <see cref="ContextSegmentFacts.FreshUntil"/>, or blank <see cref="ContextSegmentFacts.SegmentFacts"/> —
    /// each logged at Information, naming the provider and cause (T224 review finding), never echoing
    /// the provider's own facts (F108.3).
    /// </summary>
    async Task<(SegmentRequest Request, string ProviderKey)?> BuildContextSegmentRequestAsync(
        SpeechDeferral deferral, StationIdentity identity, DateTimeOffset drainNow, CancellationToken ct)
    {
        // SPEC F107.3/F107.6 (STORY-297, PLAN T224): re-verify freshness HERE, at drain time — the
        // payload was captured by the T226 ticker at ENQUEUE time (see SpeechDeferral.Context's own
        // remarks), and the boundary this drain actually fires at can land well after that.
        // Discriminator is always the originating provider's own Key for this kind (SpeechDeferral's
        // own doc) — "(unknown)" is a defensive fallback only, never expected to appear in production.
        var providerKey = deferral.Discriminator ?? "(unknown)";

        if (deferral.Context is not { } content)
        {
            logger.LogInformation(
                "Context segment for provider {ProviderKey} skipped at drain time: no " +
                "content captured (SPEC F107.6).", providerKey);
            return null;
        }

        if (content.FreshUntil <= drainNow)
        {
            logger.LogInformation(
                "Context segment for provider {ProviderKey} skipped at drain time: stale " +
                "(past FreshUntil) — music continues (SPEC F107.6).", providerKey);
            return null;
        }

        // T222 ruling (F125.2 keeps it honest): a blank SegmentFacts should never actually reach
        // here — ContextPipeline only ever constructs a ContextSegmentFacts from a non-empty window
        // join — but the type itself guarantees nothing at construction time, so this stays a
        // defense-in-depth guard rather than an assumed invariant.
        if (string.IsNullOrWhiteSpace(content.SegmentFacts))
        {
            logger.LogInformation(
                "Context segment for provider {ProviderKey} skipped at drain time: no " +
                "segment facts (SPEC F107.6).", providerKey);
            return null;
        }

        // SPEC F107.7 — Context:{Key}:PersonaId picks the voice; see this class's own remarks and
        // ResolveContextSegmentVoiceAsync for the full resolution table.
        var contextProviderSettings = contextSettings.For(providerKey);
        var (contextVoice, contextPersonaName) = await ResolveContextSegmentVoiceAsync(
            contextProviderSettings.PersonaId, providerKey, identity.Voice, ct);
        var contextReq = new SegmentRequest(
            SegmentKind.ContextSegment,
            contextVoice,
            identity.Name,
            null,
            StationLocalNow(),
            identity.Id,
            PersonaName: contextPersonaName,
            ContextFacts: content.SegmentFacts);
        return (contextReq, providerKey);
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
    /// <b>Arm once per key, never every unit (T124 review finding F2 — the double-sign-off bug this
    /// fixes):</b> this producer runs on EVERY unit while a boundary sits in-window, but it only ever
    /// ACTS the first time it sees a given <c>(BoundaryAt, outgoing persona id, incoming persona id,
    /// outgoing show id, incoming show id)</c> key (the show-id pair widened this at F116.2/PLAN T248
    /// — see <see cref="lastArmedHandoff"/>'s own remarks) — <see cref="lastArmedHandoff"/> remembers
    /// the last one it armed or cleared for, and an unchanged key returns immediately, touching
    /// neither <paramref name="deferralQueue"/> nor <paramref name="personaStore"/> again. Without
    /// this, re-running the OLD unconditional enqueue-every-unit logic on a seam landing in
    /// <c>[BoundaryAt - SignOffLeadTime, BoundaryAt)</c> would: drain SignOff at this unit (its due
    /// has arrived) — see it drain, then IMMEDIATELY re-<see cref="SpeechDeferralQueue.Enqueue"/> a
    /// FRESH SignOff for the very same boundary with a due time that is now itself already in the
    /// past (the resolver's "current" segment has not yet flipped, so <c>BoundaryAt</c>/the persona
    /// ids still read identically) — which the NEXT unit's drain would fire AGAIN, a second sign-off
    /// airing for one boundary. The two elapsed-due guards below (skip arming SignOff once
    /// <c>BoundaryAt - SignOffLeadTime &lt;= now</c>; skip arming SignOn once <c>BoundaryAt &lt;=
    /// now</c>) are the belt to this key-check's suspenders: a piece is never handed to
    /// <see cref="SpeechDeferralQueue.Enqueue"/> with a due time that has already elapsed, full stop,
    /// even on the very first unit a key is ever seen.
    /// </para>
    ///
    /// <para>
    /// A CHANGED key — the common case is a schedule write moving the boundary or reassigning a
    /// show, or the resolver's own "current" segment finally flipping to the incoming one once
    /// <c>now</c> passes the old boundary — re-arms fresh: <see cref="SpeechDeferralQueue.Enqueue"/>'s
    /// own supersede-by-kind (SPEC F74.2) discards whatever the OLD key left pending of the same
    /// kind, and this method's own <c>ClearCeremony</c> local retracts anything the old key armed
    /// that the new one has no replacement for (window exit, gap-to-gap, self-handoff — see the
    /// dedupe list below). Nothing here is left to expire on its own.
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
    /// <b>Dedupe (SPEC F92.3, the T119-review build clarification; amended by F114.3/F116.2, PLAN
    /// T248):</b> the resolver's own <c>BoundaryAt</c>/<c>NextSegment</c> stay row-accurate even
    /// across a same-persona adjacency — THIS method is where "no ceremony airs" (or "airs as a
    /// one-piece transition") for that case is decided, never the resolver. Six shapes, by
    /// outgoing/incoming persona id and — for the equal-persona case only — show id (compared, never
    /// the display <c>Name</c>, per <c>ScheduleSegment.ShowId</c>'s own "write-authoritative identity
    /// field" ruling — a show rename can never look like a show change this way):
    /// <list type="bullet">
    /// <item>both persona ids null (a genuine gap, or a gap followed by an explicit
    /// persona-less/music-only scheduled segment) — gap-to-gap: nothing airs.</item>
    /// <item>persona ids equal and non-null, AND show ids equal (both named the SAME show, or both
    /// showless — the F91.6 seeded grid's own midnight roll is the showless instance) — self-handoff:
    /// nothing airs (F92.3 as amended by F114.3).</item>
    /// <item>persona ids equal and non-null, but show ids DIFFER (F114.3/F116.2) — a real boundary for
    /// ceremony purposes, but exactly ONE piece airs: the incoming sign-on, styled as a transition
    /// (the F92.4 incoming-welcome rung as designed behavior here, not a degrade) — no SignOff at all
    /// (there is no OTHER persona to hand off to), <see cref="HandoffContext.CounterpartName"/> null,
    /// <see cref="HandoffContext.ShowName"/>/<see cref="HandoffContext.ShowFlavor"/> the incoming
    /// show's own name/flavor.</item>
    /// <item>outgoing non-null, incoming null — SignOff only, <see cref="HandoffContext.CounterpartName"/>
    /// null ("the music keeps rolling"); <see cref="HandoffContext.ShowName"/> the ending show, if
    /// any (F114.3 — sign-off may still name the show it is closing out).</item>
    /// <item>outgoing null, incoming non-null — SignOn only, <see cref="HandoffContext.CounterpartName"/>
    /// null ("no predecessor"); <see cref="HandoffContext.ShowName"/>/<see cref="HandoffContext.ShowFlavor"/>
    /// the incoming show, if any.</item>
    /// <item>both non-null and different persona — both pieces, each naming the OTHER persona
    /// (<see cref="HandoffContext.CounterpartName"/>); F116.2's show-awareness rides EVERY shape ABOVE
    /// this one too, always via <see cref="OnAirSnapshot.Show"/>/<see cref="OnAirSnapshot.NextSegment"/>'s
    /// own <c>Show</c> (SPEC F116.1's chokepoint — never re-derived, never re-queried), never gated on
    /// whether a show happens to be assigned: an unnamed block simply carries null show fields
    /// straight through, so a showless station's ceremony stays byte-identical to pre-F116 (SPEC
    /// F116.1's own test).</item>
    /// </list>
    /// <see cref="HandoffContext.ShowFlavor"/> is captured on the SIGN-ON half only (F116.2 names
    /// flavor for the sign-on prompt alone); <see cref="HandoffContext.CounterpartShowName"/> is
    /// captured on the SIGN-OFF half only (F114.3's "may name the ending show and the next" — the
    /// "next" is the counterpart's show). Both stay prompt-only forever (SPEC F115.3) — this method
    /// never logs either.
    /// </para>
    ///
    /// <para>
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
        //
        // ClearStale, never the blind Clear (round-1 review finding F2): every call site below is this
        // producer concluding "no ceremony belongs here any more" off the SCHEDULE's current state,
        // which says nothing about whether a queue-crossing SignOff already handed its paired SignOn to
        // this class's own HoldSignOnPastQueuedTail (SPEC F124.1) and is still waiting on that tail to
        // drain. A held-but-not-yet-airable SignOn (NotBefore in the future) is LIVE, not stale —
        // this producer re-evaluates every unit (by design, a schedule write must be noticed promptly),
        // so the window-exit branch below fires the very first unit real wall-clock time crosses the
        // boundary, typically well before a multi-minute queued tail has actually finished airing;
        // wiping the hold there destroyed the sign-on outright (round-1's reproduced F2 defect: it
        // survives past the boundary, then this exact call silently erases it, and the incoming DJ
        // never signs on). SignOff is never held (only a SignOn ever gets a NotBefore), so this is a
        // no-op difference for every SignOff clear below — always "stale," exactly what Clear already
        // removed.
        void ClearCeremony()
        {
            deferralQueue.ClearStale(SpeechDeferralKind.SignOff);
            deferralQueue.ClearStale(SpeechDeferralKind.SignOn);
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

        // SPEC F114.3/F116.2 (PLAN T248): ShowId is the write-authoritative identity field
        // (ScheduleSegment's own remarks) — compared here, never the display Name, so a rename can
        // never look like a show change. onAir.Segment is the SAME row onAir.PersonaId was read off;
        // onAir.NextSegment is the resolver's own next-boundary row (SPEC F116.1's chokepoint).
        var outgoingShowId = onAir.Segment?.ShowId;
        var incomingShowId = onAir.NextSegment?.ShowId;
        var tuple = (boundaryAt.Value, outgoingId, incomingId, outgoingShowId, incomingShowId);

        // Arm-once (T124 review finding F2): this exact tuple was already armed/cleared by a prior
        // unit — nothing has changed, so touch neither the queue nor personaStore again.
        if (lastArmedHandoff == tuple) return;
        lastArmedHandoff = tuple;

        if (outgoingId is null && incomingId is null)
        {
            ClearCeremony(); // gap-to-gap
            return;
        }

        if (outgoingId is not null && outgoingId == incomingId)
        {
            if (outgoingShowId == incomingShowId)
            {
                // F92.3 as amended by F114.3: same persona AND same show (or both showless) on a
                // row-accurate boundary airs no ceremony at all — never even attempted, so this never
                // shows up as a "drop" either.
                ClearCeremony(); // self-handoff
                return;
            }

            // F116.2: same persona, DIFFERENT show — a real boundary for ceremony purposes, but airs
            // exactly ONE piece: the incoming sign-on, styled as a transition (the F92.4
            // incoming-welcome rung as designed behavior here, not a degrade). There is no OTHER
            // persona to hand off to, so no SignOff is ever enqueued for this shape.
            deferralQueue.Clear(SpeechDeferralKind.SignOff);

            var transitionPersona = await ResolveHandoffPersonaAsync(incomingId, stationVoice, ct);
            if (transitionPersona is null || boundaryAt.Value <= now)
            {
                // Round-2 review finding F3: ClearStale, never the blind Clear — the SAME reasoning
                // ClearCeremony's own remarks give (a live-held SignOn's NotBefore gate has not opened
                // against REAL wall-clock time, so it is not stale merely because THIS producer has
                // concluded nothing new belongs in this slot).
                deferralQueue.ClearStale(SpeechDeferralKind.SignOn);
            }
            else
            {
                // Round-2 review finding F4: a live hold on the SignOn slot must survive being
                // overwritten here — see this branch's sibling below (the two-DJ handoff re-arm) for
                // the full ruling; both call sites share the identical carry-forward.
                var heldNotBefore = deferralQueue.Peek(SpeechDeferralKind.SignOn)?.NotBefore;
                deferralQueue.Enqueue(
                    SpeechDeferralKind.SignOn,
                    "handoff: same-persona show transition (SPEC F116.2)",
                    boundaryAt.Value,
                    new HandoffContext(
                        transitionPersona.Value.Voice,
                        transitionPersona.Value.Name,
                        CounterpartName: null, // no OTHER DJ to name — it is the same persona
                        ShowName: onAir.NextSegment?.Show?.Name,
                        ShowFlavor: onAir.NextSegment?.Show?.Flavor),
                    notBefore: heldNotBefore);
            }

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
                new HandoffContext(
                    outgoing.Value.Voice, outgoing.Value.Name, incoming?.Name,
                    ShowName: onAir.Show?.Name,
                    CounterpartShowName: onAir.NextSegment?.Show?.Name));
        }

        if (incoming is null || boundaryAt.Value <= now)
        {
            // Round-2 review finding F3: ClearStale, never the blind Clear — identical reasoning to
            // ClearCeremony's own remarks and this method's same-persona-transition branch above.
            deferralQueue.ClearStale(SpeechDeferralKind.SignOn);
        }
        else
        {
            // Round-2 review finding F4: a live hold on the CURRENT SignOn slot —
            // HoldSignOnPastQueuedTail's own NotBefore gate, still waiting on a queued tail to drain —
            // must SURVIVE being overwritten by a re-arm for what this evaluation just decided is a
            // genuinely different boundary/persona pair (a schedule write, or simply the resolver's own
            // "current" segment rolling forward once real wall-clock time finally reaches the OLD
            // boundary while the hold is still open). Chosen over the alternative (refuse to re-arm at
            // all while a hold is live): refusing would leave the STALE, now-WRONG persona/boundary
            // content sitting in this slot until the old hold finally opens — audibly worse than airing
            // the CORRECT content a few seconds later than the bare gate alone would allow, since the
            // gate we DO carry forward still enforces "not before the already-queued tail finishes,"
            // exactly the same physical constraint that queued tail always represented, regardless of
            // which ceremony's content ends up occupying this slot. Peek-then-Enqueue is racy only in
            // theory here — same single-feeder-thread invariant <see cref="HoldSignOnPastQueuedTail"/>
            // and <see cref="CaptureCrossingTrackForHeldSignOn"/> already document.
            //
            // This never disturbs the F2 reconcile guard's own Due-equality semantics
            // (<see cref="GetNextAsync"/>'s straddle branch, "reconciledSignOff.Due == pending.Due"):
            // that check reads the SignOff half only, which this branch's SignOff arm (above) still
            // stamps with the fresh, correctly-computed Due for whatever boundary this evaluation
            // resolved — carrying NotBefore forward on the SignOn half changes nothing about what Due
            // that guard ever sees.
            var heldNotBefore = deferralQueue.Peek(SpeechDeferralKind.SignOn)?.NotBefore;
            deferralQueue.Enqueue(
                SpeechDeferralKind.SignOn,
                "handoff: boundary entered the F74.3 window",
                boundaryAt.Value,
                new HandoffContext(
                    incoming.Value.Voice, incoming.Value.Name, outgoing?.Name,
                    ShowName: onAir.NextSegment?.Show?.Name,
                    ShowFlavor: onAir.NextSegment?.Show?.Flavor),
                notBefore: heldNotBefore);
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
    /// SPEC F124.4 (PLAN T269) — the callback <see cref="EnqueuePatterAsync"/> wires into
    /// <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s own <c>onExpired</c> parameter (closing over
    /// the SAME <c>timeDateStaleBudget</c> that call already threaded in — see that local's own
    /// remarks): the ONE WARN a dropped, elapsed-due <see cref="SpeechDeferralKind.TimeDate"/> deferral
    /// gets, naming the armed hour and the lateness PAST IT, distinctly from the budget it exceeded
    /// (round-3 review finding F2 — the original wording mislabeled the lateness figure as "past the
    /// budget", when it is actually the full lateness past <c>Due</c>; the budget is a SEPARATE
    /// number, now stated separately rather than conflated with it). <paramref name="deferral"/>'s own
    /// <see cref="SpeechDeferral.Due"/> is the armed hour — the SAME station-local top-of-hour instant
    /// <see cref="BuildTimeDateRequest"/> would have spoken had this drain landed in time (see that
    /// method's own remarks for why <c>Due</c>, never a drain-time clock read, names the hour).
    /// <paramref name="lateness"/> is the air-time figure <see cref="SpeechDeferralQueue.TryDequeueDue"/>
    /// already computed (<c>realNow + queuedAhead - Due</c>, never the naive wall-clock-only
    /// difference); <paramref name="budget"/> is the live value that lateness was judged against. No
    /// <see cref="events"/> publish and no music impact either way — this drop never blocks the next
    /// hour's <see cref="SpeechDeferralQueue.EnqueueIfAbsent"/> re-arm (the T230-F1 keep-alive), it
    /// simply speaks nothing this hour instead of an invented one (F124.4's own "idents are exempt, a
    /// late time check is not" ruling — this callback only ever fires for
    /// <see cref="SpeechDeferralKind.TimeDate"/>, by <see cref="SpeechDeferralQueue.TryDequeueDue"/>'s
    /// own kind-scoped contract).
    /// </summary>
    void LogTimeDateExpiry(SpeechDeferral deferral, TimeSpan lateness, TimeSpan budget) =>
        logger.LogWarning(
            "TimeDate deferral armed for {ArmedHour:HH:mm} dropped undrained — {LatenessSeconds:F0}s " +
            "past its armed hour (budget {BudgetSeconds:F0}s); a late time check would invent the hour.",
            deferral.Due, lateness.TotalSeconds, budget.TotalSeconds);

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
    /// SPEC F144.1 (STORY-358, PLAN T341, review finding F1) — fault-isolates the claim itself: an
    /// <see cref="IAnnouncementSource.ClaimDeliverableAsync"/> fault (a Host-side decorator's own DB
    /// round trip, or the SpectatorMode guard's <c>IOptionsMonitor</c> read, either of which can throw)
    /// must never cost the whole unit — the exact SAME "genuinely unreachable degrades, never faults
    /// unit assembly" shape <see cref="ResolveAnnouncementVoiceAsync"/> immediately below already
    /// carries for the voice registry (SPEC F12.4's standing defensiveness). Degrades to an empty
    /// claim — indistinguishable from "nothing deliverable" or a SPEC F145.2 refusal, which is already
    /// the seam's own documented shape (<see cref="IAnnouncementSource.ClaimDeliverableAsync"/>'s own
    /// remarks) — so this unit still assembles its music/back-announce/lead-in normally; only the
    /// announcement step itself goes dark for this one pull, and the next unit's own claim retries.
    /// </summary>
    async Task<IReadOnlyList<AnnouncementItem>> ClaimAnnouncementsAsync(IAnnouncementSource source, CancellationToken ct)
    {
        try
        {
            return await source.ClaimDeliverableAsync(AnnouncementVendCap, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Announcement claim failed — treating as an empty claim this unit; music is unaffected (SPEC F12.4).");
            return [];
        }
    }

    /// <summary>
    /// SPEC F144.2 (STORY-358, PLAN T341) — resolves the voice an owner announcement airs with:
    /// <paramref name="requestedVoice"/> (SPEC F143.1's own <c>voice</c> field — UNTRUSTED free text,
    /// already length-bounded upstream but never otherwise validated) when it names a voice the TTS
    /// backend's own <see cref="voiceLister"/> currently reports installed, else
    /// <paramref name="stationVoice"/> — the station's own default, never an error either way. This
    /// is the ONLY place <see cref="AnnouncementItem.RequestedVoice"/> is ever compared against
    /// anything: an unknown/invalid value is simply not equal to any entry in the list, so no
    /// separate "is this shaped like a voice id" check exists, and the raw string is NEVER
    /// interpolated into a path or any other structural position anywhere downstream — accepted
    /// verbatim as <see cref="SegmentRequest.Voice"/> only once it has matched a KNOWN id, at which
    /// point it is no longer meaningfully "untrusted" free text.
    ///
    /// <para>
    /// A null <paramref name="requestedVoice"/> (the "station's own default" submission, F143.1) skips
    /// the registry entirely — no network call for the common case. A null <see cref="voiceLister"/>
    /// (no Host wiring) or the registry itself faulting (a live network call —
    /// <see cref="ITtsVoiceLister.ListVoicesAsync"/>'s own contract, unlike every OTHER per-unit
    /// accessor this Orchestrator reads) both degrade to the station voice, logged once per
    /// occurrence: an unreachable voice backend must never cost the announcement its air time, and
    /// must never surface as an unhandled fault out of unit assembly (SPEC F12.4's standing
    /// defensiveness, the SAME posture <see cref="ResolvePersonaAsync"/> immediately above carries).
    /// </para>
    /// </summary>
    async Task<string> ResolveAnnouncementVoiceAsync(string? requestedVoice, string stationVoice, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(requestedVoice) || voiceLister is null)
            return stationVoice;

        try
        {
            var known = await voiceLister.ListVoicesAsync(ct);
            return known.Contains(requestedVoice, StringComparer.Ordinal) ? requestedVoice : stationVoice;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Announcement voice registry unreachable — falling back to the station voice (SPEC F144.2)");
            return stationVoice;
        }
    }

    /// <summary>
    /// SPEC F144.3/F144.4 (STORY-358, PLAN T342) — attempts the flavored render through
    /// <see cref="announcementCopyWriter"/>, fault-isolating it the SAME way
    /// <see cref="ResolveAnnouncementVoiceAsync"/>/<see cref="ClaimAnnouncementsAsync"/> immediately
    /// above already do (SPEC F12.4): a null seam (no Host wiring — the crosstalkPlanner precedent,
    /// feature dark) or any exception the writer's own never-throws contract still lets slip both
    /// degrade to <see langword="null"/>, never a faulted unit.
    /// <see cref="IAnnouncementCopyWriter.WriteAnnouncementAsync"/> itself already resolves EVERY
    /// F144.3/F144.4 failure mode (a disabled/unreachable LLM, a blown render budget, an exhausted
    /// re-ask ladder on either a fabrication or the F144.3 containment check) to
    /// <see langword="null"/> internally — this wrapper's own catch exists purely as the SAME
    /// belt-and-suspenders defense every other external seam call in this class already carries, not
    /// because that contract is expected to be broken.
    /// </summary>
    async Task<string?> ResolveFlavoredAnnouncementCopyAsync(
        SegmentRequest announcementRequest, string message, CancellationToken ct)
    {
        if (announcementCopyWriter is not { } writer)
            return null;

        try
        {
            return await writer.WriteAnnouncementAsync(announcementRequest, message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Announcement flavor render faulted — falling back to the verbatim read (SPEC F144.4)");
            return null;
        }
    }

    /// <summary>
    /// SPEC F107.7 (STORY-297, PLAN T224) — resolves the voice/name a <see cref="SegmentKind.ContextSegment"/>
    /// airs with, from <c>Context:{Key}:PersonaId</c> (<paramref name="configuredPersonaId"/>, the
    /// drain arm's own <see cref="contextSettings"/> read). A positive value names an EXPLICIT
    /// persona, resolved through <see cref="ResolveContextPersonaAsync"/> and degrading to the
    /// station voice on any miss (unresolvable id, no <paramref name="personaStore"/> wired, or a
    /// store fault — F12.4). Zero, negative, or unset (an unconfigured provider binds the same as
    /// zero) means the on-air DJ — delegated straight to <see cref="ResolvePersonaAsync"/>, whose own
    /// no-active-persona fallback already IS "music-only segment or gap ⇒ station voice, PersonaName
    /// null" (the StationId imaging precedent), so that half of F107.7 needs no code of its own here.
    /// </summary>
    async Task<(string Voice, string? PersonaName)> ResolveContextSegmentVoiceAsync(
        long? configuredPersonaId, string providerKey, string stationVoice, CancellationToken ct)
    {
        if (configuredPersonaId is { } explicitPersonaId && explicitPersonaId > 0)
        {
            var resolved = await ResolveContextPersonaAsync(explicitPersonaId, providerKey, stationVoice, ct);
            if (resolved is { } r) return (r.Voice, r.Name);

            return (stationVoice, null); // unresolvable explicit persona id — station voice, never a stall
        }

        return await ResolvePersonaAsync(stationVoice, ct);
    }

    /// <summary>
    /// Resolves an EXPLICITLY configured context-provider persona (<c>Context:{Key}:PersonaId &gt; 0</c>,
    /// SPEC F107.7) from <paramref name="personaStore"/> — never throws (F12.4): a missing row
    /// (deleted out of band), an unwired <paramref name="personaStore"/>, or any store fault all
    /// degrade to <see langword="null"/>, which <see cref="ResolveContextSegmentVoiceAsync"/> treats
    /// as "fall back to the station voice". Mirrors <see cref="ResolveHandoffPersonaAsync"/>'s own
    /// shape one call up (same never-throws contract, same <see cref="VoiceOf"/> empty-sentinel rule)
    /// but kept as its own method rather than shared: the two log different, context-appropriate WARN
    /// wording on a miss — a context provider's misconfigured persona id is not "a handoff boundary
    /// names" anything, and reusing that method's wording verbatim here would misdescribe the cause.
    /// </summary>
    async Task<(string Voice, string Name)?> ResolveContextPersonaAsync(
        long personaId, string providerKey, string stationVoice, CancellationToken ct)
    {
        if (personaStore is null) return null;

        try
        {
            var persona = await personaStore.GetByIdAsync(personaId, ct);
            if (persona is null)
            {
                logger.LogWarning(
                    "Context provider {ProviderKey} names persona id={PersonaId} with no matching " +
                    "persona row — falling back to the station voice (SPEC F107.7 degrade).",
                    providerKey, personaId);
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
                "Failed to resolve context provider {ProviderKey}'s persona id={PersonaId} — " +
                "falling back to the station voice (F12.4).",
                providerKey, personaId);
            return null;
        }
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
