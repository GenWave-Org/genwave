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
///
/// <para>
/// SPEC F151.1 (STORY-372, PLAN T359) — <see cref="Nudge"/>/<see cref="PlayCount"/> carry
/// <c>library.media_rotation</c>'s own ledger values (<c>RankerPersonaPickProvider.ToRankCandidate</c>'s
/// mapping from <c>EnvelopeCandidateRow</c>), default <c>0</c>. This record only CARRIES them —
/// <see cref="PersonaRanker.Score"/> turning <see cref="Nudge"/> into an additive scoring term is
/// T370's job, not this one's (SPEC F81.2: the envelope filters, the bias/nudge only ever ranks).
/// Trailing optional positional parameters (not a non-positional <c>init</c> property, unlike the
/// published <c>GenWave.Abstractions</c> contract's own T356/T359 additions): this record is internal
/// to <c>GenWave.Orchestration</c>, never a versioned SDK surface, so every pre-T359 positional
/// <c>new PersonaRankCandidate(...)</c> call site (this project's own tests included) keeps compiling
/// unchanged by the default alone.
/// </para>
/// </summary>
public sealed record PersonaRankCandidate(
    string MediaId,
    string? Artist,
    string? Genre,
    IReadOnlyList<string> Moods,
    double Energy,
    double RotationScore,
    double Nudge = 0,
    int PlayCount = 0);
