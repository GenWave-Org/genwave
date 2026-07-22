using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.6, F83.1 — <see cref="PersonaRanker.PickAsync"/>'s output: the chosen candidate, whether
/// the pick came from the bias-blind exploration slice (F82.4), and which taste rules fired for it.
/// <see cref="FiredRules"/> is always empty for an exploration pick (F82.4 — exploration ignores
/// taste terms entirely, so nothing fires) and never attributes an exploration pick to a rule it
/// didn't consider (F83.2's downstream lampshade posture depends on this being empty, not a stale
/// list from a prior candidate). T65 forwards this shape to the copywriter prompt unchanged.
/// </summary>
public sealed record PickResult(
    PersonaRankCandidate Candidate,
    bool IsExploration,
    IReadOnlyList<TasteRule> FiredRules);
