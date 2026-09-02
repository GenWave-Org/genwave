using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary><see cref="ILibraryRepository"/>/<see cref="IAdminLibraryWrite"/> double that always
/// throws — simulates an unexpected DB fault so a test can prove <see cref="AdsLibrarySeeder.SeedAsync"/>
/// degrades to <see cref="AdsLibrarySeedOutcome.Failed"/> instead of propagating (the Story080
/// <c>ThrowingLibraryRepository</c> shape).</summary>
public sealed class ThrowingLibraryStore : ILibraryRepository, IAdminLibraryWrite
{
    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the ads library seed.");

    public Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the ads library seed.");
}
