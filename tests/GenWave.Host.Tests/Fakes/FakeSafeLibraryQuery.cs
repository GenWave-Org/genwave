using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>The safe library and the rows in it, for specs that exercise <see cref="GenWave.Host.Seeding.SeedRowFinder"/> for real.</summary>
sealed class FakeSafeLibraryQuery(long libraryId, IReadOnlyList<AdminMediaDto> rows) : ILibraryRepository, IAdminMediaQuery
{
    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>([new LibraryInfo(libraryId, "safe")]);

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryAdminInfo>>([new LibraryAdminInfo(libraryId, "safe", rows.Count)]);

    public Task<LibraryAdminInfo?> GetByNameAsync(string name, CancellationToken ct) =>
        Task.FromResult<LibraryAdminInfo?>(name == "safe" ? new LibraryAdminInfo(libraryId, "safe", rows.Count) : null);

    public Task<PagedResult<AdminMediaDto>> ListAdminAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        Task.FromResult(new PagedResult<AdminMediaDto>(rows, rows.Count, 1));
}
