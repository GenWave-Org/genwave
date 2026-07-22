using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.6, F83.1 — <see cref="PersonaRanker.PickAsync"/>'s output: the chosen candidate, whether
/// the pick came from the bias-blind exploration slice (F82.4), which taste rules fired for it, and
/// the scored Top-K's scores (highest first — SPEC F82.3's own softmax ordering). <see cref="FiredRules"/>
/// is always empty for an exploration pick (F82.4 — exploration ignores taste terms entirely, so
/// nothing fires) and never attributes an exploration pick to a rule it didn't consider (F83.2's
/// downstream lampshade posture depends on this being empty, not a stale list from a prior
/// candidate). <see cref="TopScores"/> feeds <c>Orchestrator</c>'s per-pick debug line (SPEC F82.6,
/// PLAN T64) — that line slices to the first three; this record carries the full scored Top-K so a
/// future consumer is not limited to three. T65 forwards <see cref="Candidate"/>/
/// <see cref="IsExploration"/>/<see cref="FiredRules"/> to the copywriter prompt unchanged.
/// </summary>
public sealed record PickResult(
    PersonaRankCandidate Candidate,
    bool IsExploration,
    IReadOnlyList<TasteRule> FiredRules,
    IReadOnlyList<double> TopScores);
