namespace GenWave.Core.Domain;

/// <summary>
/// SPEC F82.2 (STORY-213, PLAN T64) — one row of
/// <see cref="Abstractions.IMediaCatalog.GetEnvelopeCandidatePoolAsync"/>'s candidate pool:
/// <see cref="RotationCandidate"/>'s exact shape (a track plus which rotation preference tiers were
/// relaxed to admit it) widened with the two fields <see cref="MediaReference"/> alone does not
/// carry that <c>GenWave.Orchestration.PersonaRanker</c> needs to score and taste-match a candidate.
/// </summary>
/// <param name="Media">The catalog projection — same shape <see cref="RotationCandidate.Media"/> carries.</param>
/// <param name="Energy">
/// The LUFS-percentile energy (SPEC F80.1) in <c>[0, 1]</c>; <see langword="null"/> while a
/// population-wide recompute lags a recent enrichment write (SPEC F80.2) — the same
/// enrichment-lag-never-silences convention <see cref="Abstractions.IMediaCatalog.GetEnvelopeCandidateAsync"/>'s
/// own energy-band predicate honors, carried through rather than re-derived.
/// </param>
/// <param name="Moods">
/// Up to three fixed-vocabulary mood tags (SPEC F85.1); empty until a mood-tagger enrichment pass
/// (a later task) has run — an absent value, not a missing feature, so <c>TasteMatcher</c> simply
/// never fires a <c>tag</c> predicate against an empty list yet.
/// </param>
/// <param name="RepeatedRecent">Tier 1 (SPEC F41.3): this id was among the caller's recent-ids list.</param>
/// <param name="RepeatedArtist">Tier 2 (SPEC F41.3): this artist matched an artist among the recent window.</param>
public sealed record EnvelopeCandidateRow(
    MediaReference Media,
    double? Energy,
    IReadOnlyList<string> Moods,
    bool RepeatedRecent,
    bool RepeatedArtist);
