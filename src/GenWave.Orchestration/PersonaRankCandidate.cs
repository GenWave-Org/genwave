namespace GenWave.Orchestration;

/// <summary>
/// SPEC F82.1-F82.2 — one track <see cref="PersonaRanker"/> scores: the same envelope-filtered pool
/// <c>GenWave.Orchestration.Orchestrator</c>'s envelope-only ladder already draws from (T61/T62), plus
/// exactly the fields the score formula and taste matcher need. <see cref="Energy"/> is the T57
/// LUFS-percentile in <c>[0, 1]</c> — the T62 review note flagged that <c>MediaReference</c> alone
/// lacks it; T64 is expected to carry it through its own catalog-row mapping so this shape can be
/// built. <see cref="Genre"/> exists on every candidate today; <see cref="Moods"/> is empty until
/// T72's mood-tag enrichment lands data to populate it — an absent value here, not a missing feature,
/// so <see cref="TasteMatcher"/> simply never fires a <c>tag</c> predicate against an empty list.
/// </summary>
public sealed record PersonaRankCandidate(
    string MediaId,
    string? Artist,
    string? Genre,
    IReadOnlyList<string> Moods,
    double Energy,
    double RotationScore);
