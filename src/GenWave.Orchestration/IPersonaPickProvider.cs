namespace GenWave.Orchestration;

using GenWave.Abstractions.Playout;
using GenWave.Core.Domain;

/// <summary>
/// SPEC F81.6 rung 0 — the persona-ranking seam <see cref="Orchestrator"/> tries before falling back
/// to the envelope-only degradation ladder. PLAN T63/T64 (STORY-213) wire a real, taste-scoring
/// implementation in ahead of <see cref="NoOpPersonaPickProvider"/> in DI; until then every pick is
/// envelope-only (SPEC F81.2: "playout never depends on the persona layer existing").
///
/// <para>
/// <see cref="Orchestrator"/> wraps every call in try/catch: an implementation that throws or times
/// out degrades to the envelope-only ladder with one loud WARN naming the fault, never a faulted
/// pick (SPEC F81.6). A <see langword="null"/> result is the ordinary "no persona opinion" outcome —
/// <see cref="NoOpPersonaPickProvider"/> always returns it — and is NOT itself logged as a
/// degradation; only a thrown exception is.
/// </para>
///
/// <para>
/// Whatever this returns is re-checked against <paramref name="envelope"/> before
/// <see cref="Orchestrator"/> trusts it (trust-but-verify, SPEC F81.5): a violating pick is discarded,
/// logged, and the pick re-runs envelope-only. With <see cref="NoOpPersonaPickProvider"/> — the only
/// binding today — this re-check never fires; it exists for a future ranker that could pick outside
/// the envelope's candidate set by a scoring bug.
/// </para>
/// </summary>
public interface IPersonaPickProvider
{
    /// <summary>
    /// Attempts a persona-ranked pick over the same scope/rotation/envelope inputs the envelope-only
    /// ladder would use. A <see langword="null"/> result means "no persona opinion, use the
    /// envelope-only ladder" — not an error.
    /// </summary>
    Task<RotationCandidate?> TryPickAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct);
}
