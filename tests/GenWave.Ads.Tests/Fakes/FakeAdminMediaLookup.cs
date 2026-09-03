using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary><see cref="IAdminMediaLookup"/> double for <see cref="AdRenderService"/>'s own
/// <c>ResolveBedAsync</c> specs (T401 review F1) — an in-memory row set keyed by id; an id never
/// added resolves <see langword="null"/>, matching <see cref="AdRenderService"/>'s own "unknown bed
/// media" failure path.</summary>
public sealed class FakeAdminMediaLookup : IAdminMediaLookup
{
    readonly Dictionary<long, (AdminMediaDto Row, long LibraryId)> rows = [];

    public FakeAdminMediaLookup Add(long id, AdminMediaDto row, long libraryId)
    {
        rows[id] = (row, libraryId);
        return this;
    }

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct) =>
        Task.FromResult(rows.TryGetValue(id, out var found) ? found : ((AdminMediaDto Row, long LibraryId)?)null);
}
