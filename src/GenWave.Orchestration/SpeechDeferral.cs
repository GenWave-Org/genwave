namespace GenWave.Orchestration;

using GenWave.Core.Domain;

/// <summary>
/// A single pending "speak at the next boundary" request held by <see cref="SpeechDeferralQueue"/>
/// (SPEC F74.1). <paramref name="Due"/> is the wall-clock instant the deferral became due — for
/// today's only producer (the station-id cadence check) that is always "now", since the trigger
/// and the boundary decision are the same per-unit planning pass; a future producer (e.g. a
/// wall-clock-scheduled handoff) can enqueue a future <paramref name="Due"/> instead.
/// </summary>
/// <param name="Kind">Which scheduled speech this is.</param>
/// <param name="Due">The instant this deferral becomes eligible to air (SPEC F74.1).</param>
/// <param name="Reason">A short, human-readable note for logs/diagnostics — never parsed.</param>
/// <param name="Handoff">
/// Additive, optional (SPEC F92.1/F92.2, STORY-243, PLAN T124): the captured voice/name/counterpart
/// a <see cref="SpeechDeferralKind.SignOff"/>/<see cref="SpeechDeferralKind.SignOn"/> deferral needs
/// to build its <c>SegmentRequest</c> at drain time — see <see cref="HandoffContext"/>'s own remarks
/// for why this is captured at enqueue time rather than re-resolved at drain time.
/// <see langword="null"/> for every other kind (<see cref="SpeechDeferralKind.StationId"/> today).
/// </param>
/// <param name="Discriminator">
/// Additive (SPEC F107.4, STORY-297): the sub-key <see cref="SpeechDeferralQueue"/>'s per-
/// <c>(kind, discriminator)</c> supersede uses alongside <see cref="Kind"/> — for
/// <see cref="SpeechDeferralKind.Context"/> this is the originating
/// <see cref="GenWave.Core.Abstractions.IContextProvider.Key"/>, so a due weather fact and a due
/// history fact coexist instead of one silently discarding the other. <see langword="null"/> for
/// every other kind — every producer that existed before F107 keeps its old one-per-kind supersede,
/// byte-identical. A future consumer (T224) reads this back off the dequeued entry to fetch the
/// matching pipeline content by key.
/// </param>
/// <param name="Context">
/// Additive, optional (SPEC F107.3, STORY-297, PLAN T224; reshaped F125.2/F125.3): the immutable
/// payload a <see cref="SpeechDeferralKind.Context"/> deferral needs to drain —
/// <c>GenWave.Context</c>'s own <c>ContextPipeline.TickAsync</c> result for the due provider, already
/// selected and joined at vend time, captured by the T226 Host ticker at ENQUEUE time and carried
/// verbatim to the drain (the same "capture, never re-fetch" posture <see cref="Handoff"/> established
/// one field up — see that param's own remarks). The drain arm re-checks freshness against
/// <see cref="ContextSegmentFacts.FreshUntil"/> at DRAIN time regardless (a unit boundary can land
/// well after this deferral was enqueued), so capturing the content early never risks airing stale
/// facts. <see langword="null"/> for every other kind, and defensively tolerated (skip, not throw)
/// should a <see cref="SpeechDeferralKind.Context"/> deferral ever somehow arrive without one.
/// </param>
public sealed record SpeechDeferral(
    SpeechDeferralKind Kind,
    DateTimeOffset Due,
    string Reason,
    HandoffContext? Handoff = null,
    string? Discriminator = null,
    ContextSegmentFacts? Context = null);
