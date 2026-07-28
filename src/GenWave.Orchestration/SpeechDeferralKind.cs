namespace GenWave.Orchestration;

/// <summary>
/// Identifies the kind of scheduled speech a <see cref="SpeechDeferral"/> represents (SPEC
/// F74.1/F74.2). <see cref="SpeechDeferralQueue"/> tracks at most one pending deferral per kind —
/// a newer enqueue of the same kind supersedes the pending one (F74.2).
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
}
