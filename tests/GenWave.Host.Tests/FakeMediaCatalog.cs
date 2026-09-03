using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scripts the catalog (PRD §4.2, SEAM 2) for selection tests and records the exclude list passed to
/// <see cref="GetRandomReadyAsync"/> so a test can assert the recently-aired ids flow through.
/// <paramref name="statusCounts"/> configures <see cref="GetStatusCountsAsync"/>'s return value
/// (default all-zero) for GET /api/status specs (STORY-084); every call's scope is recorded in
/// <see cref="StatusCountsCalls"/> so a test can assert the SafeScope actually passed through.
///
/// <see cref="GetRandomPlayableAsync"/> (SPEC F158.4, PLAN T395 review finding-8) is overridden
/// EXPLICITLY — a distinct <see cref="PlayableCalls"/> list, never folded into
/// <see cref="RandomCalls"/> — so a test can tell "the safe-track/legacy path called
/// GetRandomReadyAsync" apart from "the fenced /media/random path called GetRandomPlayableAsync".
/// Left unoverridden, this fake would inherit <c>IMediaCatalog</c>'s own DIM default (which
/// delegates to <see cref="GetRandomReadyAsync"/>), and a regression that reverted
/// <c>MediaEndpoints</c>'s <c>/media/random</c> route back to calling
/// <see cref="GetRandomReadyAsync"/> directly would go undetected — both would land in the SAME
/// <see cref="RandomCalls"/> list either way.
/// </summary>
sealed class FakeMediaCatalog(MediaReference? ready, CatalogStatusCounts? statusCounts = null) : IMediaCatalog
{
    public List<IReadOnlyList<string>> RandomCalls { get; } = [];
    public List<IReadOnlyList<string>> PlayableCalls { get; } = [];
    public List<LibraryScope> StatusCountsCalls { get; } = [];

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct)
        => Task.FromResult(ready is not null && ready.MediaId == mediaId ? ready : null);

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct)
        => Task.FromResult(ready is not null && ready.MediaId == mediaId ? ready : null);

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
    {
        RandomCalls.Add(excludeIds);
        return Task.FromResult(ready);
    }

    public Task<MediaReference?> GetRandomPlayableAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
    {
        PlayableCalls.Add(excludeIds);
        return Task.FromResult(ready);
    }

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct)
        => Task.FromResult(ready is null ? null : new RotationCandidate(ready, RepeatedRecent: orderedRecentIds.Contains(ready.MediaId), RepeatedArtist: false));

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct)
        => Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct)
    {
        StatusCountsCalls.Add(safeScope);
        return Task.FromResult(statusCounts ?? new CatalogStatusCounts(0, 0, 0, 0, 0));
    }

    // Not exercised by these selection/status specs — facets are a curation-console concern (SPEC F52.1).
    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);
}
