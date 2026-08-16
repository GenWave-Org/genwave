using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAvatarPackStore"/> double (SPEC F128.3, STORY-332, PLAN T293) — mirrors
/// <see cref="FakeFontPackStore"/>'s own upsert-by-slug contract (T290): a new slug inserts a row, an
/// existing one replaces its pack row AND its entire item set unconditionally
/// (<c>AvatarPackRepository</c>'s own delete-then-reinsert contract, at the fake-store altitude). This
/// double proves the CONTRACT <c>AvatarPackController</c>'s install route relies on; the real
/// repository's own SQL — including the true no-partial-installs rollback a fake dictionary write
/// cannot honestly repeat — is T290's own coverage against real Postgres.
/// </summary>
sealed class FakeAvatarPackStore : IAvatarPackStore
{
    readonly Dictionary<string, AvatarPack> bySlug = new(StringComparer.Ordinal);

    /// <summary>The number of <see cref="UpsertAsync"/> calls that actually wrote — lets a spec assert
    /// the install route reaches the store exactly once per install (mirrors
    /// <see cref="FakeFontPackStore.UpsertCallCount"/>'s own reasoning).</summary>
    public int UpsertCallCount { get; private set; }

    public Task UpsertAsync(
        string slug, string definition, string importedFrom,
        IReadOnlyList<AvatarPackItemInput> items, CancellationToken ct)
    {
        UpsertCallCount++;
        var storedItems = items
            .Select(i => new AvatarPackItem(i.Name, i.SuggestedPersona, i.Bytes, i.ByteSize, i.Sha256))
            .ToList();
        bySlug[slug] = new AvatarPack(slug, definition, importedFrom, DateTime.UtcNow, storedItems);
        return Task.CompletedTask;
    }

    public Task<AvatarPack?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(bySlug.TryGetValue(slug, out var pack) ? pack : null);

    /// <summary>Every installed pack, each with its own item name/suggestion metadata folded in but NO
    /// bytes — MIRRORS the real <see cref="IAvatarPackStore.GetAllAsync"/> contract exactly (review
    /// finding B1: the store now widens this read to include item metadata directly rather than
    /// forcing a caller into a per-pack <see cref="GetBySlugAsync"/> round trip just to read it off a
    /// bytes-carrying shape). A Fact that needs a specific pack's own item BYTES still reads them
    /// through <see cref="GetBySlugAsync"/> instead — shape-identical between this fake and the real
    /// <c>AvatarPackRepository</c>, unlike this method.</summary>
    public Task<IReadOnlyList<AvatarPackSummary>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AvatarPackSummary>>(bySlug.Values
            .Select(pack => new AvatarPackSummary(
                pack.Slug, pack.Definition, pack.ImportedFrom, pack.ImportedAt,
                pack.Items.Select(item => new AvatarPackItemSummary(item.Name, item.SuggestedPersona)).ToList()))
            .ToList());

    public Task<bool> DeleteAsync(string slug, CancellationToken ct) =>
        Task.FromResult(bySlug.Remove(slug));
}
