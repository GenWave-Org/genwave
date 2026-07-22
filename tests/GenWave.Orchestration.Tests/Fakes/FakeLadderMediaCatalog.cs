using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Envelope-aware catalog double for STORY-212 specs (SPEC F81.4-F81.6) — mirrors the
/// <c>FakeMediaCatalog</c> idiom (Story007) one seam over: where that fake ignores
/// <see cref="IMediaCatalog.GetEnvelopeCandidateAsync"/> entirely (falling through to the
/// interface's own envelope-blind default implementation), this one applies REAL by-construction
/// genre filtering — <see cref="MediaReference.Genre"/> must case-insensitively match one of
/// <see cref="SegmentEnvelope.Genres"/>, or the list must be empty — so the envelope-only pick
/// path (SPEC F81.2) and the degradation ladder (F81.6) can be exercised against genuinely
/// different candidate pools per rung, not merely a scripted null/non-null toggle.
///
/// <para>
/// Energy is deliberately NOT filtered: <see cref="MediaReference"/> carries no population-percentile
/// energy value (only <see cref="MediaReference.IntroEnergy"/>/<see cref="MediaReference.OutroEnergy"/>,
/// a different per-track measure) for this fake to filter on either — mirroring exactly what the
/// production <c>Orchestrator</c>'s own trust-but-verify re-check can and cannot see (SPEC F81.4's
/// null-always-passes exemption).
/// </para>
///
/// <para>
/// <see cref="NullUntilCallNumber"/> forces every call up to (but not including) that ordinal — counted
/// across BOTH <see cref="GetEnvelopeCandidateAsync"/> and <see cref="GetRotationCandidateAsync"/>, in
/// the exact order <c>Orchestrator</c>'s ladder issues them — to return null regardless of genre
/// match, so a SadPathDegradationLadder spec can force the pool empty for exactly the leading N
/// rungs and prove the (N+1)th is the one that actually succeeds.
/// </para>
/// </summary>
sealed class FakeLadderMediaCatalog(IReadOnlyList<MediaReference> pool) : IMediaCatalog
{
    int callNumber;

    /// <summary>Every call before this ordinal (1-based, envelope + rotation calls share one counter)
    /// returns null regardless of genre match. <see langword="null"/> (the default) never forces a
    /// null — every call resolves purely from genre filtering.</summary>
    public int? NullUntilCallNumber { get; set; }

    /// <summary>Every envelope <see cref="GetEnvelopeCandidateAsync"/> was called with, in call order.</summary>
    public List<SegmentEnvelope> EnvelopeCallEnvelopes { get; } = [];

    /// <summary>Every orderedRecentIds list <see cref="GetEnvelopeCandidateAsync"/> was called with.</summary>
    public List<IReadOnlyList<string>> EnvelopeCallOrderedRecentIds { get; } = [];

    /// <summary>Every artistSeparation value <see cref="GetEnvelopeCandidateAsync"/> was called with.</summary>
    public List<int> EnvelopeCallArtistSeparations { get; } = [];

    /// <summary>How many times the terminal, envelope-blind <see cref="GetRotationCandidateAsync"/> was called.</summary>
    public int RotationCallCount { get; private set; }

    public Task<RotationCandidate?> GetEnvelopeCandidateAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        CancellationToken ct)
    {
        callNumber++;
        EnvelopeCallEnvelopes.Add(envelope);
        EnvelopeCallOrderedRecentIds.Add(orderedRecentIds);
        EnvelopeCallArtistSeparations.Add(artistSeparation);
        return Task.FromResult(Resolve(orderedRecentIds, envelope));
    }

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        callNumber++;
        RotationCallCount++;
        // The terminal, pre-envelope query (SPEC F81.6) is genre-blind by definition — every genre
        // admitted, mirroring SegmentEnvelope.StationDefault's own "empty Genres = all genres" shape.
        return Task.FromResult(Resolve(orderedRecentIds, SegmentEnvelope.StationDefault));
    }

    RotationCandidate? Resolve(IReadOnlyList<string> orderedRecentIds, SegmentEnvelope envelope)
    {
        if (NullUntilCallNumber is int gate && callNumber < gate) return null;

        var eligible = pool.Where(m => SatisfiesGenre(m, envelope)).ToList();
        if (eligible.Count == 0) return null;

        var pick = eligible.FirstOrDefault(m => !orderedRecentIds.Contains(m.MediaId)) ?? eligible[0];
        return new RotationCandidate(pick, orderedRecentIds.Contains(pick.MediaId), false);
    }

    static bool SatisfiesGenre(MediaReference media, SegmentEnvelope envelope) =>
        envelope.Genres.Count == 0 ||
        (media.Genre is not null &&
            envelope.Genres.Any(g => string.Equals(g, media.Genre, StringComparison.OrdinalIgnoreCase)));

    // Not exercised by STORY-212 specs — same "no other seam needs it" story as FakeMediaCatalog's
    // own GetFacetsAsync remarks.
    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct) =>
        Task.FromResult(pool.FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct) =>
        Task.FromResult(pool.FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        Task.FromResult(pool.Count == 0 ? null : pool[0]);

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
        Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);
}
