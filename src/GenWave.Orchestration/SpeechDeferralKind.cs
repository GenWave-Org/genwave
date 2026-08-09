namespace GenWave.Orchestration;

/// <summary>
/// Identifies the kind of scheduled speech a <see cref="SpeechDeferral"/> represents (SPEC
/// F74.1/F74.2). <see cref="SpeechDeferralQueue"/> tracks at most one pending deferral per
/// <c>(kind, discriminator)</c> pair (SPEC F107.4) — a newer enqueue of the same pair supersedes
/// the pending one (F74.2); <see cref="SpeechDeferral.Discriminator"/> is null for every kind below
/// EXCEPT <see cref="Context"/>, so supersede stays exactly one-per-kind for all of them — the
/// pre-F107 behavior, byte-identical.
/// </summary>
public enum SpeechDeferralKind
{
    /// <summary>
    /// The station-id segment — two producers today: the unit-count cadence
    /// (<c>Station:Cadence:StationIdEveryNUnits</c>, <see cref="Orchestrator"/>'s own trigger, via
    /// <see cref="SpeechDeferralQueue.Enqueue"/>'s unconditional overwrite) and, additively (SPEC
    /// F110.1, STORY-301, PLAN T230), the clock-anchored top-of-hour trigger
    /// (<see cref="ClockAnchoredImagingProducer"/>, gated on <c>Station:Imaging:ClockAnchoredIdents</c>,
    /// via <see cref="SpeechDeferralQueue.EnqueueIfAbsent"/>'s conditional one — PLAN T230 review F1).
    /// Both share the SAME <c>(kind, null)</c> supersede slot, but asymmetrically: the cadence trigger
    /// always claims the slot the instant it fires, unconditionally overwriting whatever was pending;
    /// the clock-anchored trigger only ever claims an EMPTY slot — it never displaces a deferral
    /// already pending from either producer, so it can never race its own not-yet-drained deferral off
    /// the queue merely by recomputing a later due instant on a subsequent tick. With clock anchoring
    /// at its false default this stays the single-producer shape it always was (T230 acceptance:
    /// byte-identical sound).
    /// </summary>
    StationId,

    /// <summary>
    /// The outgoing DJ's sign-off at a roster boundary (SPEC F92.1, STORY-243, PLAN T124) — enqueued
    /// by <see cref="Orchestrator"/>'s own handoff producer once the resolved boundary enters the
    /// F74.3 lookahead window, due shortly BEFORE the boundary instant. Always carries a
    /// <see cref="SpeechDeferral.Handoff"/> payload.
    /// </summary>
    SignOff,

    /// <summary>
    /// The incoming DJ's sign-on at a roster boundary (SPEC F92.1, STORY-243, PLAN T124) — enqueued
    /// alongside <see cref="SignOff"/>, due AT the boundary instant. Always carries a
    /// <see cref="SpeechDeferral.Handoff"/> payload.
    /// </summary>
    SignOn,

    /// <summary>
    /// A context provider's fact-based segment (SPEC F107.3/F107.4, STORY-297) — enqueued by the
    /// T226 Host ticker once a provider's <c>Context:{Key}:SegmentCadenceMinutes</c> elapses.
    /// Carries the originating <see cref="GenWave.Core.Abstractions.IContextProvider.Key"/> as its
    /// <see cref="SpeechDeferral.Discriminator"/> — the ONE kind in this enum where supersede is
    /// per-provider rather than singleton, so a due weather fact never silently discards a due
    /// history fact (F107.4).
    /// </summary>
    Context,

    /// <summary>
    /// The clock-anchored time announcement (SPEC F110.3, STORY-302, PLAN T230) — enqueued by
    /// <see cref="ClockAnchoredImagingProducer"/> alongside <see cref="StationId"/> whenever
    /// <c>Station:Imaging:TimeAnnouncements</c> is on, due at the SAME station-local top-of-hour
    /// instant. This is the enum value's first producer — before T230 nothing ever enqueued this
    /// kind. Discriminator is always <see langword="null"/>: a singleton, one-pending-per-station
    /// cadence, the same supersede shape every pre-F107 kind carries (SPEC F107.4).
    /// </summary>
    TimeDate,
}
