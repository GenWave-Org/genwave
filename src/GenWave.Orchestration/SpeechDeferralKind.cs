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
}
