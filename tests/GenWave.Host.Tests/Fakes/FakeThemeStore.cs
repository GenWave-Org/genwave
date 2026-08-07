using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IThemeStore"/> double (SPEC F103.7/F104.13, STORY-271, PLAN T181/T207) —
/// mirrors <c>GenWave.MediaLibrary.Station.ThemeRepository</c>'s own upsert-by-slug contract (a new
/// slug inserts a row, an existing one replaces its definition and refreshes
/// <c>imported_from</c>/<c>imported_at</c> unconditionally, <c>imported_at</c> stamped only when
/// <c>importedFrom</c> is non-null — PLAN T207, the save-as-own write's <c>null</c> provenance) without
/// a real Postgres fixture, which this project has none of (mirrors <see cref="FakeScheduleStore"/>'s
/// own remarks). This double proves the CONTRACT every future consumer (<c>ThemeCatalog</c>, T182; the
/// import route, T184; the save-as-own route, T207) will rely on; the real repository's own SQL is
/// proven wherever its first Postgres-backed spec lands, exactly like <c>ScheduleRepository</c>'s split
/// between this fake and <c>GenWave.MediaLibrary.Tests/Specs/Story240_ScheduleStore.cs</c>.
/// </summary>
sealed class FakeThemeStore : IThemeStore
{
    readonly Dictionary<string, OwnerTheme> bySlug = new(StringComparer.Ordinal);

    public Task UpsertAsync(string slug, string definition, string? importedFrom, CancellationToken ct)
    {
        var createdAt = bySlug.TryGetValue(slug, out var existing) ? existing.CreatedAt : DateTime.UtcNow;
        // Mirrors ThemeRepository.UpsertAsync's own CASE expression (PLAN T207) — imported_at stays
        // null exactly when importedFrom does, OwnerTheme's own documented invariant.
        var importedAt = importedFrom is null ? (DateTime?)null : DateTime.UtcNow;
        bySlug[slug] = new OwnerTheme(slug, definition, importedFrom, importedAt, createdAt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OwnerTheme>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<OwnerTheme>>(bySlug.Values.ToList());

    public Task<OwnerTheme?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(bySlug.TryGetValue(slug, out var theme) ? theme : null);
}
