using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// SPEC F152.4 (STORY-372, PLAN T361) — the house fake-catalog idiom (<c>FakeLadderMediaCatalog</c>/
/// <c>FakePersonaPoolCatalog</c>) one seam over: <see cref="GetEnvelopeCandidatePoolAsync"/> and
/// <see cref="GetEnvelopeCandidateAsync"/> apply REAL by-construction filtering — genre (SPEC F81.1)
/// AND rotation (<see cref="RotationPredicate.MaxPlays"/>'s exact
/// <c>MediaRepository.RotationPredicateSql</c> semantics, mirrored in C#) — over a caller-supplied
/// (genre, <c>play_count</c>) row set, so the R0→R3 relax ladder in <see cref="MusicSelectionPolicy"/>
/// can be exercised against genuinely different pool answers per step, not a scripted null/non-null
/// toggle. <see cref="GetEnvelopeCandidateAsync"/> — HIGH-1 (T361 review) — is what proves a
/// PERSONA-LESS pick still honours the F152 predicate through the envelope-only fallback rather than
/// silently skipping the whole ladder. <see cref="GetPlayCountQuantileAsync"/> computes the SAME
/// discrete percentile Postgres' own <c>percentile_disc</c> would, over genre-matching rows only,
/// regardless of any rotation predicate (mirrors the production query's own "rotation predicate
/// excluded from this one read" contract). <see cref="GetRotationCandidateAsync"/> stays genre- AND
/// rotation-BLIND — SPEC F81.6's own terminal never-silence floor, unconstrained by either.
/// </summary>
sealed class FakeRotationLadderPoolCatalog(IReadOnlyList<FakeRotationLadderPoolCatalog.Row> rows) : IMediaCatalog
{
    /// <summary>One library row this fake answers from — <c>PlayCount</c> is the ledger field
    /// STORY-372 AC7/AC8's own MaxPlays-only fixtures need; <c>Media.Genre</c> is what HIGH-2's
    /// rewritten AC9 uses to force R0–R3's own genre-constrained pool empty (the ONLY way R2's
    /// quantile can genuinely answer null with a non-empty catalog — see that fact's own remarks for
    /// why "a high PlayCount" alone can never make R2 fail: <c>percentile_disc</c> always returns an
    /// actually-observed value, so a non-empty genre pool always admits at least one row at R2).</summary>
    public sealed record Row(MediaReference Media, int PlayCount);

    public Task<IReadOnlyList<EnvelopeCandidateRow>> GetEnvelopeCandidatePoolAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        int limit,
        CancellationToken ct)
    {
        IReadOnlyList<EnvelopeCandidateRow> pool = GenreAndRotationMatching(envelope)
            .Select(row => new EnvelopeCandidateRow(row.Media, Energy: null, Moods: [], RepeatedRecent: false, RepeatedArtist: false)
            {
                PlayCount = row.PlayCount,
            })
            .ToList();
        return Task.FromResult(pool);
    }

    /// <summary>
    /// HIGH-1 (T361 review) — the un-relaxed envelope-only pick <see cref="MusicSelectionPolicy.TryRotationStepAsync"/>
    /// falls back to when no persona is bound (the DEFAULT <c>NoOpPersonaPickProvider</c> case): the
    /// SAME by-construction genre+rotation filtering <see cref="GetEnvelopeCandidatePoolAsync"/>
    /// applies, narrowed to one row.
    /// </summary>
    public Task<RotationCandidate?> GetEnvelopeCandidateAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct)
    {
        var eligible = GenreAndRotationMatching(envelope).ToList();
        if (eligible.Count == 0) return Task.FromResult<RotationCandidate?>(null);

        var pick = eligible.FirstOrDefault(row => !orderedRecentIds.Contains(row.Media.MediaId)) ?? eligible[0];
        return Task.FromResult<RotationCandidate?>(
            new RotationCandidate(pick.Media, orderedRecentIds.Contains(pick.Media.MediaId), RepeatedArtist: false));
    }

    /// <summary>
    /// Mirrors Postgres' own <c>percentile_disc</c> exactly: the 1-based rank
    /// <c>CEIL(quantile * n)</c> (clamped to at least 1) into the ascending-sorted set — e.g. 5 rows
    /// of <c>[0, 1, 2, 10, 50]</c> at quantile 0.1 picks rank <c>CEIL(0.5) = 1</c>, i.e. the value
    /// <c>0</c> (verified against a real Postgres instance). Genre-matching rows only — SPEC F152.4's
    /// "the envelope's own genre/energy-constrained pool" — rotation itself deliberately ignored even
    /// though LOW-2 (T361 review) means the caller never hands this a non-null Rotation any more
    /// anyway. <see langword="null"/> when no row matches the envelope's genre allow-list at all.
    /// </summary>
    public Task<int?> GetPlayCountQuantileAsync(
        LibraryScope scope, SegmentEnvelope envelope, double quantile, CancellationToken ct)
    {
        var genreMatching = rows.Where(row => SatisfiesGenre(row.Media, envelope)).ToList();
        if (genreMatching.Count == 0) return Task.FromResult<int?>(null);

        var sorted = genreMatching.Select(row => row.PlayCount).OrderBy(playCount => playCount).ToList();
        var rank = Math.Max(1, (int)Math.Ceiling(quantile * sorted.Count));
        var index = Math.Min(rank, sorted.Count) - 1;
        return Task.FromResult<int?>(sorted[index]);
    }

    IEnumerable<Row> GenreAndRotationMatching(SegmentEnvelope envelope) =>
        rows.Where(row => SatisfiesGenre(row.Media, envelope) && SatisfiesRotation(row.PlayCount, envelope.Rotation));

    static bool SatisfiesGenre(MediaReference media, SegmentEnvelope envelope) =>
        envelope.Genres.Count == 0 ||
        (media.Genre is not null && envelope.Genres.Any(g => string.Equals(g, media.Genre, StringComparison.OrdinalIgnoreCase)));

    static bool SatisfiesRotation(int playCount, RotationPredicate? rotation) =>
        rotation?.MaxPlays is not int maxPlays || playCount <= maxPlays;

    /// <summary>SPEC F81.6's own terminal never-silence floor — genre- AND rotation-BLIND by
    /// definition (the plain pre-envelope query), the row HIGH-2's rewritten AC9 relies on to prove
    /// "never silence" once every relaxed rung above has failed.</summary>
    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        var pick = rows.FirstOrDefault(row => !orderedRecentIds.Contains(row.Media.MediaId)) ?? rows.FirstOrDefault();
        return Task.FromResult(pick is null
            ? null
            : new RotationCandidate(pick.Media, orderedRecentIds.Contains(pick.Media.MediaId), RepeatedArtist: false));
    }

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct) =>
        Task.FromResult(rows.Select(row => row.Media).FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct) =>
        Task.FromResult(rows.Select(row => row.Media).FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        Task.FromResult(rows.Count == 0 ? null : rows[0].Media);

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
        Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);
}
