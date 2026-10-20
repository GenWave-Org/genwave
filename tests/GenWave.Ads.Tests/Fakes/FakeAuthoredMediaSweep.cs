using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>In-memory <see cref="IAuthoredMediaSweep"/> (gh-#3): <see cref="Rows"/> is what the list
/// read returns, already filtered; a delete removes from it.</summary>
public sealed class FakeAuthoredMediaSweep : IAuthoredMediaSweep
{
    public List<AuthoredMediaFile> Rows { get; } = [];
    public List<long> Deleted { get; } = [];
    public long? ListedLibraryId { get; private set; }

    public Task<IReadOnlyList<AuthoredMediaFile>> ListIneligibleAsync(
        long libraryId, DateTimeOffset modifiedBefore, CancellationToken ct)
    {
        ListedLibraryId = libraryId;
        return Task.FromResult<IReadOnlyList<AuthoredMediaFile>>(Rows.ToList());
    }

    public Task<bool> DeleteIneligibleAsync(long mediaId, CancellationToken ct)
    {
        var removed = Rows.RemoveAll(m => m.Id == mediaId) > 0;
        if (removed)
            Deleted.Add(mediaId);
        return Task.FromResult(removed);
    }
}
