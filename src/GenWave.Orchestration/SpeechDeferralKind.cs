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
    /// <summary>The station-id segment (today's only wired producer: <c>Station:Cadence:StationIdEveryNUnits</c>).</summary>
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
}
