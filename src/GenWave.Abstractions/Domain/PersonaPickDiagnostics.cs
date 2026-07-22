namespace GenWave.Core.Domain;

/// <summary>
/// SPEC F82.6, F83.1 (STORY-213, PLAN T64) — <c>GenWave.Orchestration.PersonaRanker</c>'s own
/// diagnostics for one winning pick: how big the ranked candidate pool was, the winning Top-K's
/// scores (highest first — <c>GenWave.Orchestration.Orchestrator</c>'s per-pick debug line slices
/// this to the first three, SPEC F82.6), which taste rules fired, and whether the pick came from the
/// bias-blind exploration slice (SPEC F82.4). Carried on <see cref="RotationCandidate.PersonaPick"/>
/// only when a real <c>PersonaRanker</c> pick won rung 0 (<see langword="null"/> for every
/// envelope-only ladder pick, SPEC F81.6, including the common persona-off case) and forwarded onto
/// <see cref="MediaItem.PersonaPick"/> once the winning candidate narrows to the playout-facing item
/// — the same object <c>Orchestrator.EnqueuePatterAsync</c> hands the TTS patter path
/// (<see cref="SegmentRequest.Track"/>), so a future copywriter consumer (T65) reads
/// <see cref="FiredRules"/>/<see cref="IsExploration"/> off whichever track aired, with no separate
/// lookup or re-architecture.
/// </summary>
public sealed record PersonaPickDiagnostics(
    int PoolSize,
    IReadOnlyList<double> TopScores,
    IReadOnlyList<TasteRule> FiredRules,
    bool IsExploration);
