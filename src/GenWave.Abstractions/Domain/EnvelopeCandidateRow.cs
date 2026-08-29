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
    bool RepeatedArtist)
{
    /// <summary>
    /// SPEC F151.1 (STORY-372, PLAN T359, Abstractions 5.5.0) — the <c>library.media_rotation.nudge</c>
    /// ledger value (<c>[-1, 1]</c>), <c>0</c> for a never-aired track or one with no ledger row at
    /// all (<c>coalesce(rot.nudge, 0)</c> at the query). This record only CARRIES the value —
    /// <c>PersonaRanker.Score</c> turning it into an additive scoring term is T370's job, not this
    /// one's (SPEC F81.2: the envelope filters, the bias/nudge only ever ranks, never filters).
    /// Deliberately a NON-positional <c>init</c> property, the same convention
    /// <see cref="GenWave.Abstractions.Playout.SegmentEnvelope.Rotation"/> established (STORY-372
    /// AC1): every pre-5.5.0 positional <c>new EnvelopeCandidateRow(...)</c> call site — including
    /// <see cref="Abstractions.IMediaCatalog"/>'s own default-interface fallback — keeps compiling
    /// unchanged, defaulting to <c>0</c> (no nudge).
    /// </summary>
    public double Nudge { get; init; }

    /// <summary>
    /// SPEC F151.1 — the <c>library.media_rotation.play_count</c> ledger value, <c>0</c> for a
    /// never-aired track or one with no ledger row. Carried for observability (a future booth-log/
    /// debug-line chip) rather than any ranking use today. Same non-positional, additive shape as
    /// <see cref="Nudge"/>.
    /// </summary>
    public int PlayCount { get; init; }
}
