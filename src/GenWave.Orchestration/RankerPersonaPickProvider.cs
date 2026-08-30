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
/// (<see cref="OnAirPersonaAccessor"/>'s remarks: "ResolveAsync and ResolveCardAsync are two
/// independent reads of the SAME on-air snapshot"), not a novel risk this provider introduces; both
/// reads resolve the SAME on-air persona (SPEC F91.5) barring a schedule boundary landing in the
/// narrow window between them, which degrades no worse than a stale-but-consistent pick.
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
            IsExploration: result.IsExploration)
        {
            // SPEC F151.4 (STORY-371, PLAN T370) — same narrowing/ordering as TopScores just above.
            TopNudges = result.TopNudges.Take(TopScoresForDebugLine).ToList(),
        };

        return new RotationCandidate(
            winningRow.Media,
            winningRow.RepeatedRecent,
            winningRow.RepeatedArtist,
            winningRow.Energy,
            diagnostics)
        {
            // SPEC F151.1/F151.2 (STORY-371, PLAN T370) — the winning row's own ledger nudge, the
            // SAME value ToRankCandidate mapped onto the PersonaRankCandidate.Score just consumed.
            // Set here, alongside diagnostics, so "this candidate carries a Nudge" and "this candidate
            // came from rung 0" are structurally the same fact — no envelope-only rung ever sets it.
            Nudge = winningRow.Nudge,
        };
    }

    /// <summary>
    /// SPEC F82.2 base mapping, extended by SPEC F151.1 (STORY-372, PLAN T359) to carry
    /// <see cref="EnvelopeCandidateRow.Nudge"/>/<see cref="EnvelopeCandidateRow.PlayCount"/> straight
    /// onto <see cref="PersonaRankCandidate"/> — no transformation, the pool query already
    /// <c>coalesce</c>'d both to their zero defaults.
    ///
    /// <para>
    /// MED-1 (PLAN T359 review) — kept <c>internal</c> (not reverted to <c>private</c>) rather than
    /// driving <see cref="TryPickAsync"/> through the PUBLIC route with a fake <c>IMediaCatalog</c>:
    /// the public route's ONLY return type is <see cref="RotationCandidate"/>
    /// (<see cref="IPersonaPickProvider"/>'s pinned contract), and NEITHER
    /// <see cref="RotationCandidate"/> NOR its own <see cref="PersonaPickDiagnostics"/> carries
    /// <see cref="PersonaRankCandidate.Nudge"/>/<see cref="PersonaRankCandidate.PlayCount"/> anywhere
    /// — those two fields exist ONLY on the intermediate <see cref="PersonaRankCandidate"/> this
    /// method builds, consumed internally by <see cref="PersonaRanker.PickAsync"/> and never
    /// surfaced back out. Proving "the carrier reaches <see cref="PersonaRankCandidate"/>" through
    /// the public API would require widening a PUBLISHED production return shape solely to give a
    /// T359 test an observation point — a bigger, out-of-scope production change (and arguably
    /// T370's call, once <c>PersonaRanker.Score</c> actually consumes <see cref="PersonaRankCandidate.Nudge"/>
    /// and has its own reason to surface it) — so the narrow <c>InternalsVisibleTo</c> seam stays:
    /// same shape <c>LegacyPersonaCardMapper.Slugify</c>/<c>AnnouncementRepository</c>'s own internal
    /// test seams already use one project over. <c>FeatureTheNudgeInTheRanker</c>'s AC4 facts
    /// (STORY-371) exercise this exact projection directly via that grant; MED-1's own DB-backed
    /// facts (<c>FeatureThePoolHonoursTheRotationPredicate.ScenarioThePoolProjectsTheLedgerValues</c>,
    /// <c>GenWave.MediaLibrary.Tests</c>) separately prove the SQL/Dapper round-trip that produces
    /// the <see cref="EnvelopeCandidateRow"/> this method reads from — the two facts together cover
    /// producer (SQL) and consumer (this mapping) without touching a published contract.
    /// </para>
    /// </summary>
    internal static PersonaRankCandidate ToRankCandidate(EnvelopeCandidateRow row) => new(
        MediaId: row.Media.MediaId,
        Artist: row.Media.Artist,
        Genre: row.Media.Genre,
        Moods: row.Moods,
        Energy: row.Energy ?? NeutralEnergyWhenUnknown,
        RotationScore: RotationScoreOf(row),
        Nudge: row.Nudge,
        PlayCount: row.PlayCount);

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
