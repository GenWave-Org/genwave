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
public sealed record HandoffContext(string Voice, string? PersonaName, string? CounterpartName);
