using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IIconPackStore"/> double (SPEC F130, STORY-337, PLAN T303) — mirrors
/// <see cref="FakeAvatarPackStore"/>'s own upsert-by-slug contract (T290): a new slug inserts a row, an
/// existing one replaces its <c>definition</c>/<c>imported_from</c>/<c>imported_at</c> unconditionally.
/// This double proves the CONTRACT <c>IconPackController</c>'s install/uninstall/list/active routes rely
/// on; the real repository's own SQL — including the true jsonb round trip — is
/// <c>Story333_VisualLayerStores.cs</c>'s own coverage against real Postgres.
/// </summary>
sealed class FakeIconPackStore : IIconPackStore
{
    readonly Dictionary<string, IconPack> bySlug = new(StringComparer.Ordinal);

    /// <summary>The number of <see cref="UpsertAsync"/> calls that actually wrote — lets a spec assert
    /// the install route reaches the store exactly once per install (mirrors
    /// <see cref="FakeAvatarPackStore.UpsertCallCount"/>'s own reasoning).</summary>
    public int UpsertCallCount { get; private set; }

    public Task UpsertAsync(string slug, string definition, string importedFrom, CancellationToken ct)
    {
        UpsertCallCount++;
        bySlug[slug] = new IconPack(slug, definition, importedFrom, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task<IconPack?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(bySlug.TryGetValue(slug, out var pack) ? pack : null);

    public Task<IReadOnlyList<IconPack>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<IconPack>>(bySlug.Values.ToList());

    public Task<IReadOnlyList<string>> GetAllSlugsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(bySlug.Keys.ToList());

    public Task<bool> DeleteAsync(string slug, CancellationToken ct) =>
        Task.FromResult(bySlug.Remove(slug));
}
