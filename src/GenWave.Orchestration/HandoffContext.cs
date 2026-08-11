namespace GenWave.Orchestration;

/// <summary>
/// The rendering inputs a handoff-kind <see cref="SpeechDeferral"/> (<see cref="SpeechDeferralKind.SignOff"/>/
/// <see cref="SpeechDeferralKind.SignOn"/>) carries in ADDITION to Kind/Due/Reason (SPEC F92.1/F92.2,
/// STORY-243, PLAN T124) — captured by <see cref="Orchestrator"/>'s handoff producer at ENQUEUE time
/// from the (outgoing, incoming) persona pair the resolver's <c>OnAirSnapshot</c> named at that
/// instant, and carried verbatim to the drain.
///
/// <para>
/// This is deliberately NOT the whole (outgoing, incoming) pair on every entry — each deferral only
/// ever needs to describe ITS OWN piece: <see cref="Voice"/>/<see cref="PersonaName"/> are the
/// OUTGOING DJ's for a <see cref="SpeechDeferralKind.SignOff"/> deferral, the INCOMING DJ's for a
/// <see cref="SpeechDeferralKind.SignOn"/> one; <see cref="CounterpartName"/> is always the OTHER
/// DJ's name (or <see langword="null"/> for a music-only half, SPEC F92.3). The smallest honest
/// shape that lets the drain build a <c>SegmentRequest</c> directly, with no second lookup.
/// </para>
///
/// <para>
/// <b>Why captured, never re-resolved at drain time:</b> a sign-off can drain AFTER the wall clock
/// has already flipped past the boundary (the boundary passed mid-track) — at that instant,
/// <c>IActivePersonaAccessor</c>/the resolver would answer with the INCOMING persona, not the
/// outgoing one this piece must still be voiced as. Carrying the pair captured at enqueue time is
/// what keeps the drain honest regardless of when it actually fires.
/// </para>
/// </summary>
/// <param name="Voice">This piece's own TTS voice.</param>
/// <param name="PersonaName">This piece's own display name (<c>SegmentRequest.PersonaName</c>).</param>
/// <param name="CounterpartName">
/// The OTHER DJ's display name (<c>SegmentRequest.CounterpartName</c>), or <see langword="null"/>
/// when no counterpart exists for this boundary (a music-only half, SPEC F92.3).
/// </param>
/// <param name="CrossingTrackTitle">
/// SPEC F111.3 (PLAN T235), additive and optional (default <see langword="null"/> — every pre-T235
/// construction site, including the SignOff half of every ceremony, stays diff-free): the deliberately
/// boundary-crossing track's own <c>MediaItem.Title</c>, captured into the HELD SignOn's context at
/// straddle plan time (<c>Orchestrator.CaptureCrossingTrackForHeldSignOn</c>) — the SAME
/// immutable-capture pattern this whole record already establishes for Voice/PersonaName/
/// CounterpartName, just captured one straddle-seam later than the rest of them. <see langword="null"/>
/// for every non-straddle handoff (both pieces of an ordinary boundary, and the SignOff half of a
/// straddle one) — there is no crossing track to name.
/// </param>
/// <param name="CrossingTrackArtist">
/// The same crossing track's <c>MediaItem.Artist</c>, captured alongside <see cref="CrossingTrackTitle"/>
/// — <see langword="null"/> whenever that is (an untagged track, or no straddle at all).
/// </param>
/// <param name="ShowName">
/// SPEC F116.1/F116.2 (STORY-307, PLAN T248): this piece's OWN show — the ending show's name for a
/// <see cref="SpeechDeferralKind.SignOff"/> deferral, the incoming show's name for a
/// <see cref="SpeechDeferralKind.SignOn"/> one — mirrors <see cref="PersonaName"/>'s own
/// self/counterpart split. Captured at <c>Orchestrator.EnqueueHandoffCeremonyAsync</c> ENQUEUE time
/// straight off the resolver's own <c>OnAirSnapshot.Show</c>/<c>OnAirSnapshot.NextSegment.Show</c>
/// (SPEC F116.1's chokepoint — never re-derived), the SAME immutable-capture pattern this whole
/// record already establishes for Voice/PersonaName/CounterpartName. <see langword="null"/> for an
/// unnamed block, additive and optional so every pre-F116 construction site stays diff-free.
/// </param>
/// <param name="ShowFlavor">
/// SPEC F116.2/F115.3: <see cref="ShowName"/>'s own flavor text, captured ONLY for the incoming show
/// on a <see cref="SpeechDeferralKind.SignOn"/> deferral (F116.2 names flavor for the sign-on prompt
/// alone) — always <see langword="null"/> on a <see cref="SpeechDeferralKind.SignOff"/> deferral's
/// context, deliberately, not merely because it happens to be unset. Prompt-only forever (F115.3, the
/// persona-soul precedent): reaches <c>LlmPromptBuilder</c>'s prompt text and nothing else — never a
/// public payload, never a log line (this record's own compiler-generated <c>ToString()</c> would
/// render it verbatim, so no <c>{Handoff}</c>-style placeholder may ever bind this type; log
/// <see cref="PersonaName"/>/<see cref="ShowName"/> by name instead, exactly like
/// <see cref="GenWave.Core.Domain.ShowSummary"/>'s own remarks require for the type this field's
/// value is sourced from).
/// </param>
/// <param name="CounterpartShowName">
/// SPEC F114.3/F116.2: the OTHER piece's show — mirrors <see cref="CounterpartName"/>'s own
/// self/counterpart split, but populated for a <see cref="SpeechDeferralKind.SignOff"/> deferral only
/// (F114.3's "sign-off may name the ending show and the next"; F116.2 gives sign-on no analogous
/// license to name the show it is leaving). <see langword="null"/> on a <see cref="SpeechDeferralKind.SignOn"/>
/// deferral's context, and whenever no next show is named.
/// </param>
public sealed record HandoffContext(
    string Voice,
    string? PersonaName,
    string? CounterpartName,
    string? CrossingTrackTitle = null,
    string? CrossingTrackArtist = null,
    string? ShowName = null,
    string? ShowFlavor = null,
    string? CounterpartShowName = null);
