using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// gh-#99 — in-memory <see cref="IMediaLibraryMembership"/>: a fixed (media id → library id) map,
/// intersected with the asked-about libraries exactly the way the SQL implementation's WHERE does.
/// The no-args construction knows no rows — every membership answer is the empty set, the pre-#99
/// behavior every pre-existing spec assumes.
/// </summary>
sealed class FakeMediaLibraryMembership(IReadOnlyDictionary<long, long>? libraryIdByMediaId = null)
    : IMediaLibraryMembership
{
    readonly IReadOnlyDictionary<long, long> libraryIdByMediaId =
        libraryIdByMediaId ?? new Dictionary<long, long>();

    public Task<IReadOnlySet<long>> FilterToLibrariesAsync(
        IReadOnlyCollection<long> mediaIds, LibraryScope libraries, CancellationToken ct)
    {
        IReadOnlySet<long> result = mediaIds
            .Where(id => this.libraryIdByMediaId.TryGetValue(id, out var libraryId) && libraries.LibraryIds.Contains(libraryId))
            .ToHashSet();
        return Task.FromResult(result);
    }
}
