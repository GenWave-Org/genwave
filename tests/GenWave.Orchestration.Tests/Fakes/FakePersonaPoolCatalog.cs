using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// <see cref="IMediaCatalog"/> double for STORY-213/T64 specs that exercise the wired
/// <see cref="RankerPersonaPickProvider"/> chain end to end (SPEC F82.6's per-pick debug line) — the
/// one fake in this suite that overrides <see cref="GetEnvelopeCandidatePoolAsync"/> with a real,
/// caller-supplied multi-row pool rather than falling through to <see cref="IMediaCatalog"/>'s own
/// single-candidate default (see that method's own remarks). Every other member is a thin stub:
/// these specs only ever reach rung 0 (the persona pick wins), so the envelope-only ladder below it
/// is never exercised through this fake.
/// </summary>
sealed class FakePersonaPoolCatalog(IReadOnlyList<EnvelopeCandidateRow> pool) : IMediaCatalog
{
    public Task<IReadOnlyList<EnvelopeCandidateRow>> GetEnvelopeCandidatePoolAsync(
        LibraryScope scope,
        IReadOnlyList<string> orderedRecentIds,
        int artistSeparation,
        SegmentEnvelope envelope,
        int limit,
        CancellationToken ct) =>
        Task.FromResult(pool);

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct) =>
        Task.FromResult(pool.Select(row => row.Media).FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct) =>
        Task.FromResult(pool.Select(row => row.Media).FirstOrDefault(m => m.MediaId == mediaId));

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        Task.FromResult(pool.Count == 0 ? null : pool[0].Media);

    // Not exercised — these specs only ever reach rung 0 (the persona pick wins), so the
    // envelope-only ladder below it never runs.
    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct) =>
        Task.FromResult<RotationCandidate?>(null);

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        Task.FromResult(new PagedResult<MediaReference>([], 0, 0));

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
        Task.FromResult(new CatalogStatusCounts(0, 0, 0, 0, 0));

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);
}
