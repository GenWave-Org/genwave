using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted catalog double for orchestrator unit tests. Captures every call to
/// <see cref="GetRandomReadyAsync"/> and <see cref="GetRotationCandidateAsync"/> so tests can assert
/// on exclude-id filtering, scope passing, and the ordered-recent/artist-separation args SPEC F41.1
/// added. An empty pool makes <see cref="GetRotationCandidateAsync"/> return null (the
/// zero-playable-pool case, F41.2); <see cref="ScriptedRepeatedArtist"/> forces the RepeatedArtist
/// flag a non-null candidate carries.
///
/// <para>
/// <see cref="WithPool"/> seeds more than one candidate (SPEC F74.3, STORY-198):
/// <see cref="GetRotationCandidateAsync"/> then round-robins one pool entry per call — standing in
/// for the real <c>MediaRepository</c>'s <c>ORDER BY ... random() LIMIT 1</c>, which draws a
/// (non-deterministic) different row from the same tiered pool on repeat calls. Round-robin keeps
/// the boundary-bias specs deterministic: seeding a 3-minute and a 9-minute track guarantees the
/// Orchestrator's bias-window sampling loop sees both within a handful of calls, regardless of call
/// count parity. A factory method rather than a second public constructor overload keeps every
/// existing single-track <c>new FakeMediaCatalog(ready)</c> call site unambiguous, including the
/// literal-<see langword="null"/> ones.
/// </para>
/// </summary>
sealed class FakeMediaCatalog : IMediaCatalog
{
    readonly IReadOnlyList<MediaReference> pool;
    int nextIndex;

    public FakeMediaCatalog(MediaReference? ready) : this(ready is null ? [] : [ready])
    {
    }

    FakeMediaCatalog(IReadOnlyList<MediaReference> pool)
    {
        this.pool = pool;
    }

    /// <summary>See the type-level remarks — a candidate pool for boundary-bias specs (SPEC F74.3).</summary>
    public static FakeMediaCatalog WithPool(IReadOnlyList<MediaReference> pool) => new(pool);

    public List<IReadOnlyList<string>> RandomCallExcludeLists { get; } = [];
    public List<LibraryScope> RandomCallScopes { get; } = [];

    /// <summary>Every orderedRecentIds list passed to <see cref="GetRotationCandidateAsync"/>, in call order.</summary>
    public List<IReadOnlyList<string>> RotationCallOrderedRecentIds { get; } = [];

    /// <summary>Every artistSeparation value passed to <see cref="GetRotationCandidateAsync"/>, in call order.</summary>
    public List<int> RotationCallArtistSeparations { get; } = [];

    /// <summary>Every scope passed to <see cref="GetRotationCandidateAsync"/>, in call order.</summary>
    public List<LibraryScope> RotationCallScopes { get; } = [];

    /// <summary>
    /// Forces every <see cref="GetRotationCandidateAsync"/> result to carry <c>RepeatedArtist=true</c>
    /// — the one relaxation flag the default derivation (<c>RepeatedRecent</c> from
    /// <c>orderedRecentIds.Contains</c>) cannot express by construction. Defaults false.
    /// </summary>
    public bool ScriptedRepeatedArtist { get; set; }

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct)
        => Task.FromResult(pool.FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct)
        => Task.FromResult(pool.FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
    {
        RandomCallExcludeLists.Add(excludeIds);
        RandomCallScopes.Add(scope);
        return Task.FromResult(pool.Count == 0 ? null : pool[0]);
    }

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        RotationCallOrderedRecentIds.Add(orderedRecentIds);
        RotationCallArtistSeparations.Add(artistSeparation);
        RotationCallScopes.Add(scope);

        if (pool.Count == 0) return Task.FromResult<RotationCandidate?>(null);

        var media = pool[nextIndex % pool.Count];
        nextIndex++;
        return Task.FromResult<RotationCandidate?>(new RotationCandidate(
            media,
            RepeatedRecent: orderedRecentIds.Contains(media.MediaId),
            RepeatedArtist: ScriptedRepeatedArtist));
    }

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
        Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    // Not exercised by orchestrator selection specs — facets are a curation-console concern (SPEC F52.1).
    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);
}
