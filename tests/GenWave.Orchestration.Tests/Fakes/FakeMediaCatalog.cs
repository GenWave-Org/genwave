using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Scripted catalog double for orchestrator unit tests. Captures every call to
/// <see cref="GetRandomReadyAsync"/> and <see cref="GetRotationCandidateAsync"/> so tests can assert
/// on exclude-id filtering, scope passing, and the ordered-recent/artist-separation args SPEC F41.1
/// added. <c>ready == null</c> makes <see cref="GetRotationCandidateAsync"/> return null (the
/// zero-playable-pool case, F41.2); <see cref="ScriptedRepeatedArtist"/> forces the RepeatedArtist
/// flag a non-null candidate carries.
/// </summary>
sealed class FakeMediaCatalog(MediaReference? ready) : IMediaCatalog
{
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
        => Task.FromResult(ready is not null && ready.MediaId == mediaId ? ready : null);

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
    {
        RandomCallExcludeLists.Add(excludeIds);
        RandomCallScopes.Add(scope);
        return Task.FromResult(ready);
    }

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
    {
        RotationCallOrderedRecentIds.Add(orderedRecentIds);
        RotationCallArtistSeparations.Add(artistSeparation);
        RotationCallScopes.Add(scope);
        return Task.FromResult(ready is null
            ? null
            : new RotationCandidate(
                ready,
                RepeatedRecent: orderedRecentIds.Contains(ready.MediaId),
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
