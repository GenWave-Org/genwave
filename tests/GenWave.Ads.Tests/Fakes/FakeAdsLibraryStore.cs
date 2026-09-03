using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// In-memory library catalog implementing both <see cref="ILibraryRepository"/> (read) and
/// <see cref="IAdminLibraryWrite"/> (create) — the two seams <see cref="AdsLibrarySeeder"/> and
/// <see cref="LibraryAdSpotSource"/> use (the Story080 <c>FakeLibraryStore</c> shape, this project's
/// own copy since Ads.Tests carries no DB fixture — GenWave.Ads itself never references Npgsql, so
/// these interfaces' only real backing store lives in GenWave.MediaLibrary, out of this project's
/// reach).
/// </summary>
public sealed class FakeAdsLibraryStore : ILibraryRepository, IAdminLibraryWrite
{
    readonly List<LibraryAdminInfo> libraries = [];
    long nextId = 1;

    public int CreateCallCount { get; private set; }

    /// <summary>Pre-seeds an existing library (simulates one the seed/source should find and reuse).</summary>
    public long AddExisting(string name, int mediaCount = 0)
    {
        var id = nextId++;
        libraries.Add(new LibraryAdminInfo(id, name, mediaCount));
        return id;
    }

    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>(
            libraries.Where(l => ids.Contains(l.Id)).Select(l => new LibraryInfo(l.Id, l.Name)).ToList());

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryAdminInfo>>(libraries.ToList());

    public Task<LibraryAdminInfo?> GetByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult(libraries.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal)));

    public Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct)
    {
        CreateCallCount++;
        if (libraries.Any(l => string.Equals(l.Name, name, StringComparison.Ordinal)))
            return Task.FromResult<LibraryWriteResult>(new LibraryWriteResult.NameConflict());

        var id = nextId++;
        libraries.Add(new LibraryAdminInfo(id, name, 0));
        return Task.FromResult<LibraryWriteResult>(new LibraryWriteResult.Created(id));
    }

    public Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the ads library seed.");

    public Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the ads library seed.");
}
