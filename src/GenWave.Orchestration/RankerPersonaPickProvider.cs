namespace GenWave.Orchestration;

using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The real, taste-scoring <see cref="IPersonaPickProvider"/> (SPEC F81.6 rung 0; STORY-213, PLAN
/// T64): resolves the active persona through <paramref name="personaAccessor"/>, draws an
/// envelope-filtered candidate POOL from <see cref="IMediaCatalog.GetEnvelopeCandidatePoolAsync"/>,
/// and hands both to <paramref name="ranker"/>.
///
/// <para>
/// No active persona, no card, or an empty pool are all the ordinary "no persona opinion" outcome
/// (<see langword="null"/>) — never an error, and never logged here: <see cref="Orchestrator"/>'s own
/// try/catch degrade (SPEC F81.6) is the only place a FAULT is worth a WARN, and a null return is not
/// a fault (see <see cref="IPersonaPickProvider"/>'s own remarks).
/// </para>
///
/// <para>
/// <paramref name="personaAccessor"/> is read twice per call — <see cref="IActivePersonaAccessor.ResolveAsync"/>
/// for the persona id <see cref="PersonaRanker.PickAsync"/> needs to look up taste rules, then
/// <see cref="IActivePersonaAccessor.ResolveCardAsync"/> for <see cref="PersonaCard.EnergyDisposition"/>
/// — no single accessor member returns both. This mirrors the accessor's own documented shape
/// (<c>GenWave.Host.Options.ActivePersonaAccessor</c>'s remarks: "ResolveAsync and ResolveCardAsync
/// are two independent reads of the SAME activeId"), not a novel risk this provider introduces; both
/// reads resolve the SAME live <c>Station:Persona:ActiveId</c> barring an activate/deactivate landing
/// in the narrow window between them, which degrades no worse than a stale-but-consistent pick.
/// </para>
///
/// <para>
/// The winning <see cref="PickResult"/> is mapped back onto the SAME <see cref="RotationCandidate"/>
/// shape <see cref="Orchestrator"/>'s envelope-only ladder already returns — including
/// <see cref="RotationCandidate.Energy"/> (so the trust-but-verify re-check gains an energy leg, T62
/// review carry-over) and <see cref="RotationCandidate.PersonaPick"/> (the debug-line/T65
/// diagnostics carrier, SPEC F82.6/F83.1) — rather than widening <see cref="IPersonaPickProvider"/>'s
/// own pinned return type.
/// </para>
/// </summary>
public sealed class RankerPersonaPickProvider(
    IMediaCatalog catalog,
    IActivePersonaAccessor personaAccessor,
    PersonaRanker ranker,
    PersonaRankerOptions options) : IPersonaPickProvider
{
    /// <summary>
    /// SPEC F82.6 — the per-pick debug line only ever shows the top three scores; the ranker itself
    /// reports the full scored Top-K (<see cref="PickResult.TopScores"/>) so this narrowing is this
    /// provider's own choice, not a ranker limitation.
    /// </summary>
    const int TopScoresForDebugLine = 3;

    /// <summary>
    /// A candidate whose <see cref="EnvelopeCandidateRow.Energy"/> is unknown (enrichment lag, SPEC
    /// F80.2) scores as if sitting at the population midpoint — neutral, neither favored nor
    /// penalized relative to whatever this particular envelope's own energy target happens to be.
    /// An approximation, not a re-derivation of the missing percentile.
    /// </summary>
    const double NeutralEnergyWhenUnknown = 0.5;

    /// <summary>Tier 1 (SPEC F41.3) carried into the rotation-score leg: id repeated in the recent window.</summary>
    const double RepeatedRecentPenalty = 1.0;

    /// <summary>Tier 2 (SPEC F41.3) carried into the rotation-score leg: artist repeated in the recent window.</summary>
    const double RepeatedArtistPenalty = 0.5;

    /// <inheritdoc/>
    public async Task<RotationCandidate?> TryPickAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct)
    {
        var persona = await personaAccessor.ResolveAsync(ct);
        if (persona is null) return null;

        var card = await personaAccessor.ResolveCardAsync(ct);
        if (card is null) return null;

        var pool = await catalog.GetEnvelopeCandidatePoolAsync(
            scope, orderedRecentIds, artistSeparation, envelope, options.TopK, ct);
        if (pool.Count == 0) return null;

        var rowsByMediaId = new Dictionary<string, EnvelopeCandidateRow>(pool.Count, StringComparer.Ordinal);
        var candidates = new List<PersonaRankCandidate>(pool.Count);
        foreach (var row in pool)
        {
            rowsByMediaId[row.Media.MediaId] = row;
            candidates.Add(ToRankCandidate(row));
        }

        var result = await ranker.PickAsync(persona.Id, card.EnergyDisposition, envelope.EnergyRange, candidates, ct);
        if (result is null) return null;

        var winningRow = rowsByMediaId[result.Candidate.MediaId];
        var diagnostics = new PersonaPickDiagnostics(
            PoolSize: pool.Count,
            TopScores: result.TopScores.Take(TopScoresForDebugLine).ToList(),
            FiredRules: result.FiredRules,
            IsExploration: result.IsExploration);

        return new RotationCandidate(
            winningRow.Media,
            winningRow.RepeatedRecent,
            winningRow.RepeatedArtist,
            winningRow.Energy,
            diagnostics);
    }

    static PersonaRankCandidate ToRankCandidate(EnvelopeCandidateRow row) => new(
        MediaId: row.Media.MediaId,
        Artist: row.Media.Artist,
        Genre: row.Media.Genre,
        Moods: row.Moods,
        Energy: row.Energy ?? NeutralEnergyWhenUnknown,
        RotationScore: RotationScoreOf(row));

    /// <summary>
    /// Folds the pool row's own rotation-preference tiers (SPEC F41.3 — the SAME tiers
    /// <see cref="IMediaCatalog.GetEnvelopeCandidatePoolAsync"/>'s ORDER BY already ranked the pool
    /// by) into a numeric score leg for <see cref="PersonaRanker"/>'s formula (SPEC F82.2): a
    /// candidate that repeats a recent id or artist starts every taste/energy comparison already
    /// behind one that doesn't, mirroring the SQL tier order's own severity (repeated-recent is
    /// checked first, so it costs more here too) — hygiene, not law (rotation still only ever
    /// PREFERS within the envelope's own candidate set; it was never a hard filter).
    /// </summary>
    static double RotationScoreOf(EnvelopeCandidateRow row) =>
        -(row.RepeatedRecent ? RepeatedRecentPenalty : 0.0) - (row.RepeatedArtist ? RepeatedArtistPenalty : 0.0);
}
